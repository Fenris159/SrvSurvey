using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void NavigationSeparatesImplementedAndPendingSurfaces()
    {
        var viewModel = new MainWindowViewModel(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));

        Assert.Equal(9, viewModel.NavigationItems.Count);
        Assert.Equal(9, viewModel.NavigationItems.Count(item => item.IsImplemented));
        Assert.True(viewModel.IsOverviewSelected);

        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(
            item => item.Key == "exobiology");

        Assert.True(viewModel.IsExobiologySelected);
        Assert.False(viewModel.IsPendingSelected);

        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(
            item => item.Key == "travel");

        Assert.True(viewModel.IsTravelSelected);
        Assert.False(viewModel.IsPendingSelected);

        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(
            item => item.Key == "search");

        Assert.True(viewModel.IsSearchSelected);
        Assert.False(viewModel.IsPendingSelected);

        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(
            item => item.Key == "guardian");

        Assert.True(viewModel.IsGuardianSelected);
        Assert.False(viewModel.IsPendingSelected);

        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(
            item => item.Key == "colonisation");

        Assert.True(viewModel.IsColonizationSelected);
        Assert.False(viewModel.IsPendingSelected);
    }

    [Fact]
    public void ThemeGalleryContainsEveryRavenTheme()
    {
        var viewModel = new MainWindowViewModel(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));

        Assert.Equal(5, viewModel.ThemeOptions.Count);
        Assert.Equal("Blue (dark)", viewModel.SelectedThemeName);
    }

    [Fact]
    public async Task LegacyProfileCanBeImportedFromSettingsWorkflow()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-profile-vm-tests-{Guid.NewGuid():N}");
        try
        {
            var source = Path.Combine(root, "legacy");
            var data = Path.Combine(root, "current");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(
                Path.Combine(source, "settings.json"),
                "{\"unknownFutureField\":42}");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                data,
                Path.Combine(root, "cache"),
                [new LegacyProfileCandidate(LegacyProfileLocationKind.Desktop, source)]);
            var viewModel = new MainWindowViewModel(
                Path.Combine(root, "missing-journals"),
                appDataPaths: paths);

            await viewModel.ImportLegacyProfileAsync();

            Assert.True(File.Exists(Path.Combine(data, "settings.json")));
            Assert.Contains("Imported 1 files", viewModel.ProfileStatusMessage);
            Assert.True(Directory.Exists(viewModel.ProfileBackupDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task RefreshAppliesLiveJournalAndStatusState()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-live-vm-tests-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Journal.2026-07-24T100000.01.log"),
                "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + "{\"timestamp\":\"2026-07-24T10:00:01Z\",\"event\":\"Location\",\"StarSystem\":\"Sol\",\"SystemAddress\":10477373803,\"StarPos\":[0,0,0],\"Body\":\"Earth\",\"BodyType\":\"Planet\"}\n");
            await File.WriteAllTextAsync(
                Path.Combine(root, StatusFileReader.FileName),
                "{\"timestamp\":\"2026-07-24T10:00:02Z\",\"event\":\"Status\",\"Flags\":69206016,\"Flags2\":0,\"Latitude\":12.5,\"Longitude\":-44.25,\"Heading\":-1,\"Altitude\":123.4}");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "profile"),
                Path.Combine(root, "cache"),
                []);
            var viewModel = new MainWindowViewModel(root, appDataPaths: paths);

            await viewModel.RefreshAsync();

            Assert.Equal("Drew", viewModel.CommanderName);
            Assert.Contains("Sol", viewModel.SystemDescription);
            Assert.Equal("Earth", viewModel.BodyName);
            Assert.Equal("SRV", viewModel.VehicleState);
            Assert.Equal("12.500000, -44.250000", viewModel.SurfacePosition);
            Assert.Equal("359° / 123 m", viewModel.HeadingAndAltitude);
            Assert.Equal("Sol", viewModel.Search.CurrentSystemName);
            Assert.Equal("[ 0, 0, 0 ]", viewModel.Search.CurrentPosition);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task RefreshConnectsPersistedBoxelSearchRouteAndLiveCompletion()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-live-boxel-vm-tests-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            var profile = Path.Combine(root, "profile");
            Directory.CreateDirectory(journals);
            var journalPath = Path.Combine(
                journals,
                "Journal.2026-07-24T100000.01.log");
            await File.WriteAllTextAsync(
                journalPath,
                "{\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                    + "{\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + "{\"event\":\"Location\",\"StarSystem\":\"Praea Euq IL-P c5-0\","
                    + "\"SystemAddress\":100,\"StarPos\":[1,2,3]}\n");
            await File.WriteAllTextAsync(
                Path.Combine(journals, NavRouteFileReader.FileName),
                "{\"event\":\"NavRoute\",\"Route\":[{"
                    + "\"StarSystem\":\"Praea Euq IL-P c5-1\","
                    + "\"SystemAddress\":101,\"StarPos\":[4,5,6]}]}");
            var store = new CommanderProfileStore(profile);
            var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
            await store.SaveBoxelSearchAsync(
                "F123",
                "Drew",
                true,
                new BoxelSearchSnapshot(
                    true,
                    top,
                    DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                    top,
                    2,
                    'c',
                    [],
                    true,
                    false,
                    false,
                    false,
                    BoxelCompletionMode.EnterSystem));
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                profile,
                Path.Combine(root, "cache"),
                []);
            var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths,
                boxelSystemResolver: new StubBoxelResolver(
                [
                    BoxelObservation("Praea Euq IL-P c5-0", 100),
                ]));

            await viewModel.RefreshAsync();

            Assert.True(viewModel.BoxelSearch.IsActive);
            Assert.Equal(2, viewModel.BoxelSearch.Systems.Count);
            Assert.True(viewModel.BoxelSearch.Systems[1].IsKnown);
            Assert.False(viewModel.BoxelSearch.Systems[1].IsComplete);

            await File.AppendAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-24T12:00:00Z\",\"event\":\"FSDJump\","
                    + "\"StarSystem\":\"Praea Euq IL-P c5-1\","
                    + "\"SystemAddress\":101,\"StarPos\":[4,5,6]}\n");
            await viewModel.RefreshAsync();

            Assert.True(viewModel.BoxelSearch.Systems[1].IsComplete);
            Assert.Equal("Praea Euq IL-P c5-0", viewModel.BoxelSearch.NextSystem);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ExplorationUsesImportedTotalsThenPersistsNewEventsAndReset()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-exploration-vm-tests-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            var profile = Path.Combine(root, "profile");
            Directory.CreateDirectory(journals);
            Directory.CreateDirectory(profile);
            var journalPath = Path.Combine(
                journals,
                "Journal.2026-07-24T100000.01.log");
            await File.WriteAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                    + "{\"timestamp\":\"2026-07-24T10:00:01Z\",\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + "{\"timestamp\":\"2026-07-24T10:00:02Z\",\"event\":\"StartJump\",\"JumpType\":\"Hyperspace\"}\n");
            var store = new CommanderProfileStore(profile);
            await store.SaveExplorationAsync(
                "F123",
                "Drew",
                isOdyssey: true,
                new ExplorationSnapshot(1000, 100, 10, 2, 3, 4));
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                profile,
                Path.Combine(root, "cache"),
                []);
            var viewModel = new MainWindowViewModel(journals, appDataPaths: paths);

            await viewModel.RefreshAsync();

            Assert.Equal("10", viewModel.ExplorationJumps);
            Assert.Equal("100.0 ly", viewModel.ExplorationDistance);
            Assert.Equal("1,000 CR", viewModel.EstimatedExplorationValue);

            await File.AppendAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-24T10:00:03Z\",\"event\":\"StartJump\",\"JumpType\":\"Hyperspace\"}\n"
                    + "{\"timestamp\":\"2026-07-24T10:00:04Z\",\"event\":\"FSDJump\",\"JumpDist\":5.25}\n");
            await viewModel.RefreshAsync();

            Assert.Equal("11", viewModel.ExplorationJumps);
            Assert.Equal("105.2 ly", viewModel.ExplorationDistance);
            var saved = await store.LoadAsync("F123", isOdyssey: true);
            Assert.Equal(11, saved.Data!.Exploration.JumpCount);
            Assert.Equal(105.25, saved.Data.Exploration.DistanceTravelled);

            await viewModel.ResetExplorationAsync();
            Assert.True(viewModel.IsResetExplorationPending);
            await viewModel.ResetExplorationAsync();

            Assert.False(viewModel.IsResetExplorationPending);
            Assert.Equal("0", viewModel.ExplorationJumps);
            saved = await store.LoadAsync("F123", isOdyssey: true);
            Assert.Equal(ExplorationSnapshot.Empty, saved.Data!.Exploration);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ExobiologyUsesImportedStateThenPersistsLiveScanAndClear()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-exobiology-vm-tests-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            var profile = Path.Combine(root, "profile");
            Directory.CreateDirectory(journals);
            Directory.CreateDirectory(profile);
            var journalPath = Path.Combine(
                journals,
                "Journal.2026-07-24T100000.01.log");
            const string variant = "$Codex_Ent_Aleoids_01_B_Name;";
            const string species = "$Codex_Ent_Aleoids_01_Name;";
            const string genus = "$Codex_Ent_Aleoids_Genus_Name;";
            await File.WriteAllTextAsync(
                journalPath,
                "{\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                    + "{\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + $"{{\"event\":\"ScanOrganic\",\"ScanType\":\"Log\",\"Genus\":\"{genus}\",\"Species\":\"{species}\",\"Variant\":\"{variant}\",\"SystemAddress\":123,\"Body\":1}}\n"
                    + $"{{\"event\":\"ScanOrganic\",\"ScanType\":\"Sample\",\"Genus\":\"{genus}\",\"Species\":\"{species}\",\"Variant\":\"{variant}\",\"SystemAddress\":123,\"Body\":1}}\n"
                    + $"{{\"event\":\"ScanOrganic\",\"ScanType\":\"Analyse\",\"Genus\":\"{genus}\",\"Species\":\"{species}\",\"Variant\":\"{variant}\",\"SystemAddress\":123,\"Body\":1}}\n");
            var store = new CommanderProfileStore(profile);
            await store.SaveExobiologyAsync(
                "F123",
                "Drew",
                true,
                new ExobiologySnapshot(
                    null,
                    null,
                    null,
                    500,
                    ["999_1_2310101_500_False"],
                    0));
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                profile,
                Path.Combine(root, "cache"),
                []);
            var viewModel = new MainWindowViewModel(journals, appDataPaths: paths);

            await viewModel.RefreshAsync();

            Assert.Equal("500 CR", viewModel.UnclaimedBioRewards);
            Assert.Equal("1 organism", viewModel.UnclaimedBioScans);

            await File.AppendAllTextAsync(
                journalPath,
                $"{{\"event\":\"ScanOrganic\",\"ScanType\":\"Log\",\"Genus\":\"{genus}\",\"Species\":\"{species}\",\"Variant\":\"{variant}\",\"SystemAddress\":456,\"Body\":2}}\n"
                    + $"{{\"event\":\"ScanOrganic\",\"ScanType\":\"Sample\",\"Genus\":\"{genus}\",\"Species\":\"{species}\",\"Variant\":\"{variant}\",\"SystemAddress\":456,\"Body\":2}}\n"
                    + $"{{\"event\":\"ScanOrganic\",\"ScanType\":\"Analyse\",\"Genus\":\"{genus}\",\"Species\":\"{species}\",\"Variant\":\"{variant}\",\"SystemAddress\":456,\"Body\":2}}\n");
            await viewModel.RefreshAsync();

            Assert.Equal("7,253,000 CR", viewModel.UnclaimedBioRewards);
            Assert.Equal("2 organisms", viewModel.UnclaimedBioScans);
            var saved = await store.LoadAsync("F123", true);
            Assert.Equal(7_253_000, saved.Data!.Exobiology.OrganicRewards);
            Assert.Equal(2, saved.Data.Exobiology.ScannedBioEntryIds.Count);

            await viewModel.ResetExobiologyAsync();
            Assert.True(viewModel.IsResetExobiologyPending);
            await viewModel.ResetExobiologyAsync();

            Assert.False(viewModel.IsResetExobiologyPending);
            Assert.Equal("0 CR", viewModel.UnclaimedBioRewards);
            saved = await store.LoadAsync("F123", true);
            Assert.Equal(0, saved.Data!.Exobiology.OrganicRewards);
            Assert.Empty(saved.Data.Exobiology.ScannedBioEntryIds);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static BoxelSystemObservation BoxelObservation(
        string name,
        long address)
    {
        return new BoxelSystemObservation(
            BoxelAddress.Parse(name) with { SystemAddress = address },
            new GalacticCoordinate(address, 0, 0),
            null,
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            true);
    }

    private sealed class StubBoxelResolver(
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
