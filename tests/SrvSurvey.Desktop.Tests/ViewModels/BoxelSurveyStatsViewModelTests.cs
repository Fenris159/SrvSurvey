using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class BoxelSurveyStatsViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-BoxelSurveyStatsViewModelTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MassCodeFilterListsKnownPrefixesAndUnvisitedChildren()
    {
        var coordinator = await CreateCoordinatorWithSystemAsync();
        using var viewModel = CreateViewModel(coordinator);
        viewModel.SelectedMassCode = 'c';
        await viewModel.RefreshAsync();

        Assert.Contains(viewModel.BrowserRows, row =>
            row.Prefix == "Praea Euq IL-P c5-" && row.Indent == 0);
        Assert.Contains(viewModel.BrowserRows, row => row.Indent == 1);
        Assert.Contains(viewModel.BrowserRows, row => row.Status == "not visited");
    }

    [Fact]
    public async Task DetailShowsHeliumClassesAndAverages()
    {
        var coordinator = await CreateCoordinatorWithSystemAsync();
        using var viewModel = CreateViewModel(coordinator);
        await viewModel.OpenPrefixAsync("Praea Euq IL-P c5-");

        Assert.True(viewModel.IsDetailVisible);
        Assert.Contains("HE", viewModel.HeliumText, StringComparison.Ordinal);
        Assert.Contains("1 / 1", viewModel.VisitedText, StringComparison.Ordinal);
        var water = Assert.Single(
            viewModel.ClassRows,
            row => row.Code == "WW");
        Assert.Equal(1, water.Count);
        Assert.Equal(BoxelSurveyAverageFormatter.Placeholder, water.Average);
        Assert.Equal(19 + 1, viewModel.ClassRows.Count);
    }

    [Fact]
    public async Task AverageAppearsOnceMinimumVisitedIsReached()
    {
        var coordinator = await CreateCoordinatorWithSystemAsync();
        using var viewModel = CreateViewModel(coordinator);
        viewModel.MinAveragesText = "1";
        await viewModel.OpenPrefixAsync("Praea Euq IL-P c5-");

        var water = Assert.Single(viewModel.ClassRows, row => row.Code == "WW");
        Assert.Equal("1 in 1", water.Average);
    }

    [Fact]
    public async Task SearchRollupUsesFocusedPrefixes()
    {
        var coordinator = await CreateCoordinatorWithSystemAsync();
        using var viewModel = CreateViewModel(coordinator);
        await viewModel.FocusPrefixesAsync(
            ["Praea Euq IL-P c5-", "Wregoe BU-Y b2-"],
            'c');

        Assert.True(viewModel.CanShowSearchRollup);
        viewModel.ShowSearchRollup = true;
        await viewModel.RefreshAsync();
        Assert.Contains("saved search", viewModel.DetailTitle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportSkipsBelowMinimumAndWritesWhenLowered()
    {
        var coordinator = await CreateCoordinatorWithSystemAsync();
        using var viewModel = CreateViewModel(coordinator);
        await viewModel.OpenPrefixAsync("Praea Euq IL-P c5-");
        await viewModel.ExportAsync();
        Assert.Null(viewModel.LastExportDirectory);

        viewModel.MinExportText = "1";
        await viewModel.ExportAsync();
        Assert.NotNull(viewModel.LastExportDirectory);
        Assert.True(Directory.Exists(viewModel.LastExportDirectory));
        Assert.NotEmpty(Directory.GetFiles(viewModel.LastExportDirectory, "*.csv"));
        Assert.NotEmpty(Directory.GetFiles(viewModel.LastExportDirectory, "*.json"));
        Assert.NotEmpty(viewModel.RecentEntries);
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

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }
}
