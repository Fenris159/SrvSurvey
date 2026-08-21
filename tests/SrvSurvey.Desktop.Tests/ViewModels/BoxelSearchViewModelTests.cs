using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;
using static SrvSurvey.Desktop.Tests.JournalEventEnvelopeTestParser;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class BoxelSearchViewModelTests : IAsyncLifetime
{
    private readonly List<BoxelSearchSession> sessions = [];
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-boxel-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task EmptyProfileLeavesAutoCopyUnselected()
    {
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver([]));

        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);

        Assert.False(viewModel.AutoCopy);
    }

    [Fact]
    public async Task EmptyProfileDefaultsSearchStartToCurrentLocalDate()
    {
        var before = new DateTimeOffset(DateTime.Today);
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver([]));

        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);

        var after = new DateTimeOffset(DateTime.Today);
        Assert.True(viewModel.StartedOn == before || viewModel.StartedOn == after);
    }

    [Fact]
    public async Task ResumingSavedProgressRestoresItsOriginalSearchStartDate()
    {
        var originalStart = DateTimeOffset.Parse("2026-05-04T00:00:00-05:00");
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var savedStore = new SavedBoxelSearchStore(temporaryDirectory);
        var first = CreateTrackedViewModel(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new StubResolver([]),
            savedSearchStore: savedStore);
        await first.LoadProfileAsync("F123", "Drew", true, BoxelSearchSnapshot.Empty);
        first.TopBoxelText = "Praea Euq IL-P c5-0";
        first.LowMassCode = "c";
        first.StartedOn = originalStart;
        await first.ActivateAsync();
        Assert.Equal(
            SaveBoxelProgressResult.Saved,
            await first.SaveProgressAsync("Original configuration", null));
        var saved = Assert.Single(await savedStore.ListAsync("F123"));
        var resumed = CreateTrackedViewModel(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new StubResolver([]),
            savedSearchStore: savedStore);
        await resumed.LoadProfileAsync("F123", "Drew", true, BoxelSearchSnapshot.Empty);

        await resumed.ResumeSavedSearchAsync(saved.FileName);

        Assert.Equal(originalStart, resumed.StartedOn);
    }

    [Fact]
    public async Task AutoCopyNotifiesManualCopyGuidance()
    {
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver([]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();
        Assert.True(viewModel.CanSaveProgress);
        Assert.Equal("Save to Library", viewModel.LibrarySaveButtonText);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        viewModel.AutoCopy = true;

        Assert.Contains(
            nameof(BoxelSearchViewModel.NextSystemClipboardStatus),
            changedProperties);
        Assert.Contains(
            nameof(BoxelSearchViewModel.RequiresManualCopy),
            changedProperties);
        Assert.Equal("AUTO-COPY READY", viewModel.NextSystemClipboardStatus);
        Assert.False(viewModel.RequiresManualCopy);
        await viewModel.DisableAsync();
    }

    [AvaloniaFact]
    public async Task CancellingAuditKeepsSurveyStatisticsUpdatesSubscribed()
    {
        using var coordinator = new BoxelSurveyStatsCoordinator(
            new BoxelSurveyStatsStore(temporaryDirectory),
            TimeSpan.FromHours(1));
        await coordinator.SwitchCommanderAsync("F123");
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver([]),
            surveyStats: coordinator);

        await viewModel.CancelAuditAsync();
        await coordinator.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-0","SystemAddress":2001}"""),
        ]);

        Assert.Contains("Praea Euq IL-P c5-", viewModel.StatsGlanceText, StringComparison.Ordinal);
        Assert.Contains("1 recorded", viewModel.StatsGlanceText, StringComparison.Ordinal);
        Assert.Contains("highest suffix 0", viewModel.StatsGlanceText, StringComparison.Ordinal);
        Assert.DoesNotContain("1 / 1", viewModel.StatsGlanceText, StringComparison.Ordinal);
        viewModel.CancelPendingOperations();
    }

    [Fact]
    public void StatisticsStartupFailureAppearsInTheBoxelStatus()
    {
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver([]));

        viewModel.ReportStatisticsFailure("Access denied.");

        Assert.Equal(
            "Could not open boxel statistics: Access denied.",
            viewModel.StatusMessage);
    }

    [Fact]
    public async Task ActivateMergesSourcesAndPersistsLegacySearchState()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var systemDirectory = Path.Combine(
            temporaryDirectory,
            "systems",
            "F123");
        Directory.CreateDirectory(systemDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(systemDirectory, "Praea Euq IL-P c5-0_100.json"),
            """
            {
              "name": "Praea Euq IL-P c5-0",
              "address": 100,
              "starPos": [1, 2, 3],
              "lastVisited": "2026-06-01T00:00:00Z"
            }
            """);
        var resolver = new StubResolver(
        [
            Observation("Praea Euq IL-P c5-0", 100),
            Observation("Praea Euq IL-P c5-1", 101),
            Observation("Praea Euq IL-P c5-2", 102),
        ]);
        var viewModel = CreateViewModel(profileStore, resolver);
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        viewModel.StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        viewModel.SkipAlreadyVisited = true;

        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsActive);
        Assert.Equal("Praea Euq IL-P c5-1", viewModel.NextSystem);
        Assert.Equal(3, viewModel.Systems.Count);
        Assert.True(viewModel.Systems[0].IsComplete);
        Assert.Equal("2", viewModel.LastSystemAvailable);
        Assert.True(viewModel.Systems[1].ShowNextIncompleteHighlight);
        Assert.Equal("NEXT INCOMPLETE SYSTEM", viewModel.Systems[1].RowIndicator);
        var saved = await profileStore.LoadAsync("F123", true);
        Assert.True(saved.Data?.BoxelSearch.Active);
        Assert.Equal('c', saved.Data?.BoxelSearch.LowMassCode);
        Assert.True(saved.Data?.BoxelSearch.SkipAlreadyVisited);

        await viewModel.UpdateCurrentSystemAsync(
            "Praea Euq IL-P c5-0",
            new GalacticCoordinate(1, 2, 3),
            100);
        Assert.Equal("id64 100", viewModel.CurrentSystemAddressText);
        var rows = viewModel.Systems;
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) =>
            notifications.Add(eventArgs.PropertyName);

        await viewModel.UpdateCurrentSystemAsync(
            "Praea Euq IL-P c5-0",
            new GalacticCoordinate(1, 2, 3),
            100);

        Assert.Same(rows, viewModel.Systems);
        Assert.Empty(notifications);

        await viewModel.UpdateCurrentSystemAsync(
            "Praea Euq IL-P c5-1",
            new GalacticCoordinate(1, 2, 3),
            101);
        var currentNext = Assert.Single(viewModel.Systems, row => row.IsCurrent);
        Assert.True(currentNext.ShowCurrentNextHighlight);
        Assert.False(currentNext.ShowNextIncompleteHighlight);
        Assert.Equal(
            "CURRENT SYSTEM · NEXT INCOMPLETE SYSTEM",
            currentNext.RowIndicator);
    }

    [Fact]
    public async Task LastSystemAvailableIncludesSystemZeroInTheTotalCount()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var viewModel = CreateViewModel(
            profileStore,
            new StubResolver(
            [
                Observation("Praea Euq IL-P c5-0", 100),
                Observation("Praea Euq IL-P c5-1", 101),
                Observation("Praea Euq IL-P c5-2", 102),
            ]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();

        viewModel.LastSystemAvailable = "348";
        await viewModel.ApplyLastSystemAvailableAsync();

        Assert.Equal("348", viewModel.LastSystemAvailable);
        Assert.Equal(10, viewModel.Systems.Count);
        Assert.EndsWith("c5-0", viewModel.Systems[0].Name, StringComparison.Ordinal);
        Assert.EndsWith("c5-9", viewModel.Systems[^1].Name, StringComparison.Ordinal);
        Assert.Equal("Showing systems 0–9 of 349.", viewModel.SystemListNote);
        Assert.Equal("Page 1 of 35", viewModel.SystemPageText);
        Assert.Equal(1, viewModel.SystemPageNumber);
        Assert.Equal(35, viewModel.SystemPageCount);
        Assert.Equal(Enumerable.Range(1, 35), viewModel.SystemPageNumbers);
        Assert.Equal(120, viewModel.SystemPagePickerWidth);
        Assert.False(viewModel.PreviousSystemPageCommand.CanExecute(null));

        viewModel.SelectedSystemPageIndex = 17;

        Assert.Equal("Page 18 of 35", viewModel.SystemPageText);
        Assert.EndsWith("c5-170", viewModel.Systems[0].Name, StringComparison.Ordinal);
        Assert.EndsWith("c5-179", viewModel.Systems[^1].Name, StringComparison.Ordinal);
        viewModel.SelectedSystemPageIndex = 0;

        for (var page = 1; page < viewModel.SystemPageCount; page++)
        {
            Assert.True(viewModel.NextSystemPageCommand.CanExecute(null));
            viewModel.NextSystemPageCommand.Execute(null);
        }

        Assert.Equal(9, viewModel.Systems.Count);
        Assert.EndsWith("c5-340", viewModel.Systems[0].Name, StringComparison.Ordinal);
        Assert.EndsWith("c5-348", viewModel.Systems[^1].Name, StringComparison.Ordinal);
        Assert.Equal("Showing systems 340–348 of 349.", viewModel.SystemListNote);
        Assert.Equal("Page 35 of 35", viewModel.SystemPageText);
        Assert.False(viewModel.NextSystemPageCommand.CanExecute(null));
        Assert.True(viewModel.PreviousSystemPageCommand.CanExecute(null));
        Assert.EndsWith(
            "of 349 systems complete",
            viewModel.SystemProgress,
            StringComparison.Ordinal);
        Assert.Equal("Last system available updated to 348.", viewModel.StatusMessage);
        var saved = await profileStore.LoadAsync("F123", true);
        Assert.Equal(349, saved.Data?.BoxelSearch.CurrentCount);
    }

    [Fact]
    public async Task RefreshMovesToThePageContainingANewNextTarget()
    {
        var resolver = new StubResolver([]);
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            resolver);
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        viewModel.StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        viewModel.SkipKnownToSpansh = true;
        viewModel.CompleteOnFssAllBodies = true;
        await viewModel.ActivateAsync();
        viewModel.LastSystemAvailable = "40";
        await viewModel.ApplyLastSystemAvailableAsync();
        Assert.Equal(1, viewModel.SystemPageNumber);

        resolver.Systems = Enumerable.Range(0, 36)
            .Select(suffix => Observation(
                $"Praea Euq IL-P c5-{suffix}",
                100 + suffix))
            .ToArray();

        await viewModel.RefreshCurrentAsync();

        Assert.Equal("Praea Euq IL-P c5-36", viewModel.NextSystem);
        Assert.Equal(4, viewModel.SystemPageNumber);
        var highlighted = Assert.Single(viewModel.Systems, row => row.IsNextIncomplete);
        Assert.EndsWith("c5-36", highlighted.Name, StringComparison.Ordinal);

        viewModel.SelectedSystemPageIndex = 0;
        await viewModel.RefreshCurrentAsync();

        Assert.Equal(1, viewModel.SystemPageNumber);
    }

    [Fact]
    public async Task StopResetsManualPagingBeforeRestartFollowsTheNextTarget()
    {
        var observations = Enumerable.Range(0, 36)
            .Select(suffix => Observation(
                $"Praea Euq IL-P c5-{suffix}",
                100 + suffix,
                hasKnownBodies: suffix < 35))
            .ToArray();
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver(observations));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        viewModel.StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        viewModel.SkipKnownToSpansh = true;
        viewModel.CompleteOnFssAllBodies = true;

        await viewModel.ActivateAsync();

        Assert.Equal("Praea Euq IL-P c5-35", viewModel.NextSystem);
        Assert.Equal(4, viewModel.SystemPageNumber);
        viewModel.SelectedSystemPageIndex = 1;
        Assert.Equal(2, viewModel.SystemPageNumber);

        await viewModel.DisableAsync();

        Assert.Equal(1, viewModel.SystemPageNumber);
        Assert.Empty(viewModel.Systems);

        await viewModel.ActivateAsync();

        Assert.Equal("Praea Euq IL-P c5-35", viewModel.NextSystem);
        Assert.Equal(4, viewModel.SystemPageNumber);
        var highlighted = Assert.Single(viewModel.Systems, row => row.IsNextIncomplete);
        Assert.EndsWith("c5-35", highlighted.Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LastSystemAvailableEditRequiresNumbersAndRestoresUntilApplied()
    {
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver(
            [
                Observation("Praea Euq IL-P c5-0", 100),
                Observation("Praea Euq IL-P c5-8", 108),
            ]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();

        Assert.Equal("8", viewModel.LastSystemAvailable);
        Assert.False(viewModel.ApplyLastSystemAvailableCommand.CanExecute(null));

        viewModel.LastSystemAvailable = "abc";

        Assert.True(viewModel.HasLastSystemAvailableError);
        Assert.Equal(
            "Enter numbers only, from 0 to 99,999.",
            viewModel.LastSystemAvailableValidationMessage);
        Assert.False(viewModel.ApplyLastSystemAvailableCommand.CanExecute(null));

        viewModel.RestoreLastSystemAvailable();

        Assert.Equal("8", viewModel.LastSystemAvailable);
        Assert.False(viewModel.HasLastSystemAvailableError);
        Assert.False(viewModel.ApplyLastSystemAvailableCommand.CanExecute(null));

        viewModel.LastSystemAvailable = "7";

        Assert.True(viewModel.HasLastSystemAvailableError);
        Assert.Equal(
            "Enter 8 or higher; that suffix is already recorded.",
            viewModel.LastSystemAvailableValidationMessage);
        Assert.False(viewModel.ApplyLastSystemAvailableCommand.CanExecute(null));

        viewModel.LastSystemAvailable = "12";

        Assert.False(viewModel.HasLastSystemAvailableError);
        Assert.True(viewModel.ApplyLastSystemAvailableCommand.CanExecute(null));

        viewModel.RestoreLastSystemAvailable();

        Assert.Equal("8", viewModel.LastSystemAvailable);
        Assert.False(viewModel.ApplyLastSystemAvailableCommand.CanExecute(null));
    }

    [Fact]
    public async Task PagePickerWidthAccommodatesTheLastPageNumber()
    {
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver([]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();
        viewModel.LastSystemAvailable = "99999";

        await viewModel.ApplyLastSystemAvailableAsync();

        Assert.Equal(10_000, viewModel.SystemPageCount);
        Assert.Equal(10_000, viewModel.SystemPageNumbers[^1]);
        Assert.Equal(124, viewModel.SystemPagePickerWidth);
    }

    [Fact]
    public async Task DescendingSearchPagesAndCopiesFromHighestIncompleteSuffix()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var copied = new List<string>();
        var viewModel = CreateTrackedViewModel(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new StubResolver([]),
            text =>
            {
                copied.Add(text);
                return Task.CompletedTask;
            });
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        viewModel.AutoCopy = true;
        await viewModel.ActivateAsync();
        viewModel.LastSystemAvailable = "12";

        await viewModel.ApplyLastSystemAvailableAsync();
        Assert.Equal("Praea Euq IL-P c5-0", viewModel.NextSystem);

        viewModel.SortDescending = true;

        Assert.Equal("Praea Euq IL-P c5-12", viewModel.NextSystem);
        Assert.EndsWith("c5-12", viewModel.Systems[0].Name, StringComparison.Ordinal);
        Assert.EndsWith("c5-3", viewModel.Systems[^1].Name, StringComparison.Ordinal);
        Assert.Equal("Showing systems 12–3 of 13 (descending).", viewModel.SystemListNote);
        Assert.True(viewModel.Systems[0].IsNextIncomplete);

        viewModel.SelectedSystemPageIndex = 1;

        Assert.Equal("Page 2 of 2", viewModel.SystemPageText);
        Assert.EndsWith("c5-2", viewModel.Systems[0].Name, StringComparison.Ordinal);
        Assert.True(viewModel.NextJumpPageCommand.CanExecute(null));
        viewModel.NextJumpPageCommand.Execute(null);
        Assert.Equal("Page 1 of 2", viewModel.SystemPageText);
        Assert.EndsWith("c5-12", viewModel.Systems[0].Name, StringComparison.Ordinal);
        Assert.False(viewModel.NextJumpPageCommand.CanExecute(null));

        await viewModel.MarkNextEmptyAsync();

        Assert.Equal("Praea Euq IL-P c5-11", viewModel.NextSystem);
        Assert.Equal(["Praea Euq IL-P c5-11"], copied);
        var saved = await profileStore.LoadAsync("F123", true);
        Assert.True(saved.Data?.BoxelSearch.SortDescending);
    }

    [Fact]
    public async Task StartHereGroupsPersistsAndFiltersDeferredSystems()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var copied = new List<string>();
        var viewModel = CreateTrackedViewModel(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new StubResolver([]),
            text =>
            {
                copied.Add(text);
                return Task.CompletedTask;
            });
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        viewModel.AutoCopy = true;
        await viewModel.ActivateAsync();
        viewModel.LastSystemAvailable = "12";
        await viewModel.ApplyLastSystemAvailableAsync();
        copied.Clear();

        var startRow = viewModel.Systems.Single(row => row.Name.EndsWith(
            "c5-5",
            StringComparison.Ordinal));
        await startRow.StartHereAsync();

        var loaded = await profileStore.LoadAsync("F123", true);
        Assert.Equal(
            5,
            loaded.Data?.BoxelSearch.DeferredRanges.SingleOrDefault()
                ?.StartSystemNumber);

        Assert.Equal("Praea Euq IL-P c5-5", viewModel.NextSystem);
        Assert.Equal(["Praea Euq IL-P c5-5"], copied);
        Assert.Equal(
            [5, 6, 7, 8, 9, 10, 11, 12, 0, 1],
            viewModel.Systems.Select(row => BoxelAddress.Parse(row.Name).N2));
        Assert.Equal("DEFERRED", viewModel.Systems[^1].Status);
        Assert.Contains("Deferred systems are grouped last", viewModel.SystemListNote);

        viewModel.ShowOnlyDeferred = true;

        Assert.Equal(1, viewModel.SystemPageCount);
        Assert.Equal([0, 1, 2, 3, 4], viewModel.Systems.Select(row =>
            BoxelAddress.Parse(row.Name).N2));
        Assert.All(viewModel.Systems, row => Assert.Equal("DEFERRED", row.Status));
        Assert.False(viewModel.NextJumpPageCommand.CanExecute(null));
        Assert.Contains("Showing deferred systems 1\u20135 of 5", viewModel.SystemListNote);

        var reopened = viewModel.Systems.Single(row => row.Name.EndsWith(
            "c5-2",
            StringComparison.Ordinal));
        Assert.True(reopened.ReopenCommand.CanExecute(null));
        await reopened.ReopenAsync();

        loaded = await profileStore.LoadAsync("F123", true);
        Assert.Contains(
            2,
            loaded.Data?.BoxelSearch.DeferredRanges.SingleOrDefault()
                ?.Exceptions ?? []);

        Assert.Equal("Praea Euq IL-P c5-2", viewModel.NextSystem);
        Assert.DoesNotContain(viewModel.Systems, row => row.Name.EndsWith(
            "c5-2",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task SystemActionsCompleteDeferAndReopenPersistInOrder()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var viewModel = CreateViewModel(
            profileStore,
            new StubResolver([
                Observation("Praea Euq IL-P c5-0", 100)
            ]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();
        viewModel.LastSystemAvailable = "2";
        await viewModel.ApplyLastSystemAvailableAsync();

        var known = viewModel.Systems.Single(row => row.Name.EndsWith(
            "c5-0",
            StringComparison.Ordinal));
        Assert.True(known.CompleteCommand.CanExecute(null));
        await known.CompleteAsync();
        var completedSave = await profileStore.LoadAsync("F123", true);
        Assert.Contains(
            "Praea Euq IL-P c5-0",
            completedSave.Data!.BoxelSearch.CompletedSystems);

        var unknown = viewModel.Systems.Single(row => row.Name.EndsWith(
            "c5-1",
            StringComparison.Ordinal));
        Assert.True(unknown.DeferCommand.CanExecute(null));
        await unknown.DeferAsync();
        var deferredSave = await profileStore.LoadAsync("F123", true);
        Assert.Contains(
            "Praea Euq IL-P c5-1",
            deferredSave.Data!.BoxelSearch.DeferredSystems);

        var deferred = viewModel.Systems.Single(row => row.Name.EndsWith(
            "c5-1",
            StringComparison.Ordinal));
        Assert.True(deferred.ReopenCommand.CanExecute(null));
        await deferred.ReopenAsync();

        Assert.Equal("Praea Euq IL-P c5-1", viewModel.NextSystem);
        Assert.Contains("Reopened", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AbandonedLastSystemEditRestoresAndNewSearchUsesResolvedEstimate()
    {
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver(
            [
                Observation("Praea Euq RS-U d2-0", 200),
                Observation("Praea Euq RS-U d2-17", 217),
            ]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";

        Assert.True(viewModel.ActivateCommand.CanExecute(null));
        await viewModel.ActivateAsync();
        viewModel.LastSystemAvailable = "8";
        await viewModel.ApplyLastSystemAvailableAsync();
        Assert.False(viewModel.ActivateCommand.CanExecute(null));

        viewModel.LastSystemAvailable = string.Empty;
        viewModel.RestoreLastSystemAvailable();

        Assert.Equal("8", viewModel.LastSystemAvailable);

        viewModel.LastSystemAvailable = string.Empty;
        await viewModel.DisableAsync();

        Assert.Equal("8", viewModel.LastSystemAvailable);
        Assert.True(viewModel.ActivateCommand.CanExecute(null));

        viewModel.TopBoxelText = "Praea Euq RS-U d2-0";
        var displayedLastSystems = new List<string>();
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(viewModel.LastSystemAvailable))
            {
                displayedLastSystems.Add(viewModel.LastSystemAvailable);
            }
        };
        await viewModel.ActivateAsync();

        Assert.Equal("17", viewModel.LastSystemAvailable);
        Assert.DoesNotContain("0", displayedLastSystems);
        Assert.False(viewModel.ActivateCommand.CanExecute(null));
    }

    [Fact]
    public async Task ActivateResolvesImportedHandAuthoredSystemName()
    {
        var published = Path.Combine(temporaryDirectory, "pub");
        Directory.CreateDirectory(published);
        await File.WriteAllTextAsync(
            Path.Combine(
                published,
                KnownSystemAddressCatalog.LegacyFileName),
            "known_systems = {\n  \"sol\": 10477373803,\n}\n"
                + "known_missing = [\n]\n");
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var viewModel = CreateTrackedViewModel(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new StubResolver([]),
            knownSystems: KnownSystemAddressCatalog.Load(temporaryDirectory));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Sol";
        viewModel.LowMassCode = "c";

        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsActive);
        Assert.DoesNotContain("valid generated", viewModel.StatusMessage);
        var saved = await profileStore.LoadAsync("F123", true);
        Assert.Equal("Sol", saved.Data?.BoxelSearch.TopBoxel?.Name);
        Assert.Equal(
            10477373803,
            saved.Data?.BoxelSearch.TopBoxel?.SystemAddress);
    }

    [Fact]
    public async Task SuggestedSystemSelectionUsesProviderId64ForActivation()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var suggestionClient = new StubSuggestionClient(
        [
            new SystemNameSuggestion("Sol", 10477373803, "EDSM"),
            new SystemNameSuggestion("Solati", 1458376315610, "EDSM"),
        ]);
        var viewModel = CreateTrackedViewModel(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new StubResolver([]),
            systemNameSuggestionClient: suggestionClient,
            systemSuggestionDelay: TimeSpan.Zero);
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);

        viewModel.TopBoxelText = "Sol";
        await WaitUntilAsync(() => !viewModel.IsSearchingSystemSuggestions);

        Assert.Equal(2, viewModel.SystemNameSuggestions.Count);
        Assert.Equal(0, viewModel.SelectedSystemSuggestionIndex);
        Assert.Equal("2 system suggestions from EDSM.", viewModel.SystemSuggestionStatus);
        viewModel.MoveSystemSuggestionSelection(1);
        Assert.Equal(1, viewModel.SelectedSystemSuggestionIndex);
        viewModel.MoveSystemSuggestionSelection(-1);
        Assert.True(viewModel.SelectCurrentSystemSuggestion());
        Assert.Equal("Sol", viewModel.TopBoxelText);
        Assert.False(viewModel.HasSystemNameSuggestions);

        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsActive);
        var saved = await profileStore.LoadAsync("F123", true);
        Assert.Equal("Sol", saved.Data?.BoxelSearch.TopBoxel?.Name);
        Assert.Equal(
            10477373803,
            saved.Data?.BoxelSearch.TopBoxel?.SystemAddress);
    }

    [Fact]
    public async Task JournalCompletionAndGalaxyMapAutoCopyUseTheNextSystem()
    {
        var copied = new List<string>();
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var viewModel = CreateTrackedViewModel(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new StubResolver(
            [
                Observation("Praea Euq IL-P c5-0", 100),
                Observation("Praea Euq IL-P c5-1", 101),
            ]),
            text =>
            {
                copied.Add(text);
                return Task.CompletedTask;
            });
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        viewModel.AutoCopy = true;
        await viewModel.ActivateAsync();
        await viewModel.UpdateCurrentSystemAsync(
            "Praea Euq IL-P c5-1",
            new GalacticCoordinate(1, 2, 3));
        await viewModel.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-24T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-1","SystemAddress":101,"StarPos":[1,2,3]}"""),
        ]);

        await viewModel.UpdateStatusAsync(new EliteStatus
        {
            Flags = StatusFlags.InMainShip,
            GuiFocus = GuiFocus.NoFocus,
        }, nextMusicTrack: "GalaxyMap");

        Assert.Equal("Praea Euq IL-P c5-0", viewModel.NextSystem);
        Assert.Equal(["Praea Euq IL-P c5-0"], copied);
        Assert.True(viewModel.Systems[1].IsComplete);
        Assert.True(viewModel.ShouldShowGalaxyMapOverlay);
    }

    [Fact]
    public async Task RoutePrioritySuppressesBoxelCopyForTheSameMapEntry()
    {
        var copied = new List<string>();
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var viewModel = CreateTrackedViewModel(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new StubResolver(
            [
                Observation("Praea Euq IL-P c5-0", 100),
                Observation("Praea Euq IL-P c5-1", 101),
            ]),
            text =>
            {
                copied.Add(text);
                return Task.CompletedTask;
            });
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        viewModel.AutoCopy = true;
        await viewModel.ActivateAsync();
        await viewModel.UpdateCurrentSystemAsync(
            "Praea Euq IL-P c5-0",
            new GalacticCoordinate(1, 2, 3));

        await viewModel.UpdateStatusAsync(
            new EliteStatus { GuiFocus = GuiFocus.GalaxyMap },
            allowAutoCopy: false);
        await viewModel.UpdateStatusAsync(
            new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });

        Assert.Empty(copied);

        await viewModel.UpdateStatusAsync(new EliteStatus());
        await viewModel.UpdateStatusAsync(
            new EliteStatus { GuiFocus = GuiFocus.GalaxyMap });

        Assert.Single(copied);
    }

    [Fact]
    public async Task GalaxyMapOverlayValidatesFinalRouteDestination()
    {
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver(
            [
                Observation("Praea Euq IL-P c5-0", 100),
                Observation("Praea Euq IL-P c5-1", 101),
            ]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();
        await viewModel.UpdateRouteAsync(new NavRouteSnapshot(
            DateTimeOffset.Parse("2026-07-25T01:00:00Z"),
            "NavRoute",
        [
            new NavRouteEntry("Praea Euq IL-P c5-0", 0, null, "K"),
            new NavRouteEntry("Praea Euq IL-P c5-1", 0, null, "K"),
        ]));

        await viewModel.UpdateStatusAsync(
            new EliteStatus { GuiFocus = GuiFocus.GalaxyMap },
            allowAutoCopy: false);

        Assert.True(viewModel.ShouldShowGalaxyMapOverlay);
        Assert.True(viewModel.IsDestinationValid);
        Assert.Contains("destination is valid", viewModel.DestinationStatus);

        await viewModel.UpdateRouteAsync(new NavRouteSnapshot(
            DateTimeOffset.Parse("2026-07-25T01:01:00Z"),
            "NavRoute",
        [
            new NavRouteEntry("Praea Euq IL-P c5-0", 0, null, "K"),
            new NavRouteEntry("Synuefe XE-Y c17-0", 0, null, "K"),
        ]));

        Assert.False(viewModel.IsDestinationValid);
        Assert.Contains("outside", viewModel.DestinationStatus);

        await viewModel.UpdateStatusAsync(new EliteStatus());
        Assert.False(viewModel.ShouldShowGalaxyMapOverlay);
    }

    [Fact]
    public async Task RefreshDoesNotOverwriteUnappliedLastSystemAvailableEdit()
    {
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver([]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();

        viewModel.LastSystemAvailable = "348";
        await viewModel.RefreshCurrentAsync();

        Assert.Equal("348", viewModel.LastSystemAvailable);

        await viewModel.ApplyLastSystemAvailableAsync();

        Assert.EndsWith(
            "of 349 systems complete",
            viewModel.SystemProgress,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkNextEmptySkipsAndPersistsOnlyTheNextIncompleteSystem()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var copied = new List<string>();
        var viewModel = CreateTrackedViewModel(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new StubResolver([]),
            text =>
            {
                copied.Add(text);
                return Task.CompletedTask;
            });
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq RS-U d2-0";
        viewModel.LowMassCode = "c";
        viewModel.AutoCopy = true;
        await viewModel.ActivateAsync();
        viewModel.LastSystemAvailable = "2";
        await viewModel.ApplyLastSystemAvailableAsync();

        await viewModel.MarkNextEmptyAsync();

        var top = BoxelAddress.Parse("Praea Euq RS-U d2-0");
        Assert.False(await new EmptyBoxelStore(temporaryDirectory).IsEmptyAsync(top));
        Assert.Equal(top.Prefix, viewModel.CurrentBoxelName);
        Assert.Equal("Praea Euq RS-U d2-1", viewModel.NextSystem);
        Assert.Equal("EMPTY", viewModel.Systems[0].Status);
        Assert.True(viewModel.Systems[0].ReopenCommand.CanExecute(null));
        Assert.Single(copied);
        Assert.Equal("Praea Euq RS-U d2-1", copied[0]);
        var saved = await profileStore.LoadAsync("F123", true);
        Assert.Equal(top.Prefix, saved.Data?.BoxelSearch.Current?.Prefix);
        Assert.Equal(
            ["Praea Euq RS-U d2-0"],
            saved.Data?.BoxelSearch.EmptySystems);
    }

    [Fact]
    public async Task MarkingNextEmptyAdvancesToThePageContainingTheNextTarget()
    {
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver([]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq RS-U d2-0";
        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();
        viewModel.LastSystemAvailable = "10";
        await viewModel.ApplyLastSystemAvailableAsync();

        for (var suffix = 0; suffix < 10; suffix++)
        {
            Assert.EndsWith(
                $"d2-{suffix}",
                viewModel.NextSystem,
                StringComparison.Ordinal);
            await viewModel.MarkNextEmptyAsync();
        }

        Assert.Equal(2, viewModel.SystemPageNumber);
        Assert.Equal("Page 2 of 2", viewModel.SystemPageText);
        Assert.Single(viewModel.Systems);
        Assert.EndsWith("d2-10", viewModel.Systems[0].Name, StringComparison.Ordinal);
        Assert.True(viewModel.Systems[0].IsNextIncomplete);
        Assert.True(viewModel.Systems[0].ShowNextIncompleteHighlight);
    }

    [Fact]
    public async Task HierarchyNavigationShowsBreadcrumbsNamedNeighborsAndChildren()
    {
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver([]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq RS-U d2-0";
        viewModel.LowMassCode = "b";
        await viewModel.ActivateAsync();

        var root = Assert.Single(viewModel.BreadcrumbBoxels);
        var rootChildren = viewModel.ChildBoxels.ToArray();
        Assert.Same(root, viewModel.CurrentHierarchyBoxel);
        Assert.Null(viewModel.ParentBoxel);
        Assert.Null(viewModel.PreviousSiblingBoxel);
        Assert.Null(viewModel.NextSiblingBoxel);
        Assert.Equal("Search root", viewModel.SiblingPosition);
        Assert.Equal(8, rootChildren.Length);
        Assert.All(rootChildren, child =>
        {
            Assert.Equal("Not searched", child.ProgressLabel);
            Assert.Equal("NOT STARTED", child.StatusLabel);
        });

        await rootChildren[2].NavigateAsync();

        Assert.Equal(2, viewModel.BreadcrumbBoxels.Count);
        Assert.Same(root, viewModel.BreadcrumbBoxels[0]);
        Assert.Same(rootChildren[2], viewModel.CurrentHierarchyBoxel);
        Assert.Same(root, viewModel.ParentBoxel);
        Assert.Equal("3 of 8 at this level", viewModel.SiblingPosition);
        Assert.Equal(rootChildren[1].Label, viewModel.PreviousSiblingBoxel?.Label);
        Assert.Equal(rootChildren[3].Label, viewModel.NextSiblingBoxel?.Label);
        Assert.Equal(8, viewModel.ChildBoxels.Count);

        await Assert.IsType<BoxelNavigationOptionViewModel>(
            viewModel.NextSiblingBoxel).NavigateAsync();

        Assert.Equal(rootChildren[3].Label, viewModel.CurrentHierarchyBoxel?.Label);
        Assert.Equal("4 of 8 at this level", viewModel.SiblingPosition);

        await viewModel.SetProfileErrorAsync("Profile unavailable.");
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq RS-U d2-0";
        viewModel.LowMassCode = "b";
        await viewModel.ActivateAsync();

        Assert.NotSame(root, viewModel.CurrentHierarchyBoxel);
    }

    [Fact]
    public async Task HierarchyRowsKeepTheirIdentityDuringProgressUpdates()
    {
        var viewModel = CreateViewModel(
            new CommanderProfileStore(temporaryDirectory),
            new StubResolver([]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq RS-U d2-0";
        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();
        var breadcrumb = viewModel.BreadcrumbBoxels;
        var children = viewModel.ChildBoxels;
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        await viewModel.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-24T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq RS-U d2-0","SystemAddress":100,"StarPos":[1,2,3]}"""),
        ]);

        Assert.Same(breadcrumb, viewModel.BreadcrumbBoxels);
        Assert.Same(children, viewModel.ChildBoxels);
        Assert.All(children, child => Assert.Contains(child, viewModel.ChildBoxels));
        Assert.DoesNotContain(nameof(viewModel.BreadcrumbBoxels), notifications);
        Assert.DoesNotContain(nameof(viewModel.ChildBoxels), notifications);
        Assert.Equal("1 of 1 systems complete", viewModel.CurrentHierarchyBoxel?.ProgressLabel);
        Assert.Equal("COMPLETE", viewModel.CurrentHierarchyBoxel?.StatusLabel);
    }

    [Fact]
    public async Task FullAreaAuditRefreshesEveryChildAndPersistsProgress()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var top = BoxelAddress.Parse("Praea Euq RS-U d2-0");
        var observations = new[] { top }
            .Concat(top.Children)
            .Select((boxel, index) => new BoxelSystemObservation(
                boxel.WithSystemNumber(0) with { SystemAddress = 100 + index },
                new GalacticCoordinate(index, 0, 0),
                null,
                DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                true))
            .ToArray();
        var viewModel = CreateViewModel(
            profileStore,
            new StubResolver(observations));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = top.Name;
        viewModel.LowMassCode = "c";
        viewModel.StartedOn = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        viewModel.SkipKnownToSpansh = true;
        await viewModel.ActivateAsync();

        await viewModel.AuditAllAsync();

        Assert.False(viewModel.IsAuditing);
        Assert.Equal(9, viewModel.AuditProcessed);
        Assert.Equal(9, viewModel.AuditTotal);
        Assert.Equal("9 of 9 boxels complete", viewModel.BoxelProgress);
        Assert.Contains("Audited all 9 boxels", viewModel.AuditProgress);
        var saved = await profileStore.LoadAsync("F123", true);
        Assert.Equal(9, saved.Data?.BoxelSearch.CompletedPrefixes.Count);
    }

    [Fact]
    public async Task LargeAuditRequiresExplicitConfirmation()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0")
            .Parent
            .Parent
            .Parent;
        var viewModel = CreateViewModel(profileStore, new StubResolver([]));
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = top.Name;
        viewModel.LowMassCode = "a";
        await viewModel.ActivateAsync();

        Assert.True(viewModel.ShowLargeAuditConfirmation);
        Assert.False(viewModel.AuditAllCommand.CanExecute(null));

        viewModel.ConfirmLargeAudit = true;

        Assert.True(viewModel.AuditAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task NamedProgressSaveLinksAutomaticUpdatesAndReopensDialogIfRemoved()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var savedStore = new SavedBoxelSearchStore(temporaryDirectory);
        var viewModel = CreateTrackedViewModel(
            profileStore,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new StubResolver(
            [
                Observation("Praea Euq IL-P c5-0", 100),
                Observation("Praea Euq IL-P c5-1", 101),
            ]),
            savedSearchStore: savedStore);
        await viewModel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        viewModel.TopBoxelText = "Praea Euq IL-P c5-0";
        viewModel.LowMassCode = "c";
        await viewModel.ActivateAsync();

        Assert.Equal(
            SaveBoxelProgressResult.RequiresDetails,
            await viewModel.SaveProgressAsync());
        Assert.Equal(
            SaveBoxelProgressResult.Saved,
            await viewModel.SaveProgressAsync("Return later", "Test notes"));
        Assert.False(viewModel.CanSaveProgress);
        Assert.True(viewModel.IsSavedToLibrary);
        Assert.Equal("Saved to Library", viewModel.LibrarySaveButtonText);
        var entry = Assert.Single(await savedStore.ListAsync("F123"));

        await viewModel.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-24T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-1","SystemAddress":101,"StarPos":[1,2,3]}"""),
        ]);

        entry = Assert.Single(await savedStore.ListAsync("F123"));
        Assert.Equal(1, entry.CompletedSystems);
        Assert.Equal("Test notes", entry.Notes);
        Assert.Equal(
            SaveBoxelProgressResult.Saved,
            await viewModel.SaveProgressAsync());
        Assert.Single(await savedStore.ListAsync("F123"));

        File.Delete(entry.FilePath);

        Assert.Equal(
            SaveBoxelProgressResult.RequiresDetails,
            await viewModel.SaveProgressAsync());
        Assert.True(viewModel.CanSaveProgress);
        Assert.False(viewModel.IsSavedToLibrary);
        Assert.Equal("Save to Library", viewModel.LibrarySaveButtonText);
        var active = await profileStore.LoadAsync("F123", true);
        Assert.Null(active.Data?.BoxelSearch.SavedSearchFileName);
    }

    [Fact]
    public async Task RestartRestoresIndividualCompletedSystems()
    {
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var resolver = new StubResolver(
        [
            Observation("Praea Euq IL-P c5-0", 100),
            Observation("Praea Euq IL-P c5-1", 101),
        ]);
        var first = CreateViewModel(profileStore, resolver);
        await first.LoadProfileAsync("F123", "Drew", true, BoxelSearchSnapshot.Empty);
        first.TopBoxelText = "Praea Euq IL-P c5-0";
        first.LowMassCode = "c";
        await first.ActivateAsync();
        await first.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-24T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-1","SystemAddress":101,"StarPos":[1,2,3]}"""),
        ]);
        var saved = await profileStore.LoadAsync("F123", true);
        var suggestions = new CountingSuggestionClient();
        var restarted = CreateViewModel(profileStore, resolver, suggestions);

        await restarted.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            Assert.IsType<BoxelSearchSnapshot>(saved.Data?.BoxelSearch));

        Assert.Equal("Praea Euq IL-P c5-0", restarted.TopBoxelText);
        Assert.False(restarted.HasSystemNameSuggestions);
        Assert.Equal(string.Empty, restarted.SystemSuggestionStatus);
        Assert.Equal(0, suggestions.CallCount);
        Assert.False(restarted.Systems[0].IsComplete);
        Assert.True(restarted.Systems[1].IsComplete);
    }

    [Fact]
    public async Task SessionOutcomesProduceSpecificStatusMessages()
    {
        var session = new ProgrammableSession();
        var viewModel = new BoxelSearchViewModel(session);
        var warning = new BoxelSearchWarning(
            BoxelSearchHealthSubsystem.Resolver,
            BoxelSearchMessageCode.RefreshFailed);
        var cases = new (BoxelSearchOutcome Outcome, string Expected)[]
        {
            (Outcome(BoxelSearchMessageCode.SearchNotConfigured),
                "No boxel search is configured for this commander."),
            (Outcome(BoxelSearchMessageCode.SearchLoadedInactive),
                "Loaded the saved boxel search; it is currently disabled."),
            (Outcome(BoxelSearchMessageCode.ProfileLoaded),
                "Loaded the active boxel search."),
            (Outcome(BoxelSearchMessageCode.ProfileUnavailable),
                "Waiting for a commander profile."),
            (Outcome(BoxelSearchMessageCode.SearchInvalid, primaryValue: "Invalid range."),
                "Invalid range."),
            (Outcome(BoxelSearchMessageCode.SearchInvalid),
                "The boxel search configuration is invalid."),
            (Outcome(BoxelSearchMessageCode.SearchStopped),
                "Boxel search disabled; its progress was retained."),
            (Outcome(BoxelSearchMessageCode.SearchSavedToLibrary, primaryValue: "Survey"),
                "Saved boxel search as Survey."),
            (Outcome(BoxelSearchMessageCode.SearchAlreadySavedToLibrary),
                "This boxel search is already saved to the library."),
            (Outcome(BoxelSearchMessageCode.LibraryUnavailable),
                "The saved boxel search library is temporarily unavailable."),
            (Outcome(BoxelSearchMessageCode.SavedSearchResumed, primaryValue: "Survey"),
                "Resumed saved boxel search Survey."),
            (Outcome(BoxelSearchMessageCode.RefreshCompleted, primaryValue: "boxel", count: 12),
                "Refreshed 12 known systems in boxel."),
            (Outcome(BoxelSearchMessageCode.RefreshCompleted, count: 12, warnings: [warning]),
                "Refreshed 12 known systems with warnings."),
            (Outcome(BoxelSearchMessageCode.RefreshFailed),
                "The boxel refresh could not be completed."),
            (Outcome(BoxelSearchMessageCode.AuditCompleted, total: 4),
                "Audited all 4 boxels and saved the refreshed progress."),
            (Outcome(BoxelSearchMessageCode.AuditCompleted, total: 4, warnings: [warning]),
                "Audited all 4 boxels with 1 warnings."),
            (Outcome(BoxelSearchMessageCode.AuditCancelled, count: 2, total: 4),
                "Audit cancelled after 2 of 4 boxels; partial progress was saved."),
            (Outcome(BoxelSearchMessageCode.AuditFailed),
                "The full-area audit could not be completed."),
            (Outcome(BoxelSearchMessageCode.ExpectedSystemCountChanged, count: 9),
                "Last system available updated to 9."),
            (Outcome(BoxelSearchMessageCode.ExpectedSystemCountChanged, count: 9,
                kind: BoxelSearchOutcomeKind.Rejected),
                "Last system available cannot be below recorded suffix 9."),
            (Outcome(BoxelSearchMessageCode.SystemCompleted, primaryValue: "c5-1"),
                "Marked c5-1 complete."),
            (Outcome(BoxelSearchMessageCode.SystemCompleted,
                kind: BoxelSearchOutcomeKind.Rejected),
                "The system was not marked complete."),
            (Outcome(BoxelSearchMessageCode.SystemReopened, primaryValue: "c5-1"),
                "Reopened c5-1."),
            (Outcome(BoxelSearchMessageCode.SystemReopened,
                kind: BoxelSearchOutcomeKind.Rejected),
                "The system was not reopened."),
            (Outcome(BoxelSearchMessageCode.SystemDeferred, primaryValue: "c5-1"),
                "Deferred c5-1."),
            (Outcome(BoxelSearchMessageCode.SystemDeferred,
                kind: BoxelSearchOutcomeKind.Rejected),
                "The system was not deferred."),
            (Outcome(BoxelSearchMessageCode.SurveyStartChanged, primaryValue: "c5-1"),
                "Survey will start at c5-1."),
            (Outcome(BoxelSearchMessageCode.SurveyStartChanged, primaryValue: "c5-1", count: 3),
                "Survey will start at c5-1; deferred 3 earlier systems."),
            (Outcome(BoxelSearchMessageCode.SurveyStartChanged,
                kind: BoxelSearchOutcomeKind.Rejected),
                "The survey start point was not changed."),
            (Outcome(BoxelSearchMessageCode.NextSystemMarkedEmpty, primaryValue: "c5-1"),
                "Marked c5-1 empty. No incomplete systems remain."),
            (Outcome(BoxelSearchMessageCode.NextSystemMarkedEmpty,
                primaryValue: "c5-1", secondaryValue: "c5-2"),
                "Marked c5-1 empty. Next incomplete system: c5-2."),
            (Outcome(BoxelSearchMessageCode.NextSystemMarkedEmpty,
                kind: BoxelSearchOutcomeKind.Rejected),
                "The next incomplete system was not marked empty."),
            (Outcome(BoxelSearchMessageCode.NextSystemCopied, primaryValue: "c5-1"),
                "Copied c5-1 to the clipboard."),
            (Outcome(BoxelSearchMessageCode.NextSystemCopied,
                kind: BoxelSearchOutcomeKind.Rejected),
                "No next boxel system is available to copy."),
            (Outcome(BoxelSearchMessageCode.ClipboardNotReady),
                "The desktop clipboard is not available."),
            (Outcome(BoxelSearchMessageCode.ClipboardFailed),
                "The next system could not be copied."),
            (Outcome(BoxelSearchMessageCode.SynchronizationDegraded),
                "The boxel search changed for this session but could not be saved."),
        };

        foreach (var testCase in cases)
        {
            session.NextOutcome = testCase.Outcome;

            await viewModel.CopyNextSystemAsync();

            Assert.Equal(testCase.Expected, viewModel.StatusMessage);
        }

        Assert.Equal(4, viewModel.AuditTotal);
        Assert.Equal(2, viewModel.AuditProcessed);
        viewModel.CancelPendingOperations();
    }

    [Fact]
    public async Task CompetingAutoCopyOutcomeExplainsWhyItWasDisabled()
    {
        var search = BoxelSearchSessionSearchSnapshot.Empty with
        {
            Persistence = BoxelSearchSnapshot.Empty with
            {
                TopBoxel = BoxelAddress.Parse("Praea Euq IL-P c5-0"),
                AutoCopy = true,
            },
        };
        var session = new ProgrammableSession
        {
            Current = BoxelSearchSessionSnapshot.Empty with { Search = search },
            NextOutcome = Outcome(BoxelSearchMessageCode.AutoCopyChanged),
        };
        var viewModel = new BoxelSearchViewModel(session);

        await viewModel.DisableAutoCopyForCompetingRouteAsync();

        Assert.Equal(
            "Boxel auto-copy was disabled because another Galaxy Map auto-copy setting was selected.",
            viewModel.StatusMessage);
        viewModel.CancelPendingOperations();
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var session in sessions.AsEnumerable().Reverse())
        {
            await session.DisposeAsync();
        }

        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private BoxelSearchViewModel CreateTrackedViewModel(
        CommanderProfileStore profileStore,
        LegacySystemDataReader localSystemReader,
        EmptyBoxelStore emptyBoxelStore,
        IBoxelSystemResolver systemResolver,
        Func<string, Task>? clipboardWriter = null,
        KnownSystemAddressCatalog? knownSystems = null,
        SavedBoxelSearchStore? savedSearchStore = null,
        ISystemNameSuggestionClient? systemNameSuggestionClient = null,
        TimeSpan? systemSuggestionDelay = null,
        BoxelSurveyStatsCoordinator? surveyStats = null)
    {
        var viewModel = BoxelSearchViewModelTestFactory.Create(
            profileStore,
            localSystemReader,
            emptyBoxelStore,
            systemResolver,
            out var session,
            clipboardWriter,
            knownSystems,
            savedSearchStore,
            systemNameSuggestionClient,
            systemSuggestionDelay,
            surveyStats);
        sessions.Add(session);
        return viewModel;
    }

    private BoxelSearchViewModel CreateViewModel(
        CommanderProfileStore store,
        IBoxelSystemResolver resolver,
        ISystemNameSuggestionClient? suggestionClient = null,
        BoxelSurveyStatsCoordinator? surveyStats = null)
    {
        return CreateTrackedViewModel(
            store,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            resolver,
            systemNameSuggestionClient: suggestionClient,
            systemSuggestionDelay: TimeSpan.Zero,
            surveyStats: surveyStats);
    }

    private static BoxelSystemObservation Observation(
        string name,
        long address,
        bool hasKnownBodies = true)
    {
        return new BoxelSystemObservation(
            BoxelAddress.Parse(name) with { SystemAddress = address },
            new GalacticCoordinate(address, 0, 0),
            null,
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            hasKnownBodies);
    }

    private static BoxelSearchOutcome Outcome(
        BoxelSearchMessageCode code,
        string? primaryValue = null,
        string? secondaryValue = null,
        int count = 0,
        int total = 0,
        IReadOnlyList<BoxelSearchWarning>? warnings = null,
        BoxelSearchOutcomeKind kind = BoxelSearchOutcomeKind.Success)
    {
        return new BoxelSearchOutcome(
            kind,
            code,
            0,
            0,
            0,
            0,
            0,
            0,
            primaryValue,
            secondaryValue,
            count,
            total,
            Warnings: warnings);
    }

    private sealed class ProgrammableSession : IBoxelSearchSession
    {
        public BoxelSearchSessionSnapshot Current { get; set; } =
            BoxelSearchSessionSnapshot.Empty;

        public BoxelSearchOutcome NextOutcome { get; set; } =
            Outcome(BoxelSearchMessageCode.None);

        public event EventHandler<BoxelSearchSessionChangedEventArgs>? Changed
        {
            add => _ = value;
            remove => _ = value;
        }

        public Task<BoxelSearchOutcome> SwitchProfileAsync(
            BoxelSearchProfile profile,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NextOutcome);
        }

        public Task<BoxelSearchOutcome> ClearProfileAsync(
            BoxelSearchMessageCode reason = BoxelSearchMessageCode.ProfileUnavailable,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NextOutcome);
        }

        public Task<BoxelSearchOutcome> ApplyAsync(
            BoxelSearchUpdate update,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NextOutcome);
        }

        public Task<BoxelSearchOutcome> ExecuteAsync(
            IBoxelSearchAction action,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NextOutcome);
        }

        public Task<BoxelSearchLibrarySnapshot> GetLibraryAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new BoxelSearchLibrarySnapshot(0, []));
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingSuggestionClient : ISystemNameSuggestionClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<SystemNameSuggestion>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<SystemNameSuggestion>>([]);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for the asynchronous suggestion request.");
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!await condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(
            await condition(),
            "Timed out waiting for asynchronous persistence.");
    }

    private sealed class StubSuggestionClient(
        IReadOnlyList<SystemNameSuggestion> suggestions)
        : ISystemNameSuggestionClient
    {
        public Task<IReadOnlyList<SystemNameSuggestion>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(suggestions);
        }
    }

    private sealed class StubResolver(
        IReadOnlyList<BoxelSystemObservation> systems) : IBoxelSystemResolver
    {
        public IReadOnlyList<BoxelSystemObservation> Systems { get; set; } = systems;

        public Task<IReadOnlyList<BoxelSystemObservation>> SearchAsync(
            BoxelAddress boxel,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<BoxelSystemObservation>>(
                Systems.Where(system => string.Equals(
                        system.Boxel.Prefix,
                        boxel.Prefix,
                        StringComparison.Ordinal))
                    .ToArray());
        }
    }
}
