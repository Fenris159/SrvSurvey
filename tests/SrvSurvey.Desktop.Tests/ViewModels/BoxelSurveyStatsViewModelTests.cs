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
        "SrvSurvey-BoxelSurveyStatsViewModelTests-" + Guid.NewGuid().ToString("N"));

    [AvaloniaFact]
    public async Task MassCodeFilterListsOnlyRecordedPrefixesAtThatExactCode()
    {
        using var coordinator = await CreateCoordinatorWithSystemAsync();
        using var viewModel = CreateViewModel(coordinator);
        viewModel.SelectedMassCode = 'c';
        await viewModel.RefreshAsync();

        var row = Assert.Single(viewModel.BrowserRows);
        Assert.Equal("Praea Euq IL-P c5-", row.Prefix);
        Assert.Equal(0, row.Indent);
        Assert.DoesNotContain("0 / 0", row.Glance, StringComparison.Ordinal);
        Assert.Contains("1 recorded", row.Glance, StringComparison.Ordinal);
        Assert.Contains("MASS CODE C", viewModel.BrowserTitle, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task DetailShowsHeliumClassesAndAverages()
    {
        using var coordinator = await CreateCoordinatorWithSystemAsync();
        using var viewModel = CreateViewModel(coordinator);
        await viewModel.OpenPrefixAsync("Praea Euq IL-P c5-");

        Assert.True(viewModel.IsDetailVisible);
        Assert.False(viewModel.CanShowSearchRollup);
        Assert.True(viewModel.IsSelectedBoxelScope);
        Assert.Equal(
            "Entire saved search (not available)",
            viewModel.EntireSavedSearchScopeText);
        Assert.Contains(
            "Open statistics from Saved boxel searches",
            viewModel.StatisticsScopeDescription,
            StringComparison.Ordinal);
        Assert.Contains("HE", viewModel.HeliumText, StringComparison.Ordinal);
        Assert.Equal("Systems recorded: 1", viewModel.VisitedText);
        Assert.Equal("Highest recorded suffix: 0", viewModel.HighestRecordedSuffixText);
        var water = Assert.Single(
            viewModel.ClassRows,
            row => row.Code == "WW");
        Assert.Equal(1, water.Count);
        Assert.Equal(BoxelSurveyAverageFormatter.Placeholder, water.Average);
        Assert.Equal(19 + 1, viewModel.ClassRows.Count);
    }

    [AvaloniaFact]
    public async Task AverageAppearsOnceMinimumVisitedIsReached()
    {
        using var coordinator = await CreateCoordinatorWithSystemAsync();
        using var viewModel = CreateViewModel(coordinator);
        await viewModel.OpenPrefixAsync("Praea Euq IL-P c5-");

        var water = Assert.Single(viewModel.ClassRows, row => row.Code == "WW");
        Assert.Equal(BoxelSurveyAverageFormatter.Placeholder, water.Average);

        viewModel.MinSystemsForAverages = 1;

        water = Assert.Single(viewModel.ClassRows, row => row.Code == "WW");
        Assert.Equal("1 in 1", water.Average);
        Assert.Empty(viewModel.StatusMessage);
        var saved = new BoxelSurveyStatsSettingsStore(Path.Combine(
            temporaryDirectory,
            "cross-platform-ui.json")).Load();
        Assert.Equal(1, saved.MinSystemsForAverages);
    }

    [AvaloniaFact]
    public async Task RejectedStatisticsMinimumsNotifyBindingsToRestoreClampedValues()
    {
        using var coordinator = await CreateCoordinatorWithSystemAsync();
        using var viewModel = CreateViewModel(coordinator);
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
        using var coordinator = await CreateCoordinatorWithSystemAsync();
        using var viewModel = CreateViewModel(coordinator);
        await viewModel.FocusPrefixesAsync(
            ["Praea Euq IL-P c5-", "Wregoe BU-Y b2-"],
            'c');

        Assert.True(viewModel.CanShowSearchRollup);
        Assert.True(viewModel.IsSelectedBoxelScope);
        Assert.Equal("Entire saved search (2 boxels)", viewModel.EntireSavedSearchScopeText);
        Assert.Contains(
            "selected boxel only",
            viewModel.StatisticsScopeDescription,
            StringComparison.Ordinal);

        viewModel.IsEntireSavedSearchScope = true;
        await viewModel.RefreshAsync();
        Assert.True(viewModel.IsEntireSavedSearchScope);
        Assert.Contains("saved search", viewModel.DetailTitle, StringComparison.Ordinal);
        Assert.Equal(
            "Configured search systems: — (per-boxel only)",
            viewModel.ConfiguredSystemsText);
        Assert.Equal(
            "Highest recorded suffix: — (per-boxel only)",
            viewModel.HighestRecordedSuffixText);
        Assert.Contains(
            "If only one boxel has recorded data",
            viewModel.StatisticsScopeDescription,
            StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task SingleBoxelSavedSearchExplainsWhyCombinedScopeIsUnavailable()
    {
        using var coordinator = await CreateCoordinatorWithSystemAsync();
        using var viewModel = CreateViewModel(coordinator);

        await viewModel.FocusPrefixesAsync(["Praea Euq IL-P c5-"], 'c');

        Assert.False(viewModel.CanShowSearchRollup);
        Assert.True(viewModel.IsSelectedBoxelScope);
        Assert.Equal(
            "Entire saved search (1 boxel)",
            viewModel.EntireSavedSearchScopeText);
        Assert.Contains(
            "contains only one boxel",
            viewModel.StatisticsScopeDescription,
            StringComparison.Ordinal);

        viewModel.IsEntireSavedSearchScope = true;
        Assert.False(viewModel.IsEntireSavedSearchScope);
    }

    [AvaloniaFact]
    public async Task SavedSearchRefreshRaisesCommandChangesOnlyOnTheUiThread()
    {
        using var coordinator = await CreateCoordinatorWithSystemAsync();
        using var viewModel = CreateViewModel(coordinator);
        await viewModel.FocusPrefixesAsync(
            ["Praea Euq IL-P c5-", "Wregoe BU-Y b2-"],
            'c');
        viewModel.IsEntireSavedSearchScope = true;
        await viewModel.RefreshAsync();
        var subscribedButton = new Button
        {
            Command = viewModel.RefreshCommand,
        };

        await Task.Run(viewModel.RefreshAsync);

        Assert.False(viewModel.IsBusy);
        Assert.Contains(
            "entire saved search",
            viewModel.DetailTitle,
            StringComparison.Ordinal);
        GC.KeepAlive(subscribedButton);
    }

    [AvaloniaFact]
    public async Task ChildNavigationShowsOnlyRecordedDirectChildren()
    {
        using var coordinator = await CreateCoordinatorWithSystemAsync();
        var parent = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var child = parent.Children[0].WithSystemNumber(0);
        Assert.True(child.TryGetSystemAddress(out var childAddress));
        await coordinator.ApplyJournalEventsAsync(
        [
            Parse(
                $$"""{"timestamp":"2026-07-10T13:00:00Z","event":"FSDJump","StarSystem":"{{child.Name}}","SystemAddress":{{childAddress}}}"""),
        ]);
        await coordinator.FlushAsync();
        using var viewModel = CreateViewModel(coordinator);
        await viewModel.OpenPrefixAsync(parent.Prefix);
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => changes.Add(eventArgs.PropertyName);

        viewModel.ExploreChildrenCommand.Execute(null);

        Assert.False(viewModel.IsDetailVisible);
        Assert.True(viewModel.IsBrowsingChildren);
        Assert.Equal((char)(parent.MassCode - 1), viewModel.SelectedMassCode);
        Assert.Contains(nameof(viewModel.SelectedMassCode), changes);
        var row = Assert.Single(viewModel.BrowserRows);
        Assert.Equal(child.Prefix, row.Prefix);
        Assert.Equal(0, row.Indent);
        Assert.Contains(parent.Prefix, viewModel.BrowserDescription, StringComparison.Ordinal);

        viewModel.ShowAllMassCodeCommand.Execute(null);
        Assert.False(viewModel.IsBrowsingChildren);
    }

    [AvaloniaFact]
    public async Task MainEntryClearsAnEarlierSavedSearchRollup()
    {
        using var coordinator = await CreateCoordinatorWithSystemAsync();
        using var viewModel = CreateViewModel(coordinator);
        await viewModel.FocusPrefixesAsync(
            ["Praea Euq IL-P c5-", "Wregoe BU-Y b2-"],
            'c');
        viewModel.ShowSearchRollup = true;
        await viewModel.RefreshAsync();
        Assert.Contains("saved search", viewModel.DetailTitle, StringComparison.Ordinal);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.CanShowSearchRollup);
        Assert.False(viewModel.ShowSearchRollup);
        Assert.DoesNotContain("saved search", viewModel.DetailTitle, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ExportSkipsBelowMinimumAndWritesWhenLowered()
    {
        using var coordinator = await CreateCoordinatorWithSystemAsync();
        using var viewModel = CreateViewModel(coordinator);
        await viewModel.OpenPrefixAsync("Praea Euq IL-P c5-");
        await viewModel.ExportAsync();
        Assert.Null(viewModel.LastExportDirectory);

        viewModel.MinSystemsForExport = 1;
        var selectedDirectory = Path.Combine(temporaryDirectory, "chosen-export-folder");
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
        using var coordinator = await CreateCoordinatorWithSystemAsync();
        using var viewModel = CreateViewModel(coordinator);
        var storeDirectory = Path.Combine(
            temporaryDirectory,
            BoxelSurveyStatsStore.StoreDirectoryName);
        Directory.Delete(storeDirectory, recursive: true);
        await File.WriteAllTextAsync(storeDirectory, "blocked");
        await coordinator.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-10T13:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-1","SystemAddress":2002}"""),
        ]);

        await coordinator.FlushAsync();
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Contains(
            "Could not save boxel survey statistics",
            viewModel.StatusMessage,
            StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task DisposeUnsubscribesFromCoordinatorChanges()
    {
        using var coordinator = await CreateCoordinatorWithSystemAsync();
        var viewModel = CreateViewModel(coordinator);
        viewModel.ReportStatus("unchanged");
        var eventField = typeof(BoxelSurveyStatsCoordinator).GetField(
            nameof(BoxelSurveyStatsCoordinator.Changed),
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
        var before = Assert.IsAssignableFrom<MulticastDelegate>(
            eventField?.GetValue(coordinator));
        Assert.Contains(
            before.GetInvocationList(),
            handler => ReferenceEquals(handler.Target, viewModel));

        viewModel.Dispose();
        await coordinator.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-10T13:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-1","SystemAddress":2002}"""),
        ]);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
        var after = eventField?.GetValue(coordinator) as MulticastDelegate;

        Assert.DoesNotContain(
            after?.GetInvocationList() ?? [],
            handler => ReferenceEquals(handler.Target, viewModel));
        Assert.Equal("unchanged", viewModel.StatusMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private BoxelSurveyStatsViewModel CreateViewModel(
        BoxelSurveyStatsCoordinator coordinator)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var settingsPath = Path.Combine(temporaryDirectory, "cross-platform-ui.json");
        return new BoxelSurveyStatsViewModel(
            coordinator,
            new BoxelSurveyStatsSettingsStore(settingsPath));
    }

    private async Task<BoxelSurveyStatsCoordinator> CreateCoordinatorWithSystemAsync()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var coordinator = new BoxelSurveyStatsCoordinator(
            new BoxelSurveyStatsStore(temporaryDirectory),
            TimeSpan.FromHours(1));
        await coordinator.SwitchCommanderAsync("F123");
        await coordinator.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-0","SystemAddress":2001}"""),
            Parse(
                """{"event":"Scan","SystemAddress":2001,"BodyID":2,"PlanetClass":"Water world","MassEM":1,"AtmosphereComposition":[{"Name":"Helium","Percent":28.5}]}"""),
        ]);
        await coordinator.FlushAsync();
        return coordinator;
    }

}
