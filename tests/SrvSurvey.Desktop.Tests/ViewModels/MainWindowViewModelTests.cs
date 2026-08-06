using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Inara;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Storage;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Settlements;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;
using System.Globalization;
using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void NavigationContainsEveryImplementedSurface()
    {
        var viewModel = new MainWindowViewModel(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));

        Assert.Equal(11, viewModel.NavigationItems.Count);
        Assert.Equal(
            [
                "Overview",
                "Exploration",
                "Exobiology",
                "Travel",
                "Search",
                "Guardian",
                "Quests",
                "Colonisation",
                "Diagnostics",
                "Settings",
                "Guides",
            ],
            viewModel.NavigationItems.Select(item => item.Label));
        Assert.DoesNotContain(
            typeof(NavigationItemViewModel).GetProperties(),
            property => property.Name == "Glyph");
        Assert.True(viewModel.IsOverviewSelected);

        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(
            item => item.Key == "exobiology");

        Assert.True(viewModel.IsExobiologySelected);

        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(
            item => item.Key == "travel");

        Assert.True(viewModel.IsTravelSelected);

        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(
            item => item.Key == "search");

        Assert.True(viewModel.IsSearchSelected);

        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(
            item => item.Key == "guardian");

        Assert.True(viewModel.IsGuardianSelected);

        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(
            item => item.Key == "quests");

        Assert.True(viewModel.IsQuestsSelected);

        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(
            item => item.Key == "colonisation");

        Assert.True(viewModel.IsColonizationSelected);

        viewModel.SelectedNavigation = viewModel.NavigationItems.Single(
            item => item.Key == "guides");

        Assert.True(viewModel.IsGuidesSelected);
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
    public void SettingsLinkResultReportsSuccessAndFailureWithoutChangingData()
    {
        var viewModel = new MainWindowViewModel(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));

        viewModel.ReportSettingsLinkResult("the guide", true);
        Assert.Equal(
            "Opened the guide in the default browser.",
            viewModel.SettingsLinkStatusMessage);

        viewModel.ReportSettingsLinkResult(
            "the guide",
            false,
            "No launcher");
        Assert.Equal(
            "Could not open the guide: No launcher",
            viewModel.SettingsLinkStatusMessage);
    }

    [Fact]
    public void ImportedReadOnlyReferenceCachesActivateWithoutBeingRewritten()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-regional-codex-vm-{Guid.NewGuid():N}");
        try
        {
            var data = Path.Combine(root, "data");
            Directory.CreateDirectory(data);
            var catalogPath = Path.Combine(
                data,
                RegionalCodexCandidateCatalog.LegacyFileName);
            const string json =
                "{\"Inner Orion Spur\":[\"2310101_Aleoida Arcus - Green\"]}";
            File.WriteAllText(catalogPath, json);
            var published = Path.Combine(data, "pub");
            Directory.CreateDirectory(published);
            var knownSystemsPath = Path.Combine(
                published,
                KnownSystemAddressCatalog.LegacyFileName);
            const string knownSystems =
                "known_systems = {\n  \"sol\": 10477373803,\n}\n"
                + "known_missing = [\n]\n";
            File.WriteAllText(knownSystemsPath, knownSystems);
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                data,
                Path.Combine(root, "cache"),
                []);

            using var viewModel = new MainWindowViewModel(
                Path.Combine(root, "journals"),
                appDataPaths: paths);

            Assert.Contains(
                "Imported regional Codex candidates: 1.",
                viewModel.ReferenceDataStatus);
            Assert.Contains(
                "Imported known system addresses: 1.",
                viewModel.ReferenceDataStatus);
            Assert.Equal(json, File.ReadAllText(catalogPath));
            Assert.Equal(knownSystems, File.ReadAllText(knownSystemsPath));
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
                new NetworkPrivacyPreferences(false, true, true));
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
    public async Task EddnReceivesContextButPublishesOnlyNewLiveEvents()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-main-eddn-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            Directory.CreateDirectory(journals);
            var journalPath = Path.Combine(
                journals,
                "Journal.2026-07-25T120000.01.log");
            await File.WriteAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-25T12:00:00Z\",\"event\":\"Fileheader\",\"gameversion\":\"4.1\",\"build\":\"r1\"}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:01Z\",\"event\":\"LoadGame\",\"Commander\":\"Test Cmdr\",\"Horizons\":true,\"Odyssey\":true}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:02Z\",\"event\":\"Location\",\"StarSystem\":\"Test A\",\"SystemAddress\":123,\"StarPos\":[1,2,3]}\n");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "data"),
                Path.Combine(root, "cache"),
                []);
            new NetworkPrivacySettingsStore(paths.UiSettingsPath).Save(
                new NetworkPrivacyPreferences(true, true, false));
            var publisher = new RecordingEddnPublisher();
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths,
                eddnPublisher: publisher);

            await viewModel.RefreshAsync();

            var bootstrap = Assert.Single(publisher.Calls);
            Assert.False(bootstrap.AllowPublishing);
            Assert.True(bootstrap.Enabled);
            Assert.True(bootstrap.UseTestSchemas);
            Assert.Equal(3, bootstrap.Events.Count);
            Assert.DoesNotContain("Queued", viewModel.NetworkPrivacy.StatusMessage);

            await File.AppendAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-25T12:01:00Z\",\"event\":\"DockingGranted\",\"MarketID\":1,\"StationName\":\"Port\",\"LandingPad\":2}\n");
            await viewModel.RefreshAsync();

            Assert.Equal(2, publisher.Calls.Count);
            var live = publisher.Calls[1];
            Assert.True(live.AllowPublishing);
            Assert.Equal("DockingGranted", Assert.Single(live.Events).EventName);
            Assert.Contains(
                "Queued DockingGranted for EDDN (test schemas)",
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
    public async Task InaraReceivesCommanderProfileAndMultiboxSafetyContext()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-main-inara-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            Directory.CreateDirectory(journals);
            var journalPath = Path.Combine(
                journals,
                "Journal.2026-07-25T120000.01.log");
            await File.WriteAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-25T12:00:00Z\",\"event\":\"Fileheader\",\"gameversion\":\"4.1\",\"build\":\"r1\",\"Odyssey\":true}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:01Z\",\"event\":\"LoadGame\",\"Commander\":\"Test Cmdr\",\"FID\":\"F123\",\"Odyssey\":true,\"Ship\":\"mandalay\",\"ShipID\":42}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:02Z\",\"event\":\"Location\",\"StarSystem\":\"Test A\",\"SystemAddress\":123,\"StarPos\":[1,2,3]}\n");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "data"),
                Path.Combine(root, "cache"),
                []);
            new InaraSettingsStore(paths.UiSettingsPath).Save(
                new InaraPreferences(
                    UploadEnabled: true,
                    DeveloperTestMode: true));
            await new CommanderProfileStore(paths.DataDirectory)
                .SaveInaraApiKeyAsync(
                    "F123",
                    "Test Cmdr",
                    isOdyssey: true,
                    "personal-key");
            var publisher = new RecordingInaraPublisher();
            var gameWindows = new MutableGameWindowSwitcher
            {
                AvailableWindowCount = 2,
            };
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths,
                gameWindowSwitcher: gameWindows,
                inaraPublisher: publisher);

            await viewModel.RefreshAsync();

            var bootstrap = Assert.Single(publisher.Calls);
            Assert.False(bootstrap.AllowPublishing);
            Assert.False(bootstrap.AllowSharedData);
            Assert.True(bootstrap.Options.Enabled);
            Assert.True(bootstrap.Options.DeveloperTestMode);
            Assert.Equal("personal-key", bootstrap.Options.ApiKey);
            Assert.Equal("Test Cmdr", bootstrap.Options.CommanderName);
            Assert.Equal("F123", bootstrap.Options.FrontierId);
            Assert.Equal("mandalay", bootstrap.ShipType);
            Assert.Equal(42, bootstrap.ShipId);

            await File.AppendAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-25T12:01:00Z\",\"event\":\"Docked\",\"StarSystem\":\"Test A\",\"SystemAddress\":123,\"StationName\":\"Test Port\"}\n");
            await viewModel.RefreshAsync();

            Assert.Equal(2, publisher.Calls.Count);
            var live = publisher.Calls[1];
            Assert.True(live.AllowPublishing);
            Assert.False(live.AllowSharedData);
            Assert.Equal("Test Port", live.StationName);
            Assert.Contains(
                "Inara accepted",
                viewModel.Inara.PublicationStatus);
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
    public async Task InaraFailureDoesNotInterruptExistingJournalTracking()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-main-inara-isolation-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            Directory.CreateDirectory(journals);
            await File.WriteAllTextAsync(
                Path.Combine(
                    journals,
                    "Journal.2026-07-25T120000.01.log"),
                "{\"event\":\"Commander\",\"Name\":\"Test Cmdr\",\"FID\":\"F123\"}\n"
                    + "{\"event\":\"Location\",\"StarSystem\":\"Test A\",\"SystemAddress\":123,\"StarPos\":[1,2,3]}\n");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "data"),
                Path.Combine(root, "cache"),
                []);
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths,
                inaraPublisher: new ThrowingInaraPublisher());

            await viewModel.RefreshAsync();

            Assert.Equal("Test Cmdr", viewModel.CommanderName);
            Assert.Contains("Test A", viewModel.SystemDescription);
            Assert.Contains(
                "without affecting journal tracking",
                viewModel.Inara.PublicationStatus);
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
    public void DisablingInaraImmediatelyCancelsPendingPublication()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-main-inara-opt-out-{Guid.NewGuid():N}");
        try
        {
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "data"),
                Path.Combine(root, "cache"),
                []);
            var publisher = new RecordingInaraPublisher();
            using var viewModel = new MainWindowViewModel(
                configuredJournalDirectory: null,
                appDataPaths: paths,
                inaraPublisher: publisher);

            viewModel.Inara.UploadEnabled = true;
            Assert.Equal(0, publisher.CancellationCount);
            viewModel.Inara.UploadEnabled = false;

            Assert.Equal(1, publisher.CancellationCount);
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
                DisableAerialAlignmentGrid: false,
                ShowMapNotes: false,
                ShowMapLegend: false));

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
            Assert.False(viewModel.Guardian.ShowMapNotes);
            Assert.False(viewModel.Guardian.ShowMapLegend);
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
    public async Task LegacyProfileImportConvertsRetiredOrganicClaimsAfterVerification()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-profile-organic-vm-tests-{Guid.NewGuid():N}");
        try
        {
            var source = Path.Combine(root, "legacy");
            var data = Path.Combine(root, "current");
            Directory.CreateDirectory(source);
            var reference = ExobiologyReferenceCatalog.LoadEmbedded()
                .BiologyEntries.First(entry => string.Equals(
                    entry.VariantName,
                    "$Codex_Ent_Aleoids_01_B_Name;",
                    StringComparison.Ordinal));
            var sourceProfilePath = Path.Combine(source, "F123-live.json");
            await File.WriteAllTextAsync(
                sourceProfilePath,
                $$"""
                {
                  "fid": "F123",
                  "futureProfile": true,
                  "organicRewards": 1,
                  "scannedBioEntryIds": ["42_1_{{reference.EntryId}}"]
                }
                """);
            var sourceBytes = await File.ReadAllBytesAsync(sourceProfilePath);
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                data,
                Path.Combine(root, "cache"),
                [new LegacyProfileCandidate(
                    LegacyProfileLocationKind.Desktop,
                    source)]);
            var viewModel = new MainWindowViewModel(
                Path.Combine(root, "missing-journals"),
                appDataPaths: paths);

            await viewModel.ImportLegacyProfileAsync();

            Assert.Contains(
                "Converted retired organic history",
                viewModel.ProfileStatusMessage);
            Assert.Equal(
                sourceBytes,
                await File.ReadAllBytesAsync(sourceProfilePath));
            var profile = JsonNode.Parse(await File.ReadAllTextAsync(
                Path.Combine(data, "F123-live.json")))!.AsObject();
            Assert.True(profile["futureProfile"]!.GetValue<bool>());
            Assert.True(
                profile["migratedScannedOrganicsInEntryId"]!
                    .GetValue<bool>());
            Assert.Equal(
                reference.Reward,
                profile["organicRewards"]!.GetValue<long>());
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
    public async Task VerifiedLegacyProfileImportRequestsImmediateRestart()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-profile-restart-tests-{Guid.NewGuid():N}");
        try
        {
            var source = Path.Combine(root, "legacy");
            var data = Path.Combine(root, "current");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(
                Path.Combine(source, "settings.json"),
                "{\"darkTheme\":true}");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                data,
                Path.Combine(root, "cache"),
                [new LegacyProfileCandidate(
                    LegacyProfileLocationKind.Desktop,
                    source)]);
            var viewModel = new MainWindowViewModel(
                Path.Combine(root, "missing-journals"),
                appDataPaths: paths);
            var restartRequested = false;
            viewModel.ProfileImportCompleted += () =>
            {
                Assert.True(viewModel.HasCompletedLegacyImport);
                Assert.Equal(
                    "{\"darkTheme\":true}",
                    File.ReadAllText(Path.Combine(data, "settings.json")));
                restartRequested = true;
                return Task.CompletedTask;
            };

            await viewModel.ImportLegacyProfileAsync();

            Assert.True(restartRequested);
            Assert.Contains("checksum-verified", viewModel.ProfileStatusMessage);
            Assert.Contains("restarting SrvSurvey", viewModel.ProfileStatusMessage);
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
    public void PersistedJournalFolderIsUsedWhenNoStartupOverrideIsPresent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-persisted-journal-tests-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            var config = Path.Combine(root, "config");
            Directory.CreateDirectory(journals);
            Directory.CreateDirectory(config);
            var paths = new AppDataPaths(
                config,
                Path.Combine(root, "data"),
                Path.Combine(root, "cache"),
                []);
            new JournalSettingsStore(paths.UiSettingsPath).Save(
                new JournalPreferences(journals));

            var viewModel = new MainWindowViewModel(
                configuredJournalDirectory: null,
                appDataPaths: paths);

            Assert.Equal(journals, viewModel.JournalFolderPath);
            Assert.Equal(journals, viewModel.JournalSettings.DirectoryPath);
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
    public async Task RefreshLosslesslyUpdatesImportedSystemHistoryAndRepeatState()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-system-history-vm-tests-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            var profile = Path.Combine(root, "profile");
            var systems = Path.Combine(profile, "systems", "F123");
            Directory.CreateDirectory(journals);
            Directory.CreateDirectory(systems);
            var systemPath = Path.Combine(systems, "Test_42.json");
            await File.WriteAllTextAsync(
                systemPath,
                """
                {
                  "name":"Test",
                  "address":42,
                  "firstVisited":"2026-07-20T00:00:00Z",
                  "lastVisited":"2026-07-20T00:00:00Z",
                  "futureRoot":{"keep":true},
                  "bodies":[{
                    "name":"Test 1",
                    "id":1,
                    "type":"LandableBody",
                    "planetClass":"Rocky body",
                    "surfaceTemperature":180,
                    "materials":{"iron":20},
                    "bioSignalCount":1,
                    "bookmarks":{"Aleoida":[{"latitude":1}]},
                    "organisms":[{"genus":"Aleoida","analyzed":true}]
                  }]
                }
                """);
            var journalPath = Path.Combine(
                journals,
                "Journal.2026-07-24T100000.01.log");
            await File.WriteAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + "{\"timestamp\":\"2026-07-24T10:00:01Z\",\"event\":\"Location\",\"StarSystem\":\"Test\",\"SystemAddress\":42,\"StarPos\":[1,2,3]}\n"
                    + "{\"timestamp\":\"2026-07-24T10:00:02Z\",\"event\":\"FSSBodySignals\",\"SystemAddress\":42,\"BodyName\":\"Test 1\",\"BodyID\":1,\"Signals\":[{\"Type\":\"$SAA_SignalType_Biological;\",\"Count\":1}]}\n");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                profile,
                Path.Combine(root, "cache"),
                []);
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths);

            await viewModel.RefreshAsync();

            Assert.True(
                viewModel.SystemSurvey.AreBiologyOverlaysSuppressedForRepeatVisit);
            var restoredBody = Assert.Single(
                viewModel.SystemSurvey.Snapshot.Bodies);
            Assert.Equal(SystemBodyKind.LandablePlanet, restoredBody.Kind);
            Assert.Equal("Rocky body", restoredBody.PlanetClass);
            Assert.Equal(180, restoredBody.SurfaceTemperature);
            Assert.Equal(20, restoredBody.Materials["iron"]);
            Assert.True(Assert.Single(restoredBody.Organisms).IsAnalyzed);
            var saved = JsonNode.Parse(
                await File.ReadAllTextAsync(systemPath))!.AsObject();
            Assert.True(saved["futureRoot"]!["keep"]!.GetValue<bool>());
            Assert.NotNull(saved["bodies"]![0]!["bookmarks"]);
            Assert.Equal(
                "2026-07-24T10:00:01.0000000+00:00",
                saved["lastVisited"]!.GetValue<string>());

            await File.AppendAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-24T10:05:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"New Test\",\"SystemAddress\":84,\"StarPos\":[4,5,6]}\n");
            await viewModel.RefreshAsync();

            Assert.False(
                viewModel.SystemSurvey.AreBiologyOverlaysSuppressedForRepeatVisit);
            Assert.True(File.Exists(Path.Combine(
                systems,
                "New Test_84.json")));
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
    public async Task RefreshHydratesExternalBodiesWithSeparateBiologyConsent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-system-body-data-vm-tests-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            var profile = Path.Combine(root, "profile");
            Directory.CreateDirectory(journals);
            await File.WriteAllTextAsync(
                Path.Combine(journals, "Journal.2026-07-24T100000.01.log"),
                "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + "{\"timestamp\":\"2026-07-24T10:00:01Z\",\"event\":\"Location\",\"StarSystem\":\"Test\",\"SystemAddress\":42,\"StarPos\":[1,2,3]}\n"
                    + "{\"timestamp\":\"2026-07-24T10:00:02Z\",\"event\":\"Scan\",\"SystemAddress\":42,\"BodyName\":\"Test 1\",\"BodyID\":1,\"PlanetClass\":\"Rocky body\",\"Landable\":true,\"SurfaceGravity\":20}\n");
            var externalState = new SystemScanState();
            externalState.Apply(ParseJournalEvent(
                """{"event":"Location","StarSystem":"Test","SystemAddress":42}"""));
            externalState.Apply(ParseJournalEvent(
                """{"event":"Scan","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"PlanetClass":"Icy body","Landable":true,"SurfaceGravity":9,"SurfaceTemperature":180,"Materials":[{"Name":"iron","Percent":20}]}"""));
            externalState.Apply(ParseJournalEvent(
                """{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""));
            var external = new RecordingSystemBodyDataClient(
                new SystemBodyDataLoadResult(
                    [new SystemBodyDataProviderSnapshot(
                        "Spansh",
                        externalState.CreateSnapshot())],
                    []));
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                profile,
                Path.Combine(root, "cache"),
                []);
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths,
                systemBodyDataClient: external);

            viewModel.SystemSurvey.UseExternalData = false;
            await viewModel.RefreshAsync();
            Assert.Equal(0, external.CallCount);
            Assert.Equal(
                0,
                Assert.Single(viewModel.SystemSurvey.Snapshot.Bodies)
                    .SurfaceTemperature);

            viewModel.SystemSurvey.UseExternalData = true;
            await viewModel.RefreshAsync();
            await viewModel.PendingSystemBodyDataLoad;

            Assert.Equal(1, external.CallCount);
            var body = Assert.Single(viewModel.SystemSurvey.Snapshot.Bodies);
            Assert.Equal("Rocky body", body.PlanetClass);
            Assert.Equal(20, body.SurfaceGravity);
            Assert.Equal(180, body.SurfaceTemperature);
            Assert.Equal(20, body.Materials["iron"]);
            Assert.Equal(2, body.BiologicalSignalCount);
            Assert.Empty(body.Organisms);
            var savedPath = Path.Combine(
                profile,
                "systems",
                "F123",
                "Test_42.json");
            var saved = JsonNode.Parse(
                await File.ReadAllTextAsync(savedPath))!.AsObject();
            Assert.Equal(
                180,
                saved["bodies"]![0]!["surfaceTemperature"]!.GetValue<double>());

            await viewModel.RefreshAsync();
            Assert.Equal(1, external.CallCount);

            viewModel.SystemSurvey.UseExternalBioData = true;
            await viewModel.RefreshAsync();
            await viewModel.PendingSystemBodyDataLoad;

            Assert.Equal(2, external.CallCount);
            Assert.Equal(
                "Aleoida",
                Assert.Single(viewModel.SystemSurvey.Snapshot.Bodies[0].Organisms)
                    .GenusLocalized);
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
    public async Task SystemChangeCancelsStaleExternalBodyRequest()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-system-body-cancel-vm-tests-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            Directory.CreateDirectory(journals);
            var journalPath = Path.Combine(
                journals,
                "Journal.2026-07-24T100000.01.log");
            await File.WriteAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + "{\"timestamp\":\"2026-07-24T10:00:01Z\",\"event\":\"Location\",\"StarSystem\":\"Test\",\"SystemAddress\":42,\"StarPos\":[1,2,3]}\n");
            var client = new BlockingSystemBodyDataClient();
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "profile"),
                Path.Combine(root, "cache"),
                []);
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths,
                systemBodyDataClient: client);

            await viewModel.RefreshAsync();
            Assert.Equal([42], client.RequestedAddresses);

            await File.AppendAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-24T10:05:00Z\",\"event\":\"FSDJump\",\"StarSystem\":\"New Test\",\"SystemAddress\":84,\"StarPos\":[4,5,6]}\n");
            await viewModel.RefreshAsync();

            Assert.Equal([42, 84], client.RequestedAddresses);
            Assert.Contains(42, client.CanceledAddresses);
            Assert.Equal(84, viewModel.SystemSurvey.Snapshot.SystemAddress);
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
    public async Task ClearSurfaceTrackersCommandUpdatesExobiologyStatus()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-clear-surface-trackers-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            var profile = Path.Combine(root, "profile");
            Directory.CreateDirectory(journals);
            Directory.CreateDirectory(profile);
            await File.WriteAllTextAsync(
                Path.Combine(journals, "Journal.2026-07-24T100000.01.log"),
                "{\"timestamp\":\"2026-07-24T10:00:00Z\",\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                    + "{\"timestamp\":\"2026-07-24T10:00:01Z\",\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                profile,
                Path.Combine(root, "cache"),
                []);
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths);
            Assert.NotNull(viewModel.ClearSurfaceTrackersCommand);
            Assert.False(
                viewModel.ClearSurfaceTrackersCommand.CanExecute(null),
                "Command should stay disabled until a commander profile is loaded.");

            await viewModel.RefreshAsync();

            Assert.True(
                viewModel.ClearSurfaceTrackersCommand.CanExecute(null),
                "Command must raise CanExecuteChanged after profile load.");

            await viewModel.ClearSurfaceTrackersAsync();

            Assert.Contains(
                "required",
                viewModel.ExobiologyStatusMessage,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(
                string.IsNullOrWhiteSpace(viewModel.ExobiologyStatusMessage));
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
    public async Task FirstFootfallGlobalActionUpdatesAndPersistsOrganicRewards()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-first-footfall-action-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            var profile = Path.Combine(root, "profile");
            Directory.CreateDirectory(journals);
            const string variant = "$Codex_Ent_Aleoids_01_B_Name;";
            const string species = "$Codex_Ent_Aleoids_01_Name;";
            const string genus = "$Codex_Ent_Aleoids_Genus_Name;";
            await File.WriteAllTextAsync(
                Path.Combine(journals, "Journal.2026-07-25T120000.01.log"),
                "{\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                    + "{\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + "{\"event\":\"Location\",\"StarSystem\":\"Test\",\"SystemAddress\":42,\"Population\":0}\n"
                    + "{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyName\":\"Test 1\",\"BodyID\":7,\"PlanetClass\":\"Rocky body\",\"WasFootfalled\":true}\n"
                    + $"{{\"event\":\"ScanOrganic\",\"ScanType\":\"Log\",\"Genus\":\"{genus}\",\"Species\":\"{species}\",\"Variant\":\"{variant}\",\"SystemAddress\":42,\"Body\":7}}\n"
                    + $"{{\"event\":\"ScanOrganic\",\"ScanType\":\"Sample\",\"Genus\":\"{genus}\",\"Species\":\"{species}\",\"Variant\":\"{variant}\",\"SystemAddress\":42,\"Body\":7}}\n"
                    + $"{{\"event\":\"ScanOrganic\",\"ScanType\":\"Analyse\",\"Genus\":\"{genus}\",\"Species\":\"{species}\",\"Variant\":\"{variant}\",\"SystemAddress\":42,\"Body\":7}}\n");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                profile,
                Path.Combine(root, "cache"),
                []);
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths);
            await viewModel.RefreshAsync();
            Assert.Equal("Not first footfall", viewModel.BioFirstFootfall);
            var originalReward = viewModel.UnclaimedBioRewards;

            Assert.True(await viewModel.ToggleCurrentBodyFirstFootfallAsync());

            Assert.Equal(
                "Confirmed; 5x reward applies",
                viewModel.BioFirstFootfall);
            Assert.NotEqual(originalReward, viewModel.UnclaimedBioRewards);
            var saved = await new CommanderProfileStore(profile)
                .LoadAsync("F123", true);
            Assert.All(
                saved.Data!.Exobiology.ScannedBioEntryIds,
                entry => Assert.EndsWith("_True", entry));

            Assert.True(await viewModel.ToggleCurrentBodyFirstFootfallAsync());
            Assert.Equal("Not first footfall", viewModel.BioFirstFootfall);
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
    public async Task LiveFirstFootfallTextCommandCanTargetAnotherBody()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-first-footfall-text-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            var profile = Path.Combine(root, "profile");
            Directory.CreateDirectory(journals);
            var journalPath = Path.Combine(
                journals,
                "Journal.2026-07-25T120000.01.log");
            await File.WriteAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-25T12:00:00Z\",\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:01Z\",\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:02Z\",\"event\":\"Location\",\"StarSystem\":\"Test\",\"SystemAddress\":42}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:03Z\",\"event\":\"Scan\",\"SystemAddress\":42,\"BodyName\":\"Test 1\",\"BodyID\":1,\"PlanetClass\":\"Rocky body\",\"WasFootfalled\":true}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:04Z\",\"event\":\"Scan\",\"SystemAddress\":42,\"BodyName\":\"Test 2\",\"BodyID\":2,\"PlanetClass\":\"Rocky body\",\"WasFootfalled\":true}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:05Z\",\"event\":\"ApproachBody\",\"StarSystem\":\"Test\",\"SystemAddress\":42,\"Body\":\"Test 1\",\"BodyID\":1}\n");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                profile,
                Path.Combine(root, "cache"),
                []);
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths);
            await viewModel.RefreshAsync();
            Assert.Equal(1, viewModel.SystemSurvey.Snapshot.CurrentBodyId);

            await File.AppendAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-25T12:00:06Z\",\"event\":\"SendText\",\"Message\":\".ff 2\"}\n");
            await viewModel.RefreshAsync();

            Assert.False(viewModel.SystemSurvey.Snapshot.Bodies.Single(body =>
                body.BodyId == 1).IsFirstFootfall);
            Assert.True(viewModel.SystemSurvey.Snapshot.Bodies.Single(body =>
                body.BodyId == 2).IsFirstFootfall);
            var systemPath = Assert.Single(Directory.GetFiles(
                Path.Combine(profile, "systems"),
                "*.json",
                SearchOption.AllDirectories));
            var bodies = JsonNode.Parse(
                await File.ReadAllTextAsync(systemPath))!["bodies"]!.AsArray();
            Assert.True(bodies.Single(body =>
                body!["id"]!.GetValue<int>() == 2)!["firstFootFall"]!
                .GetValue<bool>());
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
    public async Task DesktopTextCommandsAreLiveOnlyAndUsePlatformBoundaries()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-desktop-text-commands-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            var profile = Path.Combine(root, "profile");
            var screenshots = Path.Combine(root, "screenshots");
            var systemScreenshots = Path.Combine(screenshots, "Test");
            Directory.CreateDirectory(journals);
            Directory.CreateDirectory(systemScreenshots);
            var journalPath = Path.Combine(
                journals,
                "Journal.2026-07-25T120000.01.log");
            await File.WriteAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-25T12:00:00Z\",\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:01Z\",\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:02Z\",\"event\":\"Location\",\"StarSystem\":\"Test\",\"SystemAddress\":42}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:03Z\",\"event\":\"ApproachSettlement\",\"Name\":\"Haberlandt Survey\",\"MarketID\":12345,\"SystemAddress\":42,\"BodyID\":3,\"BodyName\":\"Test 1\",\"Latitude\":-12.5,\"Longitude\":44.25,\"StationEconomy\":\"$economy_Agri;\",\"StationEconomy_Localised\":\"Agriculture\",\"StationServices\":[\"dock\"]}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:04Z\",\"event\":\"SendText\",\"Message\":\".imgs\"}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:05Z\",\"event\":\"SendText\",\"Message\":\"!\"}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:06Z\",\"event\":\"SendText\",\"Message\":\".kill\"}\n");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                profile,
                Path.Combine(root, "cache"),
                []);
            new ScreenshotProcessingSettingsStore(paths.UiSettingsPath).Save(
                ScreenshotProcessingPreferences.CreateDefaults() with
                {
                    TargetFolder = screenshots,
                });
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths);
            DirectoryInfo? launchedDirectory = null;
            var shutdownCount = 0;
            viewModel.SetJournalCommandPlatformServices(
                directory =>
                {
                    launchedDirectory = directory;
                    return Task.FromResult(true);
                },
                () =>
                {
                    shutdownCount++;
                    return Task.CompletedTask;
                },
                _ => Task.CompletedTask);

            await viewModel.RefreshAsync();

            Assert.Null(launchedDirectory);
            Assert.Equal(0, shutdownCount);
            Assert.False(viewModel.GroundTarget.IsTargetActive);

            await File.AppendAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-25T12:00:07Z\",\"event\":\"SendText\",\"Message\":\".imgs\"}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:08Z\",\"event\":\"SendText\",\"Message\":\"!\"}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:09Z\",\"event\":\"SendText\",\"Message\":\".kill\"}\n");
            await viewModel.RefreshAsync();

            Assert.Equal(
                Path.GetFullPath(systemScreenshots),
                launchedDirectory?.FullName);
            Assert.Equal(1, shutdownCount);
            Assert.True(viewModel.GroundTarget.IsTargetActive);
            Assert.Equal(
                -12.5,
                double.Parse(
                    viewModel.GroundTarget.TargetLatitude,
                    CultureInfo.CurrentCulture));
            Assert.Equal(
                44.25,
                double.Parse(
                    viewModel.GroundTarget.TargetLongitude,
                    CultureInfo.CurrentCulture));
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
    public async Task DeveloperMeasurementCommandsUsePortableGeometryAndClipboard()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-developer-measurement-commands-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            var profile = Path.Combine(root, "profile");
            Directory.CreateDirectory(journals);
            var journalPath = Path.Combine(
                journals,
                "Journal.2026-07-25T120000.01.log");
            const string shipType = "test_measurement_ship";
            await File.WriteAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-25T12:00:00Z\",\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:01Z\",\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + $"{{\"timestamp\":\"2026-07-25T12:00:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Drew\",\"FID\":\"F123\",\"Ship\":\"{shipType}\"}}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:03Z\",\"event\":\"Location\",\"StarSystem\":\"Test\",\"SystemAddress\":42}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:04Z\",\"event\":\"ApproachSettlement\",\"Name\":\"Haberlandt Survey\",\"MarketID\":12345,\"SystemAddress\":42,\"BodyID\":3,\"BodyName\":\"Test 1\",\"Latitude\":-12.5,\"Longitude\":44.25,\"StationEconomy\":\"$economy_Agri;\",\"StationEconomy_Localised\":\"Agriculture\",\"StationServices\":[\"dock\"]}\n");
            await File.WriteAllTextAsync(
                Path.Combine(journals, "Status.json"),
                "{\"event\":\"Status\",\"Flags\":69206016,\"Flags2\":0,\"Latitude\":-12.49,\"Longitude\":44.26,\"Heading\":90,\"Altitude\":10,\"BodyName\":\"Test 1\",\"PlanetRadius\":1000}");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                profile,
                Path.Combine(root, "cache"),
                []);
            await new GroundTargetSettingsStore(profile).SaveAsync(
                new GroundTargetSnapshot(
                    true,
                    new SurfaceCoordinate(-12.5, 44.25)));
            await new HumanSiteKnowledgeStore(profile).SaveAsync(
                new HumanSiteKnowledgeContext(
                    "F123",
                    "Drew",
                    "Test",
                    42,
                    null,
                    1000),
                new HumanSiteLiveSnapshot(
                    "Haberlandt Survey",
                    "Haberlandt Survey",
                    12345,
                    42,
                    3,
                    "Test 1",
                    new HumanSiteSurfaceLocation(-12.5, 44.25),
                    HumanSiteEconomy.Agriculture,
                    "$economy_Agri;",
                    "Agriculture",
                    string.Empty,
                    null,
                    string.Empty,
                    string.Empty,
                    ["dock"],
                    "OnFootSettlement",
                    HumanSiteLandingPads.Empty,
                    1,
                    null,
                    30,
                    HumanSiteDockingStatus.None,
                    0,
                    null,
                    false,
                    DateTimeOffset.Parse("2026-07-25T12:00:04Z"),
                    DateTimeOffset.Parse("2026-07-25T12:00:04Z")),
                HumanSiteGeometrySource.ManualFoot);
            var log = new ApplicationLogService(profile);
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths,
                applicationLogService: log);
            var clipboardWrites = new List<string>();
            viewModel.SetJournalCommandPlatformServices(
                null,
                null,
                text =>
                {
                    clipboardWrites.Add(text);
                    return Task.CompletedTask;
                });
            await viewModel.RefreshAsync();
            Assert.Equal(30, viewModel.HumanSite.ActiveSite?.Heading);

            await File.AppendAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-25T12:00:05Z\",\"event\":\"SendText\",\"Message\":\"@@\"}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:06Z\",\"event\":\"SendText\",\"Message\":\"!!\"}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:07Z\",\"event\":\"SendText\",\"Message\":\"..\"}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:08Z\",\"event\":\"SendText\",\"Message\":\"//\"}\n");
            await viewModel.RefreshAsync();

            Assert.Collection(
                clipboardWrites,
                text =>
                {
                    Assert.Contains(shipType, text);
                    Assert.Contains("HumanSiteMapPoint", text);
                },
                text =>
                {
                    Assert.StartsWith("\"offset\":", text);
                    Assert.Contains("\"rot\": 60", text);
                },
                text => Assert.StartsWith("{ \"X\":", text));
            Assert.NotEqual(
                default,
                HumanSiteVehicleOffsets.Find(shipType));
            Assert.Contains(
                "Settlement offset comparison:",
                log.Text,
                StringComparison.Ordinal);
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
    public async Task LiveFirstFootfallInferenceSynchronizesBothLegacyStores()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-first-footfall-inference-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            var profile = Path.Combine(root, "profile");
            Directory.CreateDirectory(journals);
            var journalPath = Path.Combine(
                journals,
                "Journal.2026-07-25T120000.01.log");
            await File.WriteAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-25T12:00:00Z\",\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:01Z\",\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:02Z\",\"event\":\"Location\",\"StarSystem\":\"Test\",\"SystemAddress\":42,\"Population\":0}\n");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                profile,
                Path.Combine(root, "cache"),
                []);
            var inference = new StubFirstFootfallInferenceService(
                new FirstFootfallInferenceResult(
                    FirstFootfallInferenceOutcome.Detected,
                    0.004,
                    2,
                    null));
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths,
                firstFootfallInferenceService: inference);
            await viewModel.RefreshAsync();
            Assert.Equal(0, inference.CallCount);

            await File.AppendAllTextAsync(
                journalPath,
                "{\"timestamp\":\"2026-07-25T12:00:03Z\",\"event\":\"Disembark\",\"SystemAddress\":42,\"Body\":\"Test 1\",\"BodyID\":1,\"OnPlanet\":true,\"OnStation\":false}\n");
            await viewModel.RefreshAsync();

            Assert.Equal(1, inference.CallCount);
            Assert.True(Assert.Single(
                viewModel.SystemSurvey.Snapshot.Bodies).IsFirstFootfall);
            var systemPath = Assert.Single(Directory.GetFiles(
                Path.Combine(profile, "systems"),
                "*.json",
                SearchOption.AllDirectories));
            var system = JsonNode.Parse(
                await File.ReadAllTextAsync(systemPath))!.AsObject();
            Assert.True(
                system["bodies"]![0]!["firstFootFall"]!.GetValue<bool>());

            const string variant = "$Codex_Ent_Aleoids_01_B_Name;";
            const string species = "$Codex_Ent_Aleoids_01_Name;";
            const string genus = "$Codex_Ent_Aleoids_Genus_Name;";
            await File.AppendAllTextAsync(
                journalPath,
                $"{{\"timestamp\":\"2026-07-25T12:00:04Z\",\"event\":\"ScanOrganic\",\"ScanType\":\"Log\",\"Genus\":\"{genus}\",\"Species\":\"{species}\",\"Variant\":\"{variant}\",\"SystemAddress\":42,\"Body\":1}}\n"
                    + $"{{\"timestamp\":\"2026-07-25T12:00:05Z\",\"event\":\"ScanOrganic\",\"ScanType\":\"Sample\",\"Genus\":\"{genus}\",\"Species\":\"{species}\",\"Variant\":\"{variant}\",\"SystemAddress\":42,\"Body\":1}}\n"
                    + $"{{\"timestamp\":\"2026-07-25T12:00:06Z\",\"event\":\"ScanOrganic\",\"ScanType\":\"Analyse\",\"Genus\":\"{genus}\",\"Species\":\"{species}\",\"Variant\":\"{variant}\",\"SystemAddress\":42,\"Body\":1}}\n");
            await viewModel.RefreshAsync();
            var saved = await new CommanderProfileStore(profile)
                .LoadAsync("F123", true);
            Assert.All(
                saved.Data!.Exobiology.ScannedBioEntryIds,
                entry => Assert.EndsWith("_True", entry));

            Assert.True(await viewModel.ToggleCurrentBodyFirstFootfallAsync());
            saved = await new CommanderProfileStore(profile)
                .LoadAsync("F123", true);
            Assert.All(
                saved.Data!.Exobiology.ScannedBioEntryIds,
                entry => Assert.EndsWith("_False", entry));
            system = JsonNode.Parse(
                await File.ReadAllTextAsync(systemPath))!.AsObject();
            Assert.False(
                system["bodies"]![0]!["firstFootFall"]!.GetValue<bool>());
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
    public async Task BootstrapReplayNeverRunsFirstFootfallInference()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-first-footfall-bootstrap-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            Directory.CreateDirectory(journals);
            await File.WriteAllTextAsync(
                Path.Combine(journals, "Journal.2026-07-25T120000.01.log"),
                "{\"event\":\"Fileheader\",\"Odyssey\":true}\n"
                    + "{\"event\":\"Commander\",\"Name\":\"Drew\",\"FID\":\"F123\"}\n"
                    + "{\"timestamp\":\"2026-07-25T12:00:02Z\",\"event\":\"Location\",\"StarSystem\":\"Test\",\"SystemAddress\":42,\"Population\":0}\n"
                    + "{\"event\":\"Disembark\",\"SystemAddress\":42,\"Body\":\"Test 1\",\"BodyID\":1,\"OnPlanet\":true,\"OnStation\":false}\n");
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "profile"),
                Path.Combine(root, "cache"),
                []);
            var inference = new StubFirstFootfallInferenceService(
                new FirstFootfallInferenceResult(
                    FirstFootfallInferenceOutcome.Detected,
                    1,
                    1,
                    null));
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths,
                firstFootfallInferenceService: inference);

            await viewModel.RefreshAsync();

            Assert.Equal(0, inference.CallCount);
            Assert.False(Assert.Single(
                viewModel.SystemSurvey.Snapshot.Bodies).IsFirstFootfall);
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
            Assert.False(viewModel.QuestIndicator.ShouldShow);
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

    [Fact]
    public void MultipleGameWindowInventoryControlsSharedCargoSuppression()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-main-multi-cargo-tests-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            Directory.CreateDirectory(journals);
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "profile"),
                Path.Combine(root, "cache"),
                []);
            var switcher = new MutableGameWindowSwitcher
            {
                AvailableWindowCount = 2,
            };
            var eddnPublisher = new RecordingEddnPublisher();
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths,
                gameWindowSwitcher: switcher,
                eddnPublisher: eddnPublisher);

            Assert.True(viewModel.IsSharedCargoSuppressed);
            Assert.True(viewModel.DockToDock.SharedCargoSuppressed);
            Assert.True(viewModel.Colonization.SharedCargoSuppressed);
            Assert.True(eddnPublisher.SuspensionStates[^1]);

            switcher.AvailableWindowCount = 1;
            viewModel.CommanderInstances.RefreshGameWindowCount();

            Assert.False(viewModel.IsSharedCargoSuppressed);
            Assert.False(viewModel.DockToDock.SharedCargoSuppressed);
            Assert.False(viewModel.Colonization.SharedCargoSuppressed);
            Assert.False(eddnPublisher.SuspensionStates[^1]);

            switcher.AvailableWindowCount = 2;
            viewModel.CommanderInstances.RefreshGameWindowCount();

            Assert.True(viewModel.IsSharedCargoSuppressed);
            Assert.True(eddnPublisher.SuspensionStates[^1]);
            Assert.Contains(
                "cannot be attributed safely",
                viewModel.DockToDock.StatusMessage);
            Assert.Contains(
                "cannot be attributed safely",
                viewModel.Colonization.ShipCargoPublishingStatus);
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
    public async Task CargoProjectionAppliesJournalDeltasAndRequiresFreshFileAfterAmbiguity()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-main-cargo-projection-{Guid.NewGuid():N}");
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
                {"timestamp":"2026-07-25T12:00:00Z","event":"Commander","Name":"Drew","FID":"F123"}
                {"timestamp":"2026-07-25T12:00:01Z","event":"LoadGame","Commander":"Drew","FID":"F123","Odyssey":true}

                """);
            var cargoPath = Path.Combine(journals, CargoFileReader.FileName);
            await File.WriteAllTextAsync(
                cargoPath,
                """
                {"timestamp":"2026-07-25T12:00:02Z","event":"Cargo","Vessel":"Ship","Count":2,"Inventory":[{"Name":"gold","Count":2,"Stolen":0}]}
                """);
            var shipLockerPath = Path.Combine(
                journals,
                ShipLockerFileReader.FileName);
            await File.WriteAllTextAsync(
                shipLockerPath,
                """
                {"timestamp":"2026-07-25T12:00:02Z","event":"ShipLocker","Items":[{"Name":"healthmonitor","Name_Localised":"Health Monitor","Count":2}],"Components":[],"Consumables":[],"Data":[]}
                """);
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "profile"),
                Path.Combine(root, "cache"),
                []);
            var switcher = new MutableGameWindowSwitcher
            {
                AvailableWindowCount = 1,
            };
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths,
                gameWindowSwitcher: switcher);

            await viewModel.RefreshAsync();
            Assert.Equal(2, viewModel.CurrentCargo?.GetCount("gold"));
            Assert.Single(viewModel.FrontierProfile.CurrentShipCargo);
            Assert.Single(viewModel.FrontierProfile.CurrentShipLocker);

            await File.AppendAllTextAsync(
                journalPath,
                """
                {"timestamp":"2026-07-25T12:00:03Z","event":"CollectCargo","Type":"silver","Type_Localised":"Silver"}

                """);
            await viewModel.RefreshAsync();

            Assert.Equal(2, viewModel.CurrentCargo?.GetCount("gold"));
            Assert.Equal(1, viewModel.CurrentCargo?.GetCount("silver"));

            switcher.AvailableWindowCount = 2;
            viewModel.CommanderInstances.RefreshGameWindowCount();
            Assert.Null(viewModel.CurrentCargo);
            Assert.True(viewModel.IsWaitingForFreshCargoSnapshot);
            Assert.Empty(viewModel.FrontierProfile.CurrentShipCargo);
            Assert.Empty(viewModel.FrontierProfile.CurrentShipLocker);
            Assert.Contains(
                "multiple Elite windows",
                viewModel.FrontierProfile.LocalInventoryStatus);

            switcher.AvailableWindowCount = 1;
            viewModel.CommanderInstances.RefreshGameWindowCount();
            await File.AppendAllTextAsync(
                journalPath,
                """
                {"timestamp":"2026-07-25T12:00:04Z","event":"CollectCargo","Type":"gold"}

                """);
            await viewModel.RefreshAsync();

            Assert.Null(viewModel.CurrentCargo);
            Assert.True(viewModel.IsWaitingForFreshCargoSnapshot);

            await File.WriteAllTextAsync(
                cargoPath,
                """
                {"timestamp":"2026-07-25T12:00:05Z","event":"Cargo","Vessel":"Ship","Count":5,"Inventory":[{"Name":"gold","Count":5,"Stolen":0}]}
                """);
            await viewModel.RefreshAsync();

            Assert.Equal(5, viewModel.CurrentCargo?.GetCount("gold"));
            Assert.False(viewModel.IsWaitingForFreshCargoSnapshot);
            Assert.Single(viewModel.FrontierProfile.CurrentShipCargo);
            Assert.Empty(viewModel.FrontierProfile.CurrentShipLocker);
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
    public async Task IdleMonitorPollsDoNotRepeatUiOrPublicationProjection()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-main-idle-monitor-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            Directory.CreateDirectory(journals);
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "data"),
                Path.Combine(root, "cache"),
                []);
            var publisher = new RecordingEddnPublisher();
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: paths,
                eddnPublisher: publisher);
            await viewModel.RefreshAsync();
            var statusBefore = viewModel.StatusMessage;
            var lastUpdatedBefore = viewModel.LastUpdated;
            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(100));

            await viewModel.MonitorAsync(
                TimeSpan.FromMilliseconds(5),
                cancellation.Token);

            Assert.Single(publisher.Calls);
            Assert.Equal(statusBefore, viewModel.StatusMessage);
            Assert.Equal(lastUpdatedBefore, viewModel.LastUpdated);
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
    public async Task CommanderSwitchRejectsPreviousAccountsCompanionInventory()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-main-commander-inventory-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            Directory.CreateDirectory(journals);
            await File.WriteAllTextAsync(
                Path.Combine(journals, "Journal.2026-07-25T120000.01.log"),
                """
                {"timestamp":"2026-07-25T12:00:00Z","event":"Commander","Name":"First","FID":"F123"}
                {"timestamp":"2026-07-25T12:00:01Z","event":"LoadGame","Commander":"First","FID":"F123","Odyssey":true}

                """);
            var cargoPath = Path.Combine(journals, CargoFileReader.FileName);
            var lockerPath = Path.Combine(journals, ShipLockerFileReader.FileName);
            await File.WriteAllTextAsync(
                cargoPath,
                """
                {"timestamp":"2026-07-25T12:00:02Z","event":"Cargo","Vessel":"Ship","Count":2,"Inventory":[{"Name":"gold","Count":2,"Stolen":0}]}
                """);
            await File.WriteAllTextAsync(
                lockerPath,
                """
                {"timestamp":"2026-07-25T12:00:02Z","event":"ShipLocker","Items":[{"Name":"healthmonitor","Count":2}],"Components":[],"Consumables":[],"Data":[]}
                """);
            using var viewModel = new MainWindowViewModel(
                journals,
                appDataPaths: new AppDataPaths(
                    Path.Combine(root, "config"),
                    Path.Combine(root, "profile"),
                    Path.Combine(root, "cache"),
                    []));

            await viewModel.RefreshAsync();
            Assert.Equal(2, viewModel.CurrentCargo?.GetCount("gold"));
            Assert.Single(viewModel.FrontierProfile.CurrentShipLocker);

            await File.WriteAllTextAsync(
                Path.Combine(journals, "Journal.2026-07-25T130000.01.log"),
                """
                {"timestamp":"2026-07-25T13:00:00Z","event":"Commander","Name":"Second","FID":"F456"}
                {"timestamp":"2026-07-25T13:00:01Z","event":"LoadGame","Commander":"Second","FID":"F456","Odyssey":true}

                """);
            await viewModel.RefreshAsync();

            Assert.Equal("Second", viewModel.CommanderName);
            Assert.Null(viewModel.CurrentCargo);
            Assert.Empty(viewModel.FrontierProfile.CurrentShipLocker);
            Assert.True(viewModel.IsWaitingForFreshCargoSnapshot);

            await File.WriteAllTextAsync(
                cargoPath,
                """
                {"timestamp":"2026-07-25T13:00:02Z","event":"Cargo","Vessel":"Ship","Count":3,"Inventory":[{"Name":"silver","Count":3,"Stolen":0}]}
                """);
            await File.WriteAllTextAsync(
                lockerPath,
                """
                {"timestamp":"2026-07-25T13:00:02Z","event":"ShipLocker","Items":[],"Components":[{"Name":"microelectrode","Count":4}],"Consumables":[],"Data":[]}
                """);
            await viewModel.RefreshAsync();

            Assert.Equal(3, viewModel.CurrentCargo?.GetCount("silver"));
            Assert.False(viewModel.IsWaitingForFreshCargoSnapshot);
            Assert.Equal(
                "Microelectrode",
                Assert.Single(viewModel.FrontierProfile.CurrentShipLocker).Name);
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

    private static JournalEventEnvelope ParseJournalEvent(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(
                json,
                out var journalEvent,
                out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }

    private sealed class RecordingSystemBodyDataClient(
        SystemBodyDataLoadResult result) : ISystemBodyDataClient
    {
        public int CallCount { get; private set; }

        public Task<SystemBodyDataLoadResult> GetAsync(
            string systemName,
            long systemAddress,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class BlockingSystemBodyDataClient : ISystemBodyDataClient
    {
        private readonly object sync = new();
        private readonly List<long> requestedAddresses = [];
        private readonly List<long> canceledAddresses = [];

        public IReadOnlyList<long> RequestedAddresses
        {
            get
            {
                lock (sync)
                {
                    return requestedAddresses.ToArray();
                }
            }
        }

        public IReadOnlyList<long> CanceledAddresses
        {
            get
            {
                lock (sync)
                {
                    return canceledAddresses.ToArray();
                }
            }
        }

        public Task<SystemBodyDataLoadResult> GetAsync(
            string systemName,
            long systemAddress,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                requestedAddresses.Add(systemAddress);
            }

            var completion = new TaskCompletionSource<SystemBodyDataLoadResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() =>
            {
                lock (sync)
                {
                    canceledAddresses.Add(systemAddress);
                }

                completion.TrySetCanceled(cancellationToken);
            });
            return completion.Task;
        }
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

    private sealed class RecordingInaraPublisher : IInaraPublisher
    {
        public List<InaraPublicationUpdate> Calls { get; } = [];

        public int CancellationCount { get; private set; }

        public Task<InaraPublicationResult> ApplyAsync(
            InaraPublicationUpdate update,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(update);
            return Task.FromResult(new InaraPublicationResult(
                QueuedEventCount: 0,
                AcceptedEventCount: update.AllowPublishing
                    && update.JournalEvents.Count > 0
                        ? 1
                        : 0,
                PendingEventCount: 0,
                QueuedEventNames: [],
                Warnings: []));
        }

        public Task<InaraPublicationResult> FlushAsync(
            InaraPublicationOptions options,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(InaraPublicationResult.Empty);
        }

        public void CancelPendingPublication()
        {
            CancellationCount++;
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingInaraPublisher : IInaraPublisher
    {
        public Task<InaraPublicationResult> ApplyAsync(
            InaraPublicationUpdate update,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("simulated Inara failure");
        }

        public Task<InaraPublicationResult> FlushAsync(
            InaraPublicationOptions options,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("simulated Inara failure");
        }

        public void CancelPendingPublication()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingEddnPublisher : IEddnPublisher
    {
        public List<EddnCall> Calls { get; } = [];

        public List<bool> SuspensionStates { get; } = [];

        public Task<EddnPublicationResult> ApplyAsync(
            EddnApplyRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            Calls.Add(new EddnCall(
                request.JournalEvents.ToArray(),
                request.Enabled,
                request.UseTestSchemas,
                request.AllowPublishing,
                request.AllowSharedData));
            IReadOnlyList<EddnPublishedEvent> published =
                request.Enabled
                    && request.AllowPublishing
                    && request.JournalEvents.Count > 0
                    ? [new EddnPublishedEvent(
                        request.JournalEvents[0].EventName,
                        "https://eddn.edcd.io/schemas/test/1/test",
                        request.UseTestSchemas)]
                    : [];
            return Task.FromResult(new EddnPublicationResult(published, []));
        }

        public void SetEnabled(bool enabled)
        {
        }

        public void SetSuspended(bool suspended)
        {
            SuspensionStates.Add(suspended);
        }
    }

    private sealed record EddnCall(
        IReadOnlyList<JournalEventEnvelope> Events,
        bool Enabled,
        bool UseTestSchemas,
        bool AllowPublishing,
        bool AllowSharedData);

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
            IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>?
                guardianContexts = null,
            ScreenshotNavigationContext? navigationContext = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Events = journalEvents;
            CommanderName = commanderName;
            return Task.FromResult(ScreenshotProcessingResult.Empty);
        }
    }

    private sealed class StubFirstFootfallInferenceService(
        FirstFootfallInferenceResult result)
        : IFirstFootfallInferenceService
    {
        public int CallCount { get; private set; }

        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public Task<FirstFootfallInferenceResult> DetectAsync(
            FirstFootfallInferencePreferences preferences,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }

        public void Dispose()
        {
        }
    }

    private sealed class MutableGameWindowSwitcher : IGameWindowSwitcher
    {
        public int AvailableWindowCount { get; set; }

        public int GetAvailableWindowCount() => AvailableWindowCount;

        public bool TryActivateCurrent() => AvailableWindowCount > 0;

        public bool TryActivateNext() => AvailableWindowCount > 1;

        public void Dispose()
        {
        }
    }
}
