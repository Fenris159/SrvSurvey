using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Storage;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void NavigationSeparatesImplementedAndPendingSurfaces()
    {
        var viewModel = new MainWindowViewModel(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));

        Assert.Equal(10, viewModel.NavigationItems.Count);
        Assert.Equal(10, viewModel.NavigationItems.Count(item => item.IsImplemented));
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
            item => item.Key == "quests");

        Assert.True(viewModel.IsQuestsSelected);
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
    public async Task LiveShowCommandOpensTheLatestBiologyCodexEntry()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-main-codex-show-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            Directory.CreateDirectory(journals);
            var journalPath = Path.Combine(
                journals,
                "Journal.2026-07-25T120000.01.log");
            await File.WriteAllTextAsync(
                journalPath,
                """
                {"timestamp":"2026-07-25T12:00:00Z","event":"Location","StarSystem":"Test","SystemAddress":42,"StarPos":[0,0,0],"Population":0}
                {"timestamp":"2026-07-25T12:00:01Z","event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}]}
                {"timestamp":"2026-07-25T12:00:02Z","event":"CodexEntry","SystemAddress":42,"BodyID":1,"EntryID":2310101,"Name_Localised":"Aleoida Arcus - Green","SubCategory":"$Codex_SubCategory_Organic_Structures;"}

                """);
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "data"),
                Path.Combine(root, "cache"),
                []);
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths);
            long? selectedAtOpen = null;
            viewModel.BiologyCodex.SetWindowOpener(() =>
            {
                selectedAtOpen = viewModel.BiologyCodex.SelectedOrganism?.EntryId;
                return Task.FromResult(true);
            });

            await viewModel.RefreshAsync();
            Assert.Null(selectedAtOpen);

            await File.AppendAllTextAsync(
                journalPath,
                """
                {"timestamp":"2026-07-25T12:00:03Z","event":"SendText","Message":".show"}

                """);
            await viewModel.RefreshAsync();

            Assert.Equal(2310101, selectedAtOpen);
            Assert.Equal(
                2310101,
                viewModel.BiologyCodex.SelectedOrganism!.EntryId);
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
    public async Task GreenGasGiantOptInPublishesOnlyNewLiveScans()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-main-ggg-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            Directory.CreateDirectory(journals);
            var journalPath = Path.Combine(
                journals,
                "Journal.2026-07-25T120000.01.log");
            await File.WriteAllTextAsync(
                journalPath,
                "{\"event\":\"Commander\",\"Name\":\"Test Cmdr\"}\n"
                    + "{\"event\":\"Location\",\"StarPos\":[1,2,3]}\n"
                    + GreenGasGiantScanJson + "\n");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "data"),
                Path.Combine(root, "cache"),
                []);
            new NetworkPrivacySettingsStore(paths.UiSettingsPath).Save(
                new NetworkPrivacyPreferences(true, "dev", true));
            var client = new RecordingGreenGasGiantClient();
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths,
                greenGasGiantPublicationCoordinator:
                    new GreenGasGiantPublicationCoordinator(
                        GreenGasGiantCriteriaCatalog.LoadEmbedded(),
                        client));

            await viewModel.RefreshAsync();

            Assert.Empty(client.Candidates);

            await File.AppendAllTextAsync(
                journalPath,
                GreenGasGiantScanJson + "\n");
            await viewModel.RefreshAsync();

            var candidate = Assert.Single(client.Candidates);
            Assert.Equal("Test Cmdr", candidate.CommanderName);
            Assert.Equal("potential", candidate.Tag);
            Assert.Contains(
                "Uploaded a potential Green Gas Giant candidate",
                viewModel.NetworkPrivacy.StatusMessage);
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
    public void GuardianOverlayPreferencesAreWiredIntoMainViewModel()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-main-guardian-settings-{Guid.NewGuid():N}");
        try
        {
            var settingsStore = new GuardianOverlaySettingsStore(
                Path.Combine(root, "ui-settings.json"));
            settingsStore.Save(new GuardianOverlayPreferences(
                EnableGuardianSites: false,
                AutoShowGuardianSummary: false,
                AutoShowRamTah: true,
                SuppressForActiveBuildProjects: true,
                AutoZoomNearObelisks: false,
                AutoZoomInSrvTurret: true,
                ShowComponentMaterials: true,
                OverlaySizeIndex: 3,
                DisableRuinsMeasurementGrid: true,
                DisableAerialAlignmentGrid: false));

            var viewModel = new MainWindowViewModel(
                Path.Combine(root, "missing-journals"),
                appDataPaths: new AppDataPaths(
                    Path.Combine(root, "config"),
                    Path.Combine(root, "data"),
                    Path.Combine(root, "cache"),
                    []),
                guardianOverlaySettingsStore: settingsStore);

            Assert.False(viewModel.Guardian.EnableGuardianSites);
            Assert.False(viewModel.Guardian.AutoShowGuardianSummary);
            Assert.True(viewModel.Guardian.AutoShowRamTah);
            Assert.True(viewModel.Guardian.SuppressForActiveBuildProjects);
            Assert.False(viewModel.Guardian.AutoZoomNearObelisks);
            Assert.True(viewModel.Guardian.AutoZoomInSrvTurret);
            Assert.True(viewModel.Guardian.ShowComponentMaterials);
            Assert.False(viewModel.Guardian.ShowRuinsMeasurementGrid);
            Assert.True(viewModel.Guardian.ShowAerialAlignmentGrid);
            Assert.Equal(800, viewModel.Guardian.PreferredOverlayWidth);
            Assert.Equal(1_000, viewModel.Guardian.PreferredOverlayHeight);
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
    public void StationInfoPreferencesAreWiredIntoMainViewModel()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-main-station-settings-{Guid.NewGuid():N}");
        try
        {
            var settingsStore = new StationInfoSettingsStore(
                Path.Combine(root, "ui-settings.json"));
            settingsStore.Save(new StationInfoPreferences(AutoShow: false));

            var viewModel = new MainWindowViewModel(
                Path.Combine(root, "missing-journals"),
                appDataPaths: new AppDataPaths(
                    Path.Combine(root, "config"),
                    Path.Combine(root, "data"),
                    Path.Combine(root, "cache"),
                    []),
                stationInfoSettingsStore: settingsStore);

            Assert.False(viewModel.StationInfo.AutoShow);
            viewModel.StationInfo.Dispose();
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
    public void HumanSitePreferencesAreWiredIntoMainViewModel()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-main-human-site-settings-{Guid.NewGuid():N}");
        try
        {
            var settingsStore = new HumanSiteSettingsStore(
                Path.Combine(root, "ui-settings.json"));
            settingsStore.Save(HumanSitePreferences.Default with
            {
                AutoShow = false,
                FootZoom = 3,
                ShowMedkits = false,
            });

            var viewModel = new MainWindowViewModel(
                Path.Combine(root, "missing-journals"),
                appDataPaths: new AppDataPaths(
                    Path.Combine(root, "config"),
                    Path.Combine(root, "data"),
                    Path.Combine(root, "cache"),
                    []),
                humanSiteSettingsStore: settingsStore);

            Assert.False(viewModel.HumanSite.AutoShow);
            Assert.Equal(3, viewModel.HumanSite.FootZoom);
            Assert.False(viewModel.HumanSite.ShowMedkits);
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
                "{\"unknownFutureField\":42,\"darkTheme\":true,"
                    + "\"autoShowPlotJumpInfo\":false}");
            Directory.CreateDirectory(Path.Combine(data, "logs"));
            await File.WriteAllTextAsync(
                Path.Combine(data, "logs", "startup.txt"),
                "startup log");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                data,
                Path.Combine(root, "cache"),
                [new LegacyProfileCandidate(LegacyProfileLocationKind.Desktop, source)]);
            var viewModel = new MainWindowViewModel(
                Path.Combine(root, "missing-journals"),
                appDataPaths: paths);

            Assert.Equal(source, viewModel.LegacyProfileSourcePath);
            await viewModel.ImportLegacyProfileAsync();

            Assert.True(File.Exists(Path.Combine(data, "settings.json")));
            Assert.True(File.Exists(Path.Combine(data, "logs", "startup.txt")));
            Assert.Contains("Imported 1 legacy files", viewModel.ProfileStatusMessage);
            Assert.Contains("retained 1 current-only files", viewModel.ProfileStatusMessage);
            Assert.Contains("Translated 2 legacy UI preferences", viewModel.ProfileStatusMessage);
            Assert.Contains("Restart SrvSurvey", viewModel.ProfileStatusMessage);
            Assert.Equal(
                "blue-dark",
                new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
            Assert.False(
                new JumpInfoSettingsStore(paths.UiSettingsPath).Load().AutoShow);
            Assert.True(viewModel.HasCompletedLegacyImport);
            Assert.False(viewModel.ImportLegacyProfileCommand.CanExecute(null));
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
    public async Task LegacyProfileImportPreservesCurrentUiSettingsWhenLegacySettingsAreMalformed()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-profile-malformed-settings-tests-{Guid.NewGuid():N}");
        try
        {
            var source = Path.Combine(root, "legacy");
            var data = Path.Combine(root, "current");
            var config = Path.Combine(root, "config");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(config);
            await File.WriteAllTextAsync(
                Path.Combine(source, "settings.json"),
                "{\"darkTheme\":true,");
            var paths = new AppDataPaths(
                config,
                data,
                Path.Combine(root, "cache"),
                [new LegacyProfileCandidate(
                    LegacyProfileLocationKind.Desktop,
                    source)]);
            const string currentSettings =
                "{\"Version\":1,\"Theme\":\"green-light\"}";
            await File.WriteAllTextAsync(paths.UiSettingsPath, currentSettings);
            var viewModel = new MainWindowViewModel(
                Path.Combine(root, "missing-journals"),
                appDataPaths: paths);

            await viewModel.ImportLegacyProfileAsync();

            Assert.True(viewModel.HasCompletedLegacyImport);
            Assert.Contains(
                "legacy UI preferences could not be translated",
                viewModel.ProfileStatusMessage);
            Assert.Equal(
                currentSettings,
                await File.ReadAllTextAsync(paths.UiSettingsPath));
            Assert.Equal(
                "{\"darkTheme\":true,",
                await File.ReadAllTextAsync(
                    Path.Combine(paths.DataDirectory, "settings.json")));
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
    public async Task LegacyProfileImportWaitsForJournalMonitorShutdown()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-profile-monitor-tests-{Guid.NewGuid():N}");
        try
        {
            var source = Path.Combine(root, "legacy");
            var data = Path.Combine(root, "current");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(
                Path.Combine(source, "settings.json"),
                "before monitor shutdown");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                data,
                Path.Combine(root, "cache"),
                [new LegacyProfileCandidate(LegacyProfileLocationKind.Desktop, source)]);
            var viewModel = new MainWindowViewModel(
                Path.Combine(root, "missing-journals"),
                appDataPaths: paths);
            var monitorStopped = false;
            viewModel.ProfileImportPreparing += async () =>
            {
                await Task.Yield();
                monitorStopped = true;
                await File.WriteAllTextAsync(
                    Path.Combine(source, "settings.json"),
                    "after monitor shutdown");
            };

            await viewModel.ImportLegacyProfileAsync();

            Assert.True(monitorStopped);
            Assert.Equal(
                "after monitor shutdown",
                await File.ReadAllTextAsync(
                    Path.Combine(data, "settings.json")));
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
    public async Task LegacyProfileCanBeImportedFromManuallySelectedFolder()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-manual-profile-vm-tests-{Guid.NewGuid():N}");
        try
        {
            var source = Path.Combine(root, "copied-windows-profile");
            var data = Path.Combine(root, "current");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(
                Path.Combine(source, "F123-live.json"),
                "{\"fid\":\"F123\",\"commander\":\"Drew\"}");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                data,
                Path.Combine(root, "cache"),
                []);
            var viewModel = new MainWindowViewModel(
                Path.Combine(root, "missing-journals"),
                appDataPaths: paths);

            Assert.Empty(viewModel.LegacyProfiles);
            Assert.False(viewModel.ImportLegacyProfileCommand.CanExecute(null));

            viewModel.LegacyProfileSourcePath = source;

            Assert.True(viewModel.ImportLegacyProfileCommand.CanExecute(null));
            await viewModel.ImportLegacyProfileAsync();

            Assert.Equal(
                "{\"fid\":\"F123\",\"commander\":\"Drew\"}",
                await File.ReadAllTextAsync(
                    Path.Combine(data, "F123-live.json")));
            Assert.True(viewModel.HasCompletedLegacyImport);
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
            Assert.Equal("Sol", viewModel.NearestSystems.ReferenceSystemName);
            Assert.Equal("[ 0, 0, 0 ]", viewModel.NearestSystems.ReferencePosition);
            Assert.Equal("Searching from Sol", viewModel.NearestSystems.ReferenceSummary);
            Assert.True(viewModel.SystemNotes.HasCurrentSystem);
            Assert.Equal("Sol", viewModel.SystemNotes.SystemName);
            Assert.Equal("10477373803", viewModel.SystemNotes.SystemAddress);
            Assert.True(viewModel.Route.HasProfile);
            Assert.Equal("Sol", viewModel.Route.CurrentSystem);
            Assert.Equal("Sol", viewModel.SystemSurvey.Snapshot.SystemName);
            Assert.Equal(
                10477373803,
                viewModel.SystemSurvey.Snapshot.SystemAddress);
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
    public async Task RefreshFeedsHumanSettlementJournalAndStatusPipeline()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-human-site-main-tests-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Journal.2026-07-25T030000.01.log"),
                "{\"timestamp\":\"2026-07-25T03:00:00Z\",\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + "{\"timestamp\":\"2026-07-25T03:00:01Z\",\"event\":\"Location\",\"StarSystem\":\"Test System\",\"SystemAddress\":42,\"StarPos\":[1,2,3],\"Body\":\"Test System 1\",\"BodyType\":\"Planet\"}\n"
                    + "{\"timestamp\":\"2026-07-25T03:00:02Z\",\"event\":\"Loadout\",\"Ship\":\"sidewinder\"}\n"
                    + "{\"timestamp\":\"2026-07-25T03:00:03Z\",\"event\":\"ApproachSettlement\",\"Name\":\"Haberlandt Survey\",\"Name_Localised\":\"Haberlandt Survey\",\"MarketID\":12345,\"SystemAddress\":42,\"BodyID\":3,\"BodyName\":\"Test System 1\",\"Latitude\":0,\"Longitude\":0,\"StationEconomy\":\"$economy_Agri;\",\"StationEconomy_Localised\":\"Agriculture\",\"StationFaction\":{\"Name\":\"Raven Colonial\",\"FactionState\":\"Boom\"},\"StationGovernment\":\"$government_Democracy;\",\"StationGovernment_Localised\":\"Democracy\",\"StationServices\":[\"dock\",\"refuel\"]}\n");
            await File.WriteAllTextAsync(
                Path.Combine(root, StatusFileReader.FileName),
                "{\"timestamp\":\"2026-07-25T03:00:04Z\",\"event\":\"Status\",\"Flags\":2097152,\"Flags2\":32785,\"Latitude\":0,\"Longitude\":0,\"Heading\":0,\"Altitude\":10,\"PlanetRadius\":6000000}");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "data"),
                Path.Combine(root, "cache"),
                []);
            var viewModel = new MainWindowViewModel(root, appDataPaths: paths);

            await viewModel.RefreshAsync();

            Assert.NotNull(viewModel.HumanSite.ActiveSite);
            Assert.Equal("Haberlandt Survey", viewModel.HumanSite.SiteName);
            Assert.True(viewModel.HumanSite.ShouldShow);
            Assert.True(File.Exists(Path.Combine(
                paths.DataDirectory,
                "systems",
                "F123",
                "Test System_42.json")));
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
    public async Task RefreshReplaysCommanderCodexFirstsToLegacyLedgers()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-codex-vm-tests-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            var profile = Path.Combine(root, "profile");
            Directory.CreateDirectory(journals);
            await File.WriteAllTextAsync(
                Path.Combine(journals, "Journal.2026-07-24T100000.01.log"),
                "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + "{\"timestamp\":\"2026-07-24T10:00:01Z\",\"event\":\"Location\",\"StarSystem\":\"Sol\",\"SystemAddress\":10477373803,\"StarPos\":[0,0,0]}\n"
                    + "{\"timestamp\":\"2026-07-24T10:00:02Z\",\"event\":\"CodexEntry\",\"EntryID\":2310101,\"SystemAddress\":10477373803,\"BodyID\":3}\n");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                profile,
                Path.Combine(root, "cache"),
                []);
            var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths);

            await viewModel.RefreshAsync();

            Assert.Contains("2 Commander Codex ledger entries", viewModel.CommanderCodexStatusMessage);
            Assert.Contains("2 files", viewModel.CommanderCodexStatusMessage);
            var store = new CommanderCodexStore(profile);
            var global = await store.LoadAsync("F123", "Drew");
            var first = Assert.Single(global.Data!.Firsts).Value;
            Assert.Equal(10477373803, first.SystemAddress);
            Assert.Equal(3, first.BodyId);
            Assert.Single(Directory.GetFiles(profile, "F123-codex-*.json"));
            Assert.Equal("F123", viewModel.CodexBingo.SelectedCommander?.FrontierId);
            Assert.Equal(1, viewModel.CodexBingo.DiscoveredCount);
            viewModel.CodexBingo.Dispose();
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
    public async Task RefreshConnectsFollowedRouteAndLiveFsdProgress()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-live-route-vm-tests-{Guid.NewGuid():N}");
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
                    + "{\"event\":\"Location\",\"StarSystem\":\"Sol\","
                    + "\"SystemAddress\":1,\"StarPos\":[0,0,0]}\n");
            var store = new FollowRouteStore(profile);
            await store.SaveAsync(new FollowRouteDocument(
                "F123",
                store.GetPath("F123"),
                true,
                true,
                0,
                [
                    new FollowRouteHop(
                        "Sol",
                        1,
                        new GalacticCoordinate(0, 0, 0),
                        null,
                        false,
                        false),
                    new FollowRouteHop(
                        "Second",
                        2,
                        new GalacticCoordinate(3, 4, 0),
                        null,
                        false,
                        false),
                ]));
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                profile,
                Path.Combine(root, "cache"),
                []);
            var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths);

            await viewModel.RefreshAsync();

            Assert.Equal("Second", viewModel.Route.NextHopName);
            Assert.Equal(1, viewModel.Route.ReachedCount);

            await File.AppendAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-24T12:00:00Z\","
                    + "\"event\":\"FSDJump\",\"StarSystem\":\"Second\","
                    + "\"SystemAddress\":2,\"StarPos\":[3,4,0]}\n");
            await viewModel.RefreshAsync();

            Assert.True(viewModel.Route.IsComplete);
            Assert.False(viewModel.Route.IsActive);
            Assert.Equal(2, viewModel.Route.ReachedCount);
            Assert.True((await store.LoadAsync("F123")).Route!.IsComplete);
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

    [Fact]
    public async Task LiveOrganicSamplesPopulateGroundedSurfaceHistory()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-surface-history-vm-tests-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            var profile = Path.Combine(root, "profile");
            Directory.CreateDirectory(journals);
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
                    + "{\"event\":\"Location\",\"StarSystem\":\"Test System\",\"SystemAddress\":42}\n"
                    + "{\"event\":\"Scan\",\"ScanType\":\"Detailed\",\"SystemAddress\":42,\"BodyName\":\"Test System 1\",\"BodyID\":7,\"PlanetClass\":\"Rocky body\",\"Landable\":true,\"Radius\":1000}\n"
                    + $"{{\"event\":\"ScanOrganic\",\"ScanType\":\"Log\",\"Genus\":\"{genus}\",\"Species\":\"{species}\",\"Variant\":\"{variant}\",\"SystemAddress\":42,\"Body\":7}}\n");
            var statusPath = Path.Combine(journals, "Status.json");
            await WriteSurfaceStatusAsync(statusPath, 1, 2);
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                profile,
                Path.Combine(root, "cache"),
                []);
            var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths);

            await viewModel.RefreshAsync();
            await WriteSurfaceStatusAsync(statusPath, 2, 3);
            await File.AppendAllTextAsync(
                journalPath,
                $"{{\"event\":\"ScanOrganic\",\"ScanType\":\"Sample\",\"Genus\":\"{genus}\",\"Species\":\"{species}\",\"Variant\":\"{variant}\",\"SystemAddress\":42,\"Body\":7}}\n");
            await viewModel.RefreshAsync();
            await WriteSurfaceStatusAsync(statusPath, 3, 4);
            await File.AppendAllTextAsync(
                journalPath,
                $"{{\"event\":\"ScanOrganic\",\"ScanType\":\"Analyse\",\"Genus\":\"{genus}\",\"Species\":\"{species}\",\"Variant\":\"{variant}\",\"SystemAddress\":42,\"Body\":7}}\n");
            await viewModel.RefreshAsync();

            Assert.Equal(3, viewModel.SurfaceSurvey.CurrentSurface!.BioScans.Count);
            Assert.True(viewModel.SurfaceSurvey.ShouldShow);
            Assert.Equal(
                [
                    new SurfaceCoordinate(3, 4),
                    new SurfaceCoordinate(1, 2),
                    new SurfaceCoordinate(2, 3),
                ],
                viewModel.SurfaceSurvey.CurrentSurface.BioScans
                    .Select(scan => scan.Location));

            await File.AppendAllTextAsync(
                journalPath,
                "{\"event\":\"Died\"}\n");
            await viewModel.RefreshAsync();

            Assert.All(
                viewModel.SurfaceSurvey.CurrentSurface!.BioScans,
                scan => Assert.Equal("Died", scan.Status));
            var savedProfile = await new CommanderProfileStore(profile)
                .LoadAsync("F123", true);
            Assert.Empty(savedProfile.Data!.Exobiology.ScannedBioEntryIds);
            viewModel.SurfaceSurvey.Dispose();
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
    public async Task ScreenshotsProcessOnlyAfterBootstrapReplay()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-screenshot-monitor-tests-{Guid.NewGuid():N}");
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
                    + "{\"event\":\"Screenshot\",\"Filename\":\"\\\\ED_Pictures\\\\old.bmp\"}\n");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                profile,
                Path.Combine(root, "cache"),
                []);
            var processor = new CountingScreenshotProcessor();
            var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths,
                screenshotProcessingService: processor);

            await viewModel.RefreshAsync();

            Assert.Equal(0, processor.CallCount);
            await File.AppendAllTextAsync(
                journalPath,
                "{\"event\":\"Screenshot\",\"Filename\":\"\\\\ED_Pictures\\\\new.bmp\"}\n");
            await viewModel.RefreshAsync();

            Assert.Equal(1, processor.CallCount);
            Assert.Equal("Drew", processor.CommanderName);
            Assert.Equal(
                "new.bmp",
                Path.GetFileName(
                    Assert.Single(processor.Events).Payload
                        .GetProperty("Filename")
                        .GetString()!
                        .Replace('\\', '/')));
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
    public async Task EnabledMigratedQuestRunsOnlyForLiveDesktopJournalEvents()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-main-quest-tests-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            var profile = Path.Combine(root, "profile");
            var config = Path.Combine(root, "config");
            var questDirectory = Path.Combine(profile, "quests");
            Directory.CreateDirectory(journals);
            Directory.CreateDirectory(questDirectory);
            var journalPath = Path.Combine(
                journals,
                "Journal.2026-07-24T100000.01.log");
            await File.WriteAllTextAsync(
                journalPath,
                "{\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                    + "{\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + "{\"event\":\"Scan\",\"BodyName\":\"Historical body\"}\n");
            var statePath = Path.Combine(questDirectory, "F123.json");
            await File.WriteAllTextAsync(
                statePath,
                """
                {
                  "fid":"F123",
                  "cmdr":"Drew",
                  "devRef":"Raven|desktop|1",
                  "devQuest":{
                    "startTime":"2026-07-01T00:00:00Z",
                    "chapters":[{"id":"start","startTime":"2026-07-01T00:00:00Z"}],
                    "vars":{},
                    "future":"preserve"
                  },
                  "futureRoot":true
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(questDirectory, "dev-desktop.json"),
                """
                {
                  "publisher":"Raven",
                  "id":"desktop",
                  "ver":1,
                  "title":"Desktop Integration Quest",
                  "firstChapter":"start",
                  "objectives":{},
                  "strings":{},
                  "msgs":[],
                  "chapters":{
                    "start":"function on_Scan(entry) quest:set('body', entry.BodyName); return true end"
                  }
                }
                """);
            var paths = new AppDataPaths(
                config,
                profile,
                Path.Combine(root, "cache"),
                []);
            new QuestSettingsStore(paths.UiSettingsPath).SaveEnabled(true);
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths);

            await viewModel.RefreshAsync();

            var quest = Assert.Single(viewModel.Quests);
            Assert.True(quest.IsDevelopment);
            Assert.Equal("Desktop Integration Quest", quest.Title);
            Assert.Single(viewModel.QuestWorkspace.ActiveQuests);
            Assert.True(viewModel.QuestIndicator.ShouldShow);
            viewModel.ShowQuests();
            Assert.True(viewModel.IsQuestsSelected);
            Assert.DoesNotContain(
                "Historical body",
                await File.ReadAllTextAsync(statePath),
                StringComparison.Ordinal);

            await File.AppendAllTextAsync(
                journalPath,
                "{\"event\":\"Scan\",\"BodyName\":\"Live body\"}\n");
            await viewModel.RefreshAsync();

            Assert.Contains(
                "Live body",
                await File.ReadAllTextAsync(statePath),
                StringComparison.Ordinal);
            Assert.Contains(
                "futureRoot",
                await File.ReadAllTextAsync(statePath),
                StringComparison.Ordinal);
            Assert.True(JournalEventEnvelope.TryParse(
                "{\"event\":\"Scan\",\"BodyName\":\"Replay body\"}",
                out var replayEvent,
                out var replayError), replayError);
            viewModel.JournalInspector.ApplyUpdate([replayEvent!], null);
            viewModel.JournalInspector.SelectedEvent =
                viewModel.JournalInspector.Events[0];
            viewModel.JournalInspector.ReplayConfirmed = true;

            await viewModel.JournalInspector.ReplayAsync();

            Assert.Contains(
                "Replay body",
                await File.ReadAllTextAsync(statePath),
                StringComparison.Ordinal);
            Assert.Contains(
                "Replayed Scan",
                viewModel.JournalInspector.StatusMessage);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(
                questDirectory,
                "quest-state-backups")).Length);
            Assert.Contains("1 active quest", viewModel.QuestStatusMessage);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static Task WriteSurfaceStatusAsync(
        string path,
        double latitude,
        double longitude)
    {
        return File.WriteAllTextAsync(
            path,
            $$"""
            {"event":"Status","Flags":69206016,"Flags2":0,"Latitude":{{latitude}},"Longitude":{{longitude}},"Heading":90,"Altitude":10,"BodyName":"Test System 1","PlanetRadius":1000}
            """);
    }

    private const string GreenGasGiantScanJson =
        "{\"event\":\"Scan\","
        + "\"PlanetClass\":\"Sudarsky class III gas giant\","
        + "\"SurfaceTemperature\":310}";

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

    private sealed class RecordingGreenGasGiantClient
        : IGreenGasGiantClient
    {
        public List<GreenGasGiantCandidate> Candidates { get; } = [];

        public Task PublishAsync(
            GreenGasGiantCandidate candidate,
            CancellationToken cancellationToken = default)
        {
            Candidates.Add(candidate);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingScreenshotProcessor
        : IScreenshotProcessingService
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<JournalEventEnvelope> Events { get; private set; } = [];

        public string? CommanderName { get; private set; }

        public Task<ScreenshotProcessingResult> ProcessAsync(
            IReadOnlyList<JournalEventEnvelope> journalEvents,
            ScreenshotProcessingPreferences preferences,
            string? commanderName,
            CancellationToken cancellationToken = default,
            ScreenshotGuardianContext? guardianContext = null)
        {
            CallCount++;
            Events = journalEvents;
            CommanderName = commanderName;
            return Task.FromResult(ScreenshotProcessingResult.Empty);
        }
    }
}
