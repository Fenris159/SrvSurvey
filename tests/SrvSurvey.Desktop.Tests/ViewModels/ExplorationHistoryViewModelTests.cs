using System.Text.Json;
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

        public ExplorationHistoryViewModel Create() => new(Data, Profiles, () => [Journals]);

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
