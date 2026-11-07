using System.Text.Json;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ExplorationHistoryViewModelTests
{
    [Fact]
    public async Task AutomaticBalanceBackfillsAndTheChoicePersistsWithoutResettingSurveyTotals()
    {
        using var fixture = new Fixture();
        await using ExplorationHistoryViewModel model = fixture.Create();
        model.SetCommander("F100", "History Commander", true, false);
        await model.PendingSynchronization;
        Assert.Null(model.AutomaticEstimatedValue);
        Assert.Equal("23,106 CR", model.UnsoldText);

        model.AutoResetEstimatedValue = true;
        await model.PendingPreferenceSave;
        Assert.Equal(23106, model.AutomaticEstimatedValue);
        CommanderProfileLoadResult profile = await fixture.Profiles.LoadAsync("F100", true);
        Assert.True(profile.Data!.AutoResetExplorationValue);
        Assert.Equal(42, profile.Data.Exploration.JumpCount);
        Assert.Equal(9, profile.Data.Exploration.ScanCount);
        Assert.Equal(777, profile.Data.Exploration.EstimatedRewards);
        using var stored = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.ProfilePath));
        Assert.Equal("preserved", stored.RootElement.GetProperty("futureField").GetString());

        model.AutoResetEstimatedValue = false;
        await model.PendingPreferenceSave;
        Assert.Null(model.AutomaticEstimatedValue);
        Assert.False((await fixture.Profiles.LoadAsync("F100", true)).Data!.AutoResetExplorationValue);
    }

    [Fact]
    public async Task NewCollectionsDoNotCloseAnInspectorWhenItsRowMovesToAnotherPage()
    {
        using var fixture = new Fixture();
        for (int index = 1; index <= 49; index++)
        {
            await File.AppendAllLinesAsync(
                fixture.JournalPath("F100"),
                [
                    $$"""{"timestamp":"2026-10-01T13:00:00Z","event":"Location","StarSystem":"Survey {{index}}","SystemAddress":{{index}}}""",
                    $$"""{"timestamp":"2026-10-01T13:01:00Z","event":"Scan","SystemAddress":{{index}},"BodyID":0,"BodyName":"Survey {{index}} A","StarType":"NS","StellarMass":1.4}""",
                ]
            );
        }
        await using ExplorationHistoryViewModel model = fixture.Create();
        model.SetCommander("F100", "Survey commander", true, true);
        await model.PendingSynchronization;
        Assert.Equal(50, model.Rows.Count);
        model.SelectedRow = Assert.Single(model.Rows, row => row.SystemName == "Alpha Survey");
        await model.PendingDetails;
        await File.AppendAllLinesAsync(
            fixture.JournalPath("F100"),
            [
                """{"timestamp":"2026-10-01T14:00:00Z","event":"Location","StarSystem":"Newest survey","SystemAddress":200}""",
                """{"timestamp":"2026-10-01T14:01:00Z","event":"Scan","SystemAddress":200,"BodyID":0,"BodyName":"Newest survey A","StarType":"NS","StellarMass":1.4}""",
            ]
        );
        await model.RefreshAsync();
        await model.PendingDetails;
        Assert.DoesNotContain(model.Rows, row => row.SystemName == "Alpha Survey");
        Assert.True(model.HasSelection);
        Assert.Equal("Alpha Survey", model.SelectedRow?.SystemName);
        Assert.Equal("Alpha Survey A", Assert.Single(model.Bodies).Name);
    }

    [Fact]
    public async Task ARecoveredFilterQueryClearsItsErrorAndPreservesLaterClipboardMessages()
    {
        using var fixture = new Fixture();
        int failNext = 0;
        await using ExplorationHistoryViewModel model = fixture.Create(
            (catalog, query, token) =>
                Interlocked.Exchange(ref failNext, 0) == 1
                    ? Task.FromException<ExplorationHistoryPage>(new IOException("Read interrupted."))
                    : catalog.QueryAsync(query, token)
        );
        model.SetCommander("F100", "Survey commander", true, true);
        await model.PendingSynchronization;
        Interlocked.Exchange(ref failNext, 1);
        model.SearchText = "missing";
        await model.PendingQuery;
        Assert.Equal("Exploration history could not be read: Read interrupted.", model.StatusMessage);
        model.SearchText = "Alpha";
        await model.PendingQuery;
        Assert.Equal("Alpha Survey", Assert.Single(model.Rows).SystemName);
        Assert.Equal("History cataloged from 1 journal file for Survey commander.", model.StatusMessage);
        model.SetClipboardWriter(_ => Task.CompletedTask);
        model.Rows[0].CopySystemNameCommand.Execute(null);
        model.SearchText = "Survey";
        await model.PendingQuery;
        Assert.Equal("Copied system name: Alpha Survey", model.StatusMessage);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("dates")]
    [InlineData("query")]
    [InlineData("storage")]
    public async Task MissingOriginalScansWithholdTheAutomaticBalanceUntilMappingCanRecover(string readFailure)
    {
        using var fixture = new Fixture(false);
        await File.AppendAllTextAsync(
            fixture.JournalPath("F100"),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Alpha Survey 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}"""
                + "\n"
        );
        bool failReads = false;
        await using var model = new ExplorationHistoryViewModel(
            fixture.Data,
            fixture.Profiles,
            () => [fixture.Journals],
            queryReader: (catalog, query, token) =>
                failReads
                    ? Task.FromException<ExplorationHistoryPage>(new IOException("Page unavailable."))
                    : catalog.QueryAsync(query, token),
            summaryReader: (catalog, token) =>
                failReads && readFailure == "storage"
                    ? Task.FromException<ExplorationHistorySummary>(new IOException("Summary unavailable."))
                    : catalog.ReadSummaryAsync(token)
        );
        model.SetCommander("F100", "Survey commander", true, true);
        await model.PendingSynchronization;
        Assert.Equal(449200, model.AutomaticEstimatedValue);
        if (readFailure == "dates")
        {
            model.FromDate = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
            model.ThroughDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            await model.PendingQuery;
        }
        failReads = readFailure is "query" or "storage";
        string path = fixture.JournalPath("F100");
        File.Move(path, path + ".saved");
        await File.WriteAllLinesAsync(
            Path.Combine(fixture.Journals, "Journal.2026-10-01T140000.01.log"),
            [
                """{"timestamp":"2026-10-01T14:00:00Z","event":"Fileheader","Odyssey":true}""",
                """{"event":"Commander","FID":"F100"}""",
                """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"BodyName":"Alpha Survey 1","ProbesUsed":5,"EfficiencyTarget":6}""",
            ]
        );
        await model.RefreshAsync();
        Assert.Null(model.AutomaticEstimatedValue);
        Assert.Equal("449,200 CR", Assert.Single(model.Rows).Estimate);
        Assert.Contains("Restore the journal", model.StatusMessage);
        File.Move(path + ".saved", path);
        failReads = false;
        model.FromDate = null;
        model.ThroughDate = null;
        await model.PendingQuery;
        await model.RefreshAsync();
        Assert.Equal(2700541, model.AutomaticEstimatedValue);
    }

    [Fact]
    public async Task APreFailurePageCannotRestoreEligibilityWhileRecoverySummaryIsLoading()
    {
        using var fixture = new Fixture(false);
        await File.AppendAllTextAsync(
            fixture.JournalPath("F100"),
            """{"event":"Scan","SystemAddress":100,"BodyID":1,"BodyName":"Alpha Survey 1","PlanetClass":"High metal content body","TerraformState":"Terraformable","MassEM":1,"WasDiscovered":false,"WasMapped":false}"""
                + "\n"
        );
        var queryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseQuery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var summaryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSummary = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int holdQuery = 0;
        bool holdSummary = false;
        await using var model = new ExplorationHistoryViewModel(
            fixture.Data,
            fixture.Profiles,
            () => [fixture.Journals],
            queryReader: async (catalog, query, token) =>
            {
                ExplorationHistoryPage page = await catalog.QueryAsync(query, token);
                if (Interlocked.Exchange(ref holdQuery, 0) == 1)
                {
                    queryEntered.TrySetResult();
                    await releaseQuery.Task.WaitAsync(token);
                }
                return page;
            },
            summaryReader: async (catalog, token) =>
            {
                ExplorationHistorySummary summary = await catalog.ReadSummaryAsync(token);
                if (holdSummary)
                {
                    summaryEntered.TrySetResult();
                    await releaseSummary.Task.WaitAsync(token);
                }
                return summary;
            }
        );
        try
        {
            model.SetCommander("F100", "Survey commander", true, true);
            await model.PendingSynchronization;
            Assert.Equal(449200, model.AutomaticEstimatedValue);
            Interlocked.Exchange(ref holdQuery, 1);
            model.SearchText = "Alpha";
            Task previous = model.PendingQuery;
            await queryEntered.Task;
            File.Move(fixture.JournalPath("F100"), fixture.JournalPath("F100") + ".saved");
            const string name = "Journal.2026-10-01T140000.01.log";
            const string mapping =
                """{"event":"SAAScanComplete","SystemAddress":100,"BodyID":1,"BodyName":"Alpha Survey 1","ProbesUsed":5,"EfficiencyTarget":6}""";
            await File.WriteAllTextAsync(
                Path.Combine(fixture.Journals, name),
                """
                {"timestamp":"2026-10-01T14:00:00Z","event":"Fileheader","Odyssey":true}
                {"event":"Commander","FID":"F100"}
                """
                    + "\n"
                    + mapping
                    + "\n"
            );
            Assert.True(
                SrvSurvey.Core.Journal.JournalEventEnvelope.TryParse(
                    mapping,
                    out SrvSurvey.Core.Journal.JournalEventEnvelope? observed,
                    out _
                )
            );
            holdSummary = true;
            model.Observe([observed! with { SourceRecordIdentity = name + ":0" }]);
            await summaryEntered.Task;
            releaseQuery.TrySetResult();
            await previous;
            Assert.Null(model.AutomaticEstimatedValue);
            releaseSummary.TrySetResult();
            await model.PendingSynchronization;
            Assert.Null(model.AutomaticEstimatedValue);
        }
        finally
        {
            releaseQuery.TrySetResult();
            releaseSummary.TrySetResult();
        }
    }

    [Fact]
    public async Task SelectingAnotherSystemStopsTheAbandonedOriginalRead()
    {
        using var fixture = new Fixture();
        await File.AppendAllLinesAsync(
            fixture.JournalPath("F100"),
            [
                """{"event":"Location","StarSystem":"Beta Survey","SystemAddress":200}""",
                """{"event":"Scan","SystemAddress":200,"BodyID":0,"BodyName":"Beta Survey A","StarType":"NS","StellarMass":1.4}""",
            ]
        );
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ExplorationHistoryDetails>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        CancellationToken abandoned = default;
        await using var model = new ExplorationHistoryViewModel(
            fixture.Data,
            fixture.Profiles,
            () => [fixture.Journals],
            detailReader: async (catalog, row, token) =>
            {
                if (row.SystemName == "Alpha Survey")
                {
                    abandoned = token;
                    entered.TrySetResult();
                    return await release.Task.WaitAsync(token);
                }
                return await catalog.ReadDetailsAsync(row, token);
            }
        );
        try
        {
            model.SetCommander("F100", "Survey commander", true, true);
            await model.PendingSynchronization;
            model.SelectedRow = Assert.Single(model.Rows, row => row.SystemName == "Alpha Survey");
            Task obsolete = model.PendingDetails;
            await entered.Task;
            model.SelectedRow = Assert.Single(model.Rows, row => row.SystemName == "Beta Survey");
            await model.PendingDetails;
            Assert.True(abandoned.IsCancellationRequested);
            await obsolete;
            Assert.Equal("Beta Survey A", Assert.Single(model.Bodies).Name);
            Assert.Equal("Beta Survey", model.DetailTitle);
        }
        finally
        {
            release.TrySetResult(new ExplorationHistoryDetails(0, []));
        }
    }

    [Fact]
    public async Task SearchSelectionAndCopyExposeOnlyTheRequestedSystemsOriginalData()
    {
        using var fixture = new Fixture();
        await using ExplorationHistoryViewModel model = fixture.Create();
        model.SetCommander("F100", "History Commander", true, true);
        await model.PendingSynchronization;
        model.SearchText = "alph";
        await model.PendingQuery;
        ExplorationHistoryRowViewModel row = Assert.Single(model.Rows);
        Assert.Equal("Alpha Survey", row.SystemName);
        model.SelectedRow = row;
        await model.PendingDetails;
        Assert.Equal("NS", Assert.Single(model.Bodies).BodyClass);
        Assert.Contains("StellarMass", model.RawJson);
        Assert.True(model.HasSelection);
        string? copied = null;
        model.SetClipboardWriter(value =>
        {
            copied = value;
            return Task.CompletedTask;
        });
        row.CopySystemNameCommand.Execute(null);
        Assert.Equal("Alpha Survey", copied);
        model.SelectedRow = null;
        await model.PendingDetails;
        Assert.Empty(model.Bodies);
        Assert.Equal(string.Empty, model.RawJson);
        Assert.False(model.HasSelection);
    }

    [Fact]
    public async Task SwitchingCommandersCancelsOldWorkAndDoesNotReuseItsRowsOrTrackingChoice()
    {
        using var fixture = new Fixture();
        fixture.AddCommander("F200", "Second Survey", 200);
        await using ExplorationHistoryViewModel model = fixture.Create();
        model.SetCommander("F100", "First", true, true);
        model.SetCommander("F200", "Second", true, false);
        await model.PendingSynchronization;
        Assert.Equal("Second", model.CommanderLabel);
        Assert.False(model.AutoResetEstimatedValue);
        Assert.Null(model.AutomaticEstimatedValue);
        Assert.Equal("Second Survey", Assert.Single(model.Rows).SystemName);
        Assert.Empty(model.Bodies);
    }

    [Fact]
    public async Task ACommanderHeaderWithoutExplorationRecordsDoesNotReplaceASavedEstimateWithZero()
    {
        using var fixture = new Fixture(false);
        await using ExplorationHistoryViewModel model = fixture.Create();
        model.SetCommander("F100", "History Commander", true, true);
        await model.PendingSynchronization;
        Assert.Null(model.AutomaticEstimatedValue);
        Assert.Empty(model.Rows);
        Assert.Contains("saved estimate", model.ValueModeDescription);
    }

    [Fact]
    public async Task MainWorkspaceUsesTheUnsoldBalanceOnlyWhenTheCommanderEnablesIt()
    {
        using var fixture = new Fixture();
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            fixture.Journals,
            builder =>
                builder.WithAppDataPaths(
                    new(Path.Combine(fixture.Root, "config"), fixture.Data, Path.Combine(fixture.Root, "cache"), [])
                )
        );
        await main.RefreshAsync();
        await main.ExplorationHistory.PendingSynchronization;
        Assert.Equal("777 CR", main.EstimatedExplorationValue);
        main.ExplorationHistory.AutoResetEstimatedValue = true;
        await main.ExplorationHistory.PendingPreferenceSave;
        Assert.Equal("23,106 CR", main.EstimatedExplorationValue);
        await main.ResetExplorationAsync();
        Assert.Equal(
            "Select Confirm reset to clear the saved survey totals. Automatic tracking keeps the journal-derived unsold balance.",
            main.ExplorationStatusMessage
        );
        main.CancelResetExplorationCommand.Execute(null);
        Assert.Equal("42", main.ExplorationJumps);
        main.ExplorationHistory.AutoResetEstimatedValue = false;
        await main.ExplorationHistory.PendingPreferenceSave;
        Assert.Equal("777 CR", main.EstimatedExplorationValue);
        Assert.Equal("42", main.ExplorationJumps);
    }

    [Fact]
    public async Task UnavailableCatalogKeepsTheSavedValueAndRecoversWhenStorageIsRestored()
    {
        using var fixture = new Fixture();
        string blocked = Path.Combine(fixture.Data, "commanders");
        await File.WriteAllTextAsync(blocked, "This file blocks the catalog directory.");
        await using ExplorationHistoryViewModel model = fixture.Create();
        model.SetCommander("F100", "Survey commander", true, true);
        await model.PendingSynchronization;
        Assert.Null(model.AutomaticEstimatedValue);
        Assert.Contains("could not be updated", model.StatusMessage);
        File.Delete(blocked);
        await model.RefreshAsync();
        Assert.Equal(23106, model.AutomaticEstimatedValue);
        Assert.Equal("Alpha Survey", Assert.Single(model.Rows).SystemName);
        model.ThroughDate = DateTime.MaxValue;
        await model.PendingQuery;
        Assert.Equal("Alpha Survey", Assert.Single(model.Rows).SystemName);
    }

    [Fact]
    public async Task AnUnavailableClipboardExplainsTheFailureWithoutDiscardingTheHistory()
    {
        using var fixture = new Fixture();
        await using ExplorationHistoryViewModel model = fixture.Create();
        model.SetCommander("F100", "Survey commander", true, false);
        await model.PendingSynchronization;
        model.SetClipboardWriter(_ => Task.FromException(new NotSupportedException("Clipboard is unavailable.")));
        ExplorationHistoryRowViewModel row = Assert.Single(model.Rows);
        row.CopySystemNameCommand.Execute(null);
        Assert.Equal("The system name could not be copied: Clipboard is unavailable.", model.StatusMessage);
        Assert.Equal("Alpha Survey", Assert.Single(model.Rows).SystemName);
    }

    [Fact]
    public async Task AnInvalidCatalogUpdateCanRecoverAndSwitchCommandersAfterSourcesAreRestored()
    {
        using var fixture = new Fixture();
        fixture.AddCommander("F200", "Second Survey", 200);
        string original = await File.ReadAllTextAsync(fixture.JournalPath("F100"));
        string second = await File.ReadAllTextAsync(fixture.JournalPath("F200"));
        await using ExplorationHistoryViewModel model = fixture.Create();
        model.SetCommander("F100", "Survey commander", true, true);
        await model.PendingSynchronization;
        File.Delete(fixture.JournalPath("F200"));
        await File.WriteAllTextAsync(
            fixture.JournalPath("F100"),
            """{"timestamp":"2026-10-01T12:00:00Z","event":"Fileheader","Odyssey":true}""" + "\n"
        );
        await model.RefreshAsync();
        Assert.Contains("Restore those files", model.StatusMessage);
        Assert.Equal(23106, model.AutomaticEstimatedValue);
        await File.WriteAllTextAsync(fixture.JournalPath("F100"), original);
        await File.WriteAllTextAsync(fixture.JournalPath("F200"), second);
        await model.RefreshAsync();
        Assert.Equal("Alpha Survey", Assert.Single(model.Rows).SystemName);
        model.SetCommander("F200", "Second", true, true);
        await model.PendingSynchronization;
        Assert.Equal("Second Survey", Assert.Single(model.Rows).SystemName);
        Assert.Equal(23106, model.AutomaticEstimatedValue);
    }

    [Fact]
    public async Task AnOlderImportKeepsTheSelectedSystemInsteadOfFollowingItsReusedDatabaseId()
    {
        using var fixture = new Fixture();
        await using ExplorationHistoryViewModel model = fixture.Create();
        model.SetCommander("F100", "Survey commander", true, true);
        await model.PendingSynchronization;
        model.SelectedRow = Assert.Single(model.Rows);
        await model.PendingDetails;
        string older = (await File.ReadAllTextAsync(fixture.JournalPath("F100")))
            .Replace("2026-10-01", "2026-09-30", StringComparison.Ordinal)
            .Replace("Alpha Survey", "Beta Survey", StringComparison.Ordinal)
            .Replace("\"SystemAddress\":100", "\"SystemAddress\":200", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(fixture.Journals, "Journal.2026-09-30T120000.01.log"), older);
        await model.RefreshAsync();
        await model.PendingDetails;
        Assert.Equal("Alpha Survey", model.SelectedRow?.SystemName);
        Assert.Equal("Alpha Survey", model.DetailTitle);
        Assert.Equal("Alpha Survey A", Assert.Single(model.Bodies).Name);
    }

    [Fact]
    public async Task RefreshKeepsTheSelectedLostCollectionWhenBothCollectionDatesAreUnknown()
    {
        using var fixture = new Fixture(false);
        await File.AppendAllLinesAsync(
            fixture.JournalPath("F100"),
            [
                """{"event":"Scan","SystemAddress":100,"BodyID":0,"BodyName":"Original survey","StarType":"NS","StellarMass":1.4}""",
                """{"event":"Resurrect","Option":"rebuy","Cost":0}""",
                """{"event":"Scan","SystemAddress":100,"BodyID":0,"BodyName":"New survey","StarType":"NS","StellarMass":1.4}""",
            ]
        );
        await using ExplorationHistoryViewModel model = fixture.Create();
        model.SetCommander("F100", "Survey commander", true, true);
        await model.PendingSynchronization;
        Assert.Equal(2, model.Rows.Count);
        Assert.All(model.Rows, row => Assert.Null(row.Data.FirstCollected));
        model.SelectedRow = Assert.Single(model.Rows, row => row.Data.Status == ExplorationDataStatus.Lost);
        await model.PendingDetails;
        await model.RefreshAsync();
        await model.PendingDetails;
        Assert.Equal(ExplorationDataStatus.Lost, model.SelectedRow?.Data.Status);
        Assert.Equal("Original survey", Assert.Single(model.Bodies).Name);
    }

    [Fact]
    public async Task AnOlderScanExtendingTheSelectedCollectionKeepsItsDetailsOpen()
    {
        using var fixture = new Fixture();
        await using ExplorationHistoryViewModel model = fixture.Create();
        model.SetCommander("F100", "Survey commander", true, true);
        await model.PendingSynchronization;
        model.SelectedRow = Assert.Single(model.Rows);
        await model.PendingDetails;
        string older = (await File.ReadAllTextAsync(fixture.JournalPath("F100")))
            .Replace("2026-10-01", "2026-09-30", StringComparison.Ordinal)
            .Replace("\"BodyID\":0", "\"BodyID\":1", StringComparison.Ordinal)
            .Replace("Alpha Survey A", "Alpha Survey B", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(fixture.Journals, "Journal.2026-09-30T120000.01.log"), older);
        await model.RefreshAsync();
        await model.PendingDetails;
        Assert.Equal("Alpha Survey", model.SelectedRow?.SystemName);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 12, 2, 0, TimeSpan.Zero), model.SelectedRow?.Data.FirstCollected);
        Assert.Equal((string[])["Alpha Survey A", "Alpha Survey B"], model.Bodies.Select(body => body.Name));
    }

    [Fact]
    public async Task ReplacingTheSelectedOriginalRecordClearsDetailsInsteadOfFollowingAReusedId()
    {
        using var fixture = new Fixture();
        await using ExplorationHistoryViewModel model = fixture.Create();
        model.SetCommander("F100", "Survey commander", true, true);
        await model.PendingSynchronization;
        model.SelectedRow = Assert.Single(model.Rows);
        await model.PendingDetails;
        string replacement = (await File.ReadAllTextAsync(fixture.JournalPath("F100"))).Replace(
            "Alpha Survey A",
            "Alter Survey A",
            StringComparison.Ordinal
        );
        await File.WriteAllTextAsync(fixture.JournalPath("F100"), replacement);
        await model.RefreshAsync();
        await model.PendingDetails;
        Assert.Equal("Alpha Survey", Assert.Single(model.Rows).SystemName);
        Assert.Null(model.SelectedRow);
        Assert.Empty(model.Bodies);
        Assert.False(model.HasSelection);
    }

    [Fact]
    public async Task ARepeatedScanKeepsTheOpenCollectionsDetailsSelected()
    {
        using var fixture = new Fixture();
        await using ExplorationHistoryViewModel model = fixture.Create();
        model.SetCommander("F100", "Survey commander", true, true);
        await model.PendingSynchronization;
        model.SelectedRow = Assert.Single(model.Rows);
        await model.PendingDetails;
        await File.AppendAllLinesAsync(
            fixture.JournalPath("F100"),
            [
                """{"timestamp":"2026-10-01T13:00:00Z","event":"Scan","SystemAddress":100,"BodyID":0,"BodyName":"Alpha Survey A","StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}""",
            ]
        );
        await model.RefreshAsync();
        await model.PendingDetails;
        Assert.Equal("Alpha Survey", model.SelectedRow?.SystemName);
        Assert.Equal("Alpha Survey A", Assert.Single(model.Bodies).Name);
        Assert.Contains("2026-10-01T13:00:00Z", model.RawJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASelectionOpenedWhileAQueryIsPendingRemainsSelected(bool withInitialSelection)
    {
        using var fixture = new Fixture();
        await File.AppendAllLinesAsync(
            fixture.JournalPath("F100"),
            [
                """{"timestamp":"2026-10-01T13:00:00Z","event":"Location","StarSystem":"Beta Survey","SystemAddress":200}""",
                """{"timestamp":"2026-10-01T13:01:00Z","event":"Scan","SystemAddress":200,"BodyID":0,"BodyName":"Beta Survey A","StarType":"NS","StellarMass":1.4}""",
            ]
        );
        int holdNext = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ExplorationHistoryViewModel model = fixture.Create(
            async (catalog, query, token) =>
            {
                ExplorationHistoryPage page = await catalog.QueryAsync(query, token);
                if (Interlocked.Exchange(ref holdNext, 0) == 1)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token);
                }
                return page;
            }
        );
        try
        {
            model.SetCommander("F100", "Survey commander", true, true);
            await model.PendingSynchronization;
            if (withInitialSelection)
            {
                model.SelectedRow = Assert.Single(model.Rows, row => row.SystemName == "Alpha Survey");
                await model.PendingDetails;
            }
            Interlocked.Exchange(ref holdNext, 1);
            model.SearchText = "Survey";
            Task pending = model.PendingQuery;
            await entered.Task;
            model.SelectedRow = Assert.Single(model.Rows, row => row.SystemName == "Beta Survey");
            await model.PendingDetails;
            release.TrySetResult();
            await pending;
            await model.PendingDetails;
            Assert.Equal("Beta Survey", model.SelectedRow?.SystemName);
            Assert.Equal("Beta Survey A", Assert.Single(model.Bodies).Name);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task LegacyCachedRowsRemainVisibleWhenSourceDiscoveryFailsDuringUpgrade()
    {
        using var fixture = new Fixture();
        await using (ExplorationHistoryViewModel initial = fixture.Create())
        {
            initial.SetCommander("F100", "Survey commander", true, true);
            await initial.PendingSynchronization;
        }
        string database = new ExplorationHistoryCatalog(fixture.Data, "F100", true).FilePath;
        using (
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()
            )
        )
        {
            await connection.OpenAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DROP TABLE collection_anchors; PRAGMA user_version=1;";
            await command.ExecuteNonQueryAsync();
        }
        string archive = Path.Combine(fixture.Root, "archive");
        Directory.CreateDirectory(archive);
        string incompatible = (await File.ReadAllTextAsync(fixture.JournalPath("F100"))).Replace(
            "Alpha Survey",
            "Other Survey",
            StringComparison.Ordinal
        );
        await File.WriteAllTextAsync(
            Path.Combine(archive, Path.GetFileName(fixture.JournalPath("F100"))),
            incompatible
        );
        await using var model = new ExplorationHistoryViewModel(
            fixture.Data,
            fixture.Profiles,
            () => [archive, fixture.Journals]
        );
        model.SetCommander("F100", "Survey commander", true, true);
        await model.PendingSynchronization;
        Assert.Contains("Conflicting copies", model.StatusMessage);
        model.SelectedRow = Assert.Single(model.Rows);
        Assert.Equal("Alpha Survey", model.SelectedRow.SystemName);
        await model.PendingDetails;
        Assert.Equal("Alpha Survey A", Assert.Single(model.Bodies).Name);
    }

    [Fact]
    public async Task AnOlderQueryFailureCannotReplaceTheSuccessfulNewerQueryStatus()
    {
        using var fixture = new Fixture();
        int holdNext = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ExplorationHistoryViewModel model = fixture.Create(
            async (catalog, query, token) =>
            {
                ExplorationHistoryPage page = await catalog.QueryAsync(query, token);
                if (Interlocked.Exchange(ref holdNext, 0) == 1)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token);
                    throw new IOException("The older query failed.");
                }
                return page;
            }
        );
        try
        {
            model.SetCommander("F100", "Survey commander", true, true);
            await model.PendingSynchronization;
            Interlocked.Exchange(ref holdNext, 1);
            model.SearchText = "Alpha";
            Task older = model.PendingQuery;
            await entered.Task;
            model.SearchText = "Survey";
            await model.PendingQuery;
            string currentStatus = model.StatusMessage;
            release.TrySetResult();
            await older;
            Assert.Equal("Alpha Survey", Assert.Single(model.Rows).SystemName);
            Assert.Equal(currentStatus, model.StatusMessage);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(bool withScan = true)
        {
            Directory.CreateDirectory(Data);
            Directory.CreateDirectory(Journals);
            File.WriteAllText(
                ProfilePath,
                """{"fid":"F100","commander":"History Commander","isOdyssey":true,"explRewards":777,"countJumps":42,"countScans":9,"futureField":"preserved"}"""
            );
            AddCommander("F100", "Alpha Survey", 100, withScan);
            Profiles = new CommanderProfileStore(Data);
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "SrvSurvey-history-view-" + Guid.NewGuid());
        public string Data => Path.Combine(Root, "data");
        public string Journals => Path.Combine(Root, "journals");
        public string ProfilePath => Path.Combine(Data, "F100-live.json");
        public CommanderProfileStore Profiles { get; }

        public string JournalPath(string id) => Path.Combine(Journals, $"Journal.2026-10-01T1200{id[1..]}.01.log");

        public ExplorationHistoryViewModel Create(
            Func<
                ExplorationHistoryCatalog,
                ExplorationHistoryQuery,
                CancellationToken,
                Task<ExplorationHistoryPage>
            >? queryReader = null
        ) => new(Data, Profiles, () => [Journals], queryReader);

        public void AddCommander(string id, string system, long address, bool withScan = true)
        {
            string[] lines =
            [
                """{"timestamp":"2026-10-01T12:00:00Z","event":"Fileheader","gameversion":"4.0.0","Odyssey":true}""",
                $$"""{"timestamp":"2026-10-01T12:00:01Z","event":"Commander","FID":"{{id}}","Name":"History Commander"}""",
                $$"""{"timestamp":"2026-10-01T12:01:00Z","event":"Location","StarSystem":"{{system}}","SystemAddress":{{address}},"StarPos":[10,20,30]}""",
                .. withScan
                    ? new[]
                    {
                        $$"""{"timestamp":"2026-10-01T12:02:00Z","event":"Scan","SystemAddress":{{address}},"BodyID":0,"BodyName":"{{system}} A","StarType":"NS","StellarMass":1.4,"WasDiscovered":true,"WasMapped":true}""",
                    }
                    : [],
            ];
            File.WriteAllLines(JournalPath(id), lines);
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
