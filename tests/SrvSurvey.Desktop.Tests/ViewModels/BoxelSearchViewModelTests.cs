using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;
using static SrvSurvey.Desktop.Tests.JournalEventEnvelopeTestParser;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class BoxelSearchViewModelTests : IDisposable
{
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

        viewModel.UpdateCurrentSystem(
            "Praea Euq IL-P c5-0",
            new GalacticCoordinate(1, 2, 3),
            100);
        Assert.Equal("id64 100", viewModel.CurrentSystemAddressText);
        var rows = viewModel.Systems;
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) =>
            notifications.Add(eventArgs.PropertyName);

        viewModel.UpdateCurrentSystem(
            "Praea Euq IL-P c5-0",
            new GalacticCoordinate(1, 2, 3),
            100);

        Assert.Same(rows, viewModel.Systems);
        Assert.Empty(notifications);

        viewModel.UpdateCurrentSystem(
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
        var viewModel = new BoxelSearchViewModel(
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
        var viewModel = new BoxelSearchViewModel(
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
        startRow.StartHereCommand.Execute(null);
        await WaitUntilAsync(async () =>
        {
            var loaded = await profileStore.LoadAsync("F123", true);
            return loaded.Data?.BoxelSearch.DeferredRanges.SingleOrDefault()
                ?.StartSystemNumber == 5;
        });
        await WaitUntilAsync(() => copied.Contains(
            "Praea Euq IL-P c5-5",
            StringComparer.Ordinal));

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
        reopened.ReopenCommand.Execute(null);
        await WaitUntilAsync(async () =>
        {
            var loaded = await profileStore.LoadAsync("F123", true);
            return loaded.Data?.BoxelSearch.DeferredRanges.SingleOrDefault()
                ?.Exceptions.Contains(2) == true;
        });

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
        known.CompleteCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.StatusMessage.Contains(
            "Marked",
            StringComparison.Ordinal));
        var completedSave = await profileStore.LoadAsync("F123", true);
        Assert.Contains(
            "Praea Euq IL-P c5-0",
            completedSave.Data!.BoxelSearch.CompletedSystems);

        var unknown = viewModel.Systems.Single(row => row.Name.EndsWith(
            "c5-1",
            StringComparison.Ordinal));
        Assert.True(unknown.DeferCommand.CanExecute(null));
        unknown.DeferCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.Systems.Single(row => row.Name.EndsWith(
            "c5-1",
            StringComparison.Ordinal)).IsDeferred);
        await WaitUntilAsync(() => viewModel.StatusMessage.Contains(
            "Deferred",
            StringComparison.Ordinal));
        var deferredSave = await profileStore.LoadAsync("F123", true);
        Assert.Contains(
            "Praea Euq IL-P c5-1",
            deferredSave.Data!.BoxelSearch.DeferredSystems);

        var deferred = viewModel.Systems.Single(row => row.Name.EndsWith(
            "c5-1",
            StringComparison.Ordinal));
        Assert.True(deferred.ReopenCommand.CanExecute(null));
        deferred.ReopenCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.Systems.Single(row => row.Name.EndsWith(
            "c5-1",
            StringComparison.Ordinal)).IsDeferred == false);
        await WaitUntilAsync(() => viewModel.StatusMessage.Contains(
            "Reopened",
            StringComparison.Ordinal));

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
        var viewModel = new BoxelSearchViewModel(
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
        var viewModel = new BoxelSearchViewModel(
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
        var viewModel = new BoxelSearchViewModel(
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
        viewModel.UpdateCurrentSystem(
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
        var viewModel = new BoxelSearchViewModel(
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
        viewModel.UpdateCurrentSystem(
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
        var viewModel = new BoxelSearchViewModel(
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

        viewModel.SetProfileError("Profile unavailable.");
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
        var viewModel = new BoxelSearchViewModel(
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

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private BoxelSearchViewModel CreateViewModel(
        CommanderProfileStore store,
        IBoxelSystemResolver resolver,
        ISystemNameSuggestionClient? suggestionClient = null,
        BoxelSurveyStatsCoordinator? surveyStats = null)
    {
        return new BoxelSearchViewModel(
            store,
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            resolver,
            systemNameSuggestionClient: suggestionClient,
            systemSuggestionDelay: TimeSpan.Zero,
            surveyStats: surveyStats);
    }

    private static BoxelSystemObservation Observation(string name, long address)
    {
        return new BoxelSystemObservation(
            BoxelAddress.Parse(name) with { SystemAddress = address },
            new GalacticCoordinate(address, 0, 0),
            null,
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            true);
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
        public Task<IReadOnlyList<BoxelSystemObservation>> SearchAsync(
            BoxelAddress boxel,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<BoxelSystemObservation>>(
                systems.Where(system => string.Equals(
                        system.Boxel.Prefix,
                        boxel.Prefix,
                        StringComparison.Ordinal))
                    .ToArray());
        }
    }
}
