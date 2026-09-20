using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;
using static SrvSurvey.Desktop.Tests.JournalEventEnvelopeTestParser;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class BoxelSurveyStatsViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-BoxelSurveyStatsViewModelTests-" + Guid.NewGuid().ToString("N")
    );

    [AvaloniaFact]
    public async Task MassCodeButtonsToggleMultipleFiltersForBrowserAndRecentRows()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        await coordinator.ApplyJournalEventsAsync([
            Parse(
                """{"timestamp":"2026-07-10T13:00:00Z","event":"FSDJump","StarSystem":"Wregoe BU-Y b2-0","SystemAddress":3001}"""
            ),
        ]);
        await coordinator.FlushAsync();
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);
        await viewModel.RefreshAsync();

        Assert.Equal(2, viewModel.RecentEntries.Count);
        Assert.All(viewModel.MassCodes, option => Assert.False(option.IsSelected));

        viewModel.SelectMassCodeCommand.Execute('c');

        BoxelSurveyBrowserRowViewModel row = Assert.Single(viewModel.BrowserRows);
        Assert.Equal("Praea Euq IL-P c5-", row.Prefix);
        Assert.Single(viewModel.RecentEntries);
        Assert.True(viewModel.MassCodes.Single(option => option.MassCode == 'c').IsSelected);

        viewModel.SelectMassCodeCommand.Execute('b');

        Assert.Equal(2, viewModel.BrowserRows.Count);
        Assert.Equal(2, viewModel.RecentEntries.Count);
        Assert.Contains("B, C", viewModel.BrowserTitle, StringComparison.Ordinal);

        viewModel.SelectMassCodeCommand.Execute('c');

        row = Assert.Single(viewModel.BrowserRows);
        Assert.Equal("Wregoe BU-Y b2-", row.Prefix);
        Assert.Single(viewModel.RecentEntries);
        Assert.False(viewModel.MassCodes.Single(option => option.MassCode == 'c').IsSelected);
        Assert.True(viewModel.MassCodes.Single(option => option.MassCode == 'b').IsSelected);
    }

    [AvaloniaFact]
    public async Task ListSectionCommandsKeepExactlyOneBoxelListExpanded()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);
        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsRecentSectionExpanded);
        Assert.False(viewModel.IsBrowserSectionExpanded);

        viewModel.ShowBrowserSectionCommand.Execute(null);

        Assert.False(viewModel.IsRecentSectionExpanded);
        Assert.True(viewModel.IsBrowserSectionExpanded);

        viewModel.ShowRecentSectionCommand.Execute(null);

        Assert.True(viewModel.IsRecentSectionExpanded);
        Assert.False(viewModel.IsBrowserSectionExpanded);
    }

    [AvaloniaFact]
    public async Task RecentEntriesAreLimitedToEight()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        JournalEventEnvelope[] jumps = Enumerable
            .Range(6, 9)
            .Select(index =>
                Parse(
                    $$"""{"timestamp":"2026-07-10T13:{{index}}:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c{{index}}-0","SystemAddress":{{3000 + index}}}"""
                )
            )
            .ToArray();
        await coordinator.ApplyJournalEventsAsync(jumps);
        await coordinator.FlushAsync();
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);

        await viewModel.RefreshAsync();

        Assert.Equal(8, viewModel.RecentEntries.Count);
    }

    [AvaloniaFact]
    public async Task DetailShowsHeliumClassesAndAverages()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);
        await viewModel.OpenPrefixAsync("Praea Euq IL-P c5-");

        Assert.True(viewModel.IsDetailVisible);
        Assert.False(viewModel.CanShowSearchRollup);
        Assert.True(viewModel.IsSelectedBoxelScope);
        Assert.Equal("Entire saved search (not available)", viewModel.EntireSavedSearchScopeText);
        Assert.Contains(
            "Open statistics from Saved boxel searches",
            viewModel.StatisticsScopeDescription,
            StringComparison.Ordinal
        );
        Assert.Contains("HE", viewModel.HeliumText, StringComparison.Ordinal);
        Assert.Equal("Systems recorded: 1", viewModel.VisitedText);
        Assert.Equal("Highest recorded suffix: 0", viewModel.HighestRecordedSuffixText);
        BoxelSurveyClassRowViewModel water = Assert.Single(viewModel.ClassRows, row => row.Code == "WW");
        Assert.Equal(1, water.Count);
        Assert.Equal(BoxelSurveyAverageFormatter.Placeholder, water.Average);
        Assert.Equal(19 + 1, viewModel.ClassRows.Count);
    }

    [AvaloniaFact]
    public async Task AverageAppearsOnceMinimumVisitedIsReached()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);
        await viewModel.OpenPrefixAsync("Praea Euq IL-P c5-");

        BoxelSurveyClassRowViewModel water = Assert.Single(viewModel.ClassRows, row => row.Code == "WW");
        Assert.Equal(BoxelSurveyAverageFormatter.Placeholder, water.Average);

        viewModel.MinSystemsForAverages = 1;

        water = Assert.Single(viewModel.ClassRows, row => row.Code == "WW");
        Assert.Equal("1 in 1", water.Average);
        Assert.Empty(viewModel.StatusMessage);
        BoxelSurveyStatsPreferences saved = new BoxelSurveyStatsSettingsStore(
            Path.Combine(temporaryDirectory, "cross-platform-ui.json")
        ).Load();
        Assert.Equal(1, saved.MinSystemsForAverages);
    }

    [AvaloniaFact]
    public async Task RejectedStatisticsMinimumsNotifyBindingsToRestoreClampedValues()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => changes.Add(eventArgs.PropertyName);

        viewModel.MinSystemsForAverages = 1;
        changes.Clear();
        viewModel.MinSystemsForAverages = 0;

        Assert.Equal(1, viewModel.MinSystemsForAverages);
        Assert.Contains(nameof(viewModel.MinSystemsForAverages), changes);

        viewModel.MinSystemsForExport = 1000;
        changes.Clear();
        viewModel.MinSystemsForExport = 1001;

        Assert.Equal(1000, viewModel.MinSystemsForExport);
        Assert.Contains(nameof(viewModel.MinSystemsForExport), changes);
    }

    [AvaloniaFact]
    public async Task SearchRollupUsesFocusedPrefixes()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);
        await viewModel.FocusPrefixesAsync(["Praea Euq IL-P c5-", "Wregoe BU-Y b2-"], 'c');

        Assert.True(viewModel.CanShowSearchRollup);
        Assert.True(viewModel.IsSelectedBoxelScope);
        Assert.Equal("Entire saved search (2 boxels)", viewModel.EntireSavedSearchScopeText);
        Assert.Contains("selected boxel only", viewModel.StatisticsScopeDescription, StringComparison.Ordinal);

        viewModel.IsEntireSavedSearchScope = true;
        await viewModel.RefreshAsync();
        Assert.True(viewModel.IsEntireSavedSearchScope);
        Assert.Contains("saved search", viewModel.DetailTitle, StringComparison.Ordinal);
        Assert.Equal("Configured search systems: — (per-boxel only)", viewModel.ConfiguredSystemsText);
        Assert.Equal("Highest recorded suffix: — (per-boxel only)", viewModel.HighestRecordedSuffixText);
        Assert.Contains(
            "If only one boxel has recorded data",
            viewModel.StatisticsScopeDescription,
            StringComparison.Ordinal
        );
    }

    [AvaloniaFact]
    public async Task SingleBoxelSavedSearchExplainsWhyCombinedScopeIsUnavailable()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);

        await viewModel.FocusPrefixesAsync(["Praea Euq IL-P c5-"], 'c');

        Assert.False(viewModel.CanShowSearchRollup);
        Assert.True(viewModel.IsSelectedBoxelScope);
        Assert.Equal("Entire saved search (1 boxel)", viewModel.EntireSavedSearchScopeText);
        Assert.Contains("contains only one boxel", viewModel.StatisticsScopeDescription, StringComparison.Ordinal);

        viewModel.IsEntireSavedSearchScope = true;
        Assert.False(viewModel.IsEntireSavedSearchScope);
    }

    [AvaloniaFact]
    public async Task SavedSearchRefreshRaisesCommandChangesOnlyOnTheUiThread()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);
        await viewModel.FocusPrefixesAsync(["Praea Euq IL-P c5-", "Wregoe BU-Y b2-"], 'c');
        viewModel.IsEntireSavedSearchScope = true;
        await viewModel.RefreshAsync();
        var subscribedButton = new Button { Command = viewModel.RefreshCommand };

        await Task.Run(viewModel.RefreshAsync);

        Assert.False(viewModel.IsBusy);
        Assert.Contains("entire saved search", viewModel.DetailTitle, StringComparison.Ordinal);
        GC.KeepAlive(subscribedButton);
    }

    [AvaloniaFact]
    public async Task UnchangedRefreshKeepsExistingRowCollections()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);
        await viewModel.OpenPrefixAsync("Praea Euq IL-P c5-");
        IReadOnlyList<BoxelSurveyBrowserRowViewModel> browserRows = viewModel.BrowserRows;
        IReadOnlyList<BoxelSurveyClassRowViewModel> classRows = viewModel.ClassRows;
        IReadOnlyList<BoxelSurveyIndexEntry> recentEntries = viewModel.RecentEntries;
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => changes.Add(eventArgs.PropertyName);

        await viewModel.RefreshAsync();

        Assert.Same(browserRows, viewModel.BrowserRows);
        Assert.Same(classRows, viewModel.ClassRows);
        Assert.Same(recentEntries, viewModel.RecentEntries);
        Assert.DoesNotContain(nameof(viewModel.BrowserRows), changes);
        Assert.DoesNotContain(nameof(viewModel.ClassRows), changes);
        Assert.DoesNotContain(nameof(viewModel.RecentEntries), changes);
    }

    [AvaloniaFact]
    public async Task CoordinatorChangeBurstCoalescesUiRefreshes()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);
        await viewModel.OpenPrefixAsync("Praea Euq IL-P c5-");
        int busyTransitions = 0;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(viewModel.IsBusy))
            {
                busyTransitions++;
            }
        };
        MethodInfo? raiseChanged = typeof(BoxelSurveyStatsCoordinator).GetMethod(
            "RaiseChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        Assert.NotNull(raiseChanged);

        Task.Run(() =>
            {
                for (int index = 0; index < 20; index++)
                {
                    raiseChanged.Invoke(coordinator, null);
                }
            })
            .GetAwaiter()
            .GetResult();

        for (int attempt = 0; attempt < 50 && busyTransitions < 2; attempt++)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
            await Task.Delay(10);
        }

        await Task.Delay(50);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
        Assert.InRange(busyTransitions, 2, 4);
        Assert.False(viewModel.IsBusy);
    }

    [AvaloniaFact]
    public async Task ChildNavigationShowsOnlyRecordedDirectChildren()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        var parent = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        BoxelAddress child = parent.Children[0].WithSystemNumber(0);
        Assert.True(child.TryGetSystemAddress(out long childAddress));
        await coordinator.ApplyJournalEventsAsync([
            Parse(
                $$"""{"timestamp":"2026-07-10T13:00:00Z","event":"FSDJump","StarSystem":"{{child.Name}}","SystemAddress":{{childAddress}}}"""
            ),
        ]);
        await coordinator.FlushAsync();
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);
        await viewModel.OpenPrefixAsync(parent.Prefix);
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => changes.Add(eventArgs.PropertyName);

        viewModel.ExploreChildrenCommand.Execute(null);

        Assert.False(viewModel.IsDetailVisible);
        Assert.True(viewModel.IsBrowsingChildren);
        Assert.Equal((char)(parent.MassCode - 1), viewModel.SelectedMassCode);
        Assert.Contains(nameof(viewModel.SelectedMassCode), changes);
        BoxelSurveyBrowserRowViewModel row = Assert.Single(viewModel.BrowserRows);
        Assert.Equal(child.Prefix, row.Prefix);
        Assert.Equal(0, row.Indent);
        Assert.Contains(parent.Prefix, viewModel.BrowserDescription, StringComparison.Ordinal);

        viewModel.ShowAllMassCodeCommand.Execute(null);
        Assert.False(viewModel.IsBrowsingChildren);
    }

    [AvaloniaFact]
    public async Task MainEntryClearsAnEarlierSavedSearchRollup()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);
        await viewModel.FocusPrefixesAsync(["Praea Euq IL-P c5-", "Wregoe BU-Y b2-"], 'c');
        viewModel.ShowSearchRollup = true;
        await viewModel.RefreshAsync();
        Assert.Contains("saved search", viewModel.DetailTitle, StringComparison.Ordinal);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.CanShowSearchRollup);
        Assert.False(viewModel.ShowSearchRollup);
        Assert.DoesNotContain("saved search", viewModel.DetailTitle, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task UnscopedMainEntryClearsEarlierMassCodeFilters()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        await coordinator.ApplyJournalEventsAsync([
            Parse(
                """{"timestamp":"2026-07-10T13:00:00Z","event":"FSDJump","StarSystem":"Wregoe BU-Y b2-0","SystemAddress":3001}"""
            ),
        ]);
        await coordinator.FlushAsync();
        await coordinator.SwitchCommanderAsync(null);
        await coordinator.SwitchCommanderAsync("F123");
        Assert.Null(coordinator.Current);
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);
        await viewModel.RefreshAsync();
        Assert.Equal(2, viewModel.BrowserRows.Count);
        Assert.Equal(2, viewModel.RecentEntries.Count);

        viewModel.SelectMassCodeCommand.Execute('c');
        Assert.Single(viewModel.BrowserRows);
        Assert.Single(viewModel.RecentEntries);

        await viewModel.InitializeAsync();

        Assert.All(viewModel.MassCodes, option => Assert.False(option.IsSelected));
        Assert.Equal(2, viewModel.BrowserRows.Count);
        Assert.Equal(2, viewModel.RecentEntries.Count);
    }

    [AvaloniaFact]
    public async Task ExportSkipsBelowMinimumAndWritesWhenLowered()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);
        await viewModel.OpenPrefixAsync("Praea Euq IL-P c5-");
        await viewModel.ExportAsync();
        Assert.Null(viewModel.LastExportDirectory);

        viewModel.MinSystemsForExport = 1;
        string selectedDirectory = Path.Combine(temporaryDirectory, "chosen-export-folder");
        await viewModel.ExportAsync(selectedDirectory);
        Assert.Equal(Path.GetFullPath(selectedDirectory), viewModel.LastExportDirectory);
        Assert.True(Directory.Exists(selectedDirectory));
        Assert.NotEmpty(Directory.GetFiles(selectedDirectory, "*.csv"));
        Assert.NotEmpty(Directory.GetFiles(selectedDirectory, "*.json"));
        Assert.NotEmpty(viewModel.RecentEntries);
    }

    [AvaloniaFact]
    public async Task PersistenceFailureIsReportedAsStatus()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        using BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);
        string storeDirectory = Path.Combine(temporaryDirectory, BoxelSurveyStatsStore.StoreDirectoryName);
        Directory.Delete(storeDirectory, recursive: true);
        await File.WriteAllTextAsync(storeDirectory, "blocked");
        await coordinator.ApplyJournalEventsAsync([
            Parse(
                """{"timestamp":"2026-07-10T13:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-1","SystemAddress":2002}"""
            ),
        ]);

        await coordinator.FlushAsync();
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Contains("Could not save boxel survey statistics", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task DisposeUnsubscribesFromCoordinatorChanges()
    {
        using BoxelSurveyStatsCoordinator coordinator = await CreateCoordinatorWithSystemAsync();
        BoxelSurveyStatsViewModel viewModel = CreateViewModel(coordinator);
        viewModel.ReportStatus("unchanged");
        FieldInfo? eventField = typeof(BoxelSurveyStatsCoordinator).GetField(
            nameof(BoxelSurveyStatsCoordinator.Changed),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        MulticastDelegate before = Assert.IsType<MulticastDelegate>(
            eventField?.GetValue(coordinator),
            exactMatch: false
        );
        Assert.Contains(before.GetInvocationList(), handler => ReferenceEquals(handler.Target, viewModel));

        viewModel.Dispose();
        await coordinator.ApplyJournalEventsAsync([
            Parse(
                """{"timestamp":"2026-07-10T13:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-1","SystemAddress":2002}"""
            ),
        ]);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
        var after = eventField?.GetValue(coordinator) as MulticastDelegate;

        Assert.DoesNotContain(after?.GetInvocationList() ?? [], handler => ReferenceEquals(handler.Target, viewModel));
        Assert.Equal("unchanged", viewModel.StatusMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private BoxelSurveyStatsViewModel CreateViewModel(BoxelSurveyStatsCoordinator coordinator)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string settingsPath = Path.Combine(temporaryDirectory, "cross-platform-ui.json");
        return new BoxelSurveyStatsViewModel(coordinator, new BoxelSurveyStatsSettingsStore(settingsPath));
    }

    private async Task<BoxelSurveyStatsCoordinator> CreateCoordinatorWithSystemAsync()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var coordinator = new BoxelSurveyStatsCoordinator(
            new BoxelSurveyStatsStore(temporaryDirectory),
            TimeSpan.FromHours(1)
        );
        await coordinator.SwitchCommanderAsync("F123");
        await coordinator.ApplyJournalEventsAsync([
            Parse(
                """{"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-0","SystemAddress":2001}"""
            ),
            Parse(
                """{"event":"Scan","SystemAddress":2001,"BodyID":2,"PlanetClass":"Water world","MassEM":1,"AtmosphereComposition":[{"Name":"Helium","Percent":28.5}]}"""
            ),
        ]);
        await coordinator.FlushAsync();
        return coordinator;
    }
}
