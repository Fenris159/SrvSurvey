using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void NavigationSeparatesImplementedAndPendingSurfaces()
    {
        var viewModel = new MainWindowViewModel(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));

        Assert.Equal(9, viewModel.NavigationItems.Count);
        Assert.Equal(4, viewModel.NavigationItems.Count(item => item.IsImplemented));
        Assert.True(viewModel.IsOverviewSelected);

        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(
            item => item.Key == "exobiology");

        Assert.True(viewModel.IsPendingSelected);
        Assert.Equal("Exobiology", viewModel.PendingPageTitle);
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
                    + "{\"timestamp\":\"2026-07-24T10:00:01Z\",\"event\":\"Location\",\"StarSystem\":\"Sol\",\"SystemAddress\":10477373803,\"Body\":\"Earth\",\"BodyType\":\"Planet\"}\n");
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
}
