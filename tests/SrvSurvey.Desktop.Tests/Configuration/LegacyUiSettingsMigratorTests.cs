using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class LegacyUiSettingsMigratorTests : IDisposable
{
    /// <summary>A player-data import must leave unsaved overlay visibility and output defaults in effect.</summary>
    [Fact]
    public async Task ProfileImportPreservesOverlayDefaultsAlongsideCurrentPreferences()
    {
        AppDataPaths paths = CreatePaths();
        string source = Path.Combine(temporaryDirectory, "overlay-disabled-source");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(paths.ConfigDirectory);
        await File.WriteAllTextAsync(paths.UiSettingsPath, """{"Version":1,"Theme":"blue-dark"}""");
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            """
            {"autoShowPlotJumpInfo":false,"autoShowPlotBodyInfo":false,
             "hidePlottersFromCombatSuits":true,"streamOneOverlay":true,"displayVR":true}
            """
        );
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups")
        );

        new LegacyUiSettingsMigrator().MigrateIfNeeded(paths);

        Assert.True(new JumpInfoSettingsStore(paths.UiSettingsPath).Load().AutoShow);
        Assert.True(new SystemSurveySettingsStore(paths.UiSettingsPath).Load().AutoShowBodyInfo);
        Assert.False(new OverlayBehaviorSettingsStore(paths.UiSettingsPath).Load().HideInDominatorSuit);
        Assert.False(new StreamOverlaySettingsStore(paths.UiSettingsPath).LoadEnabled());
        Assert.False(new VrOverlaySettingsStore(paths.UiSettingsPath).Load().Enabled);
    }

    /// <summary>Reopening after import must preserve the active commander's saved overlay choices.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportAndSubsequentStartupPreserveCommanderOverlaySettings(bool autoShow)
    {
        AppDataPaths paths = CreatePaths() with { SettingsFrontierId = "F123" };
        string source = Path.Combine(temporaryDirectory, "commander-source");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.UiSettingsPath)!);
        const string sharedSettings = """{"Version":1,"Theme":"orange-dark"}""";
        await File.WriteAllTextAsync(paths.SharedUiSettingsPath, sharedSettings);
        string current = $$"""
            {"Version":1,"JumpInfo":{"AutoShow":{{autoShow.ToString().ToLowerInvariant()}} },
             "SystemSurvey":{"AutoShowBodyInfo":true},"OverlayScale":{"Index":4},
             "Colonization":{"Overlay":{"AutoShow":true} },"Input":{"Bindings":{"showJumpInfo":"CTRL J"} } }
            """;
        await File.WriteAllTextAsync(paths.UiSettingsPath, current);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            """
            {"autoShowPlotJumpInfo":false,"plotJumpInfoMinimal":true,"autoShowPlotBodyInfo":false,
             "plotterScale":22.0,"hideMultiFloatie":true,"streamOneOverlay":true,"displayVR":true,
             "autoShowPlotBuildCommodities":false,"keyActions_TEST":{"showJumpInfo":"ALT J"}}
            """
        );
        ProfileImportResult import = await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups")
        );

        Assert.True(new LegacyUiSettingsMigrator().MigrateIfNeeded(paths).Migrated);
        byte[] afterImport = await File.ReadAllBytesAsync(paths.UiSettingsPath);
        Assert.False(new LegacyUiSettingsMigrator().MigrateIfNeeded(paths).Migrated);

        Assert.Equal(afterImport, await File.ReadAllBytesAsync(paths.UiSettingsPath));
        JsonObject original = Assert.IsType<JsonObject>(JsonNode.Parse(current));
        JsonObject migrated = Assert.IsType<JsonObject>(JsonNode.Parse(afterImport));
        foreach (string section in new[] { "JumpInfo", "SystemSurvey", "OverlayScale", "Colonization", "Input" })
        {
            Assert.True(JsonNode.DeepEquals(original[section], migrated[section]), section);
        }
        Assert.Equal(autoShow, new JumpInfoSettingsStore(paths.UiSettingsPath).Load().AutoShow);
        Assert.False(new JumpInfoSettingsStore(paths.UiSettingsPath).Load().Minimal);
        Assert.False(new OverlayBehaviorSettingsStore(paths.UiSettingsPath).Load().HideMultiGameCommanderOverlay);
        Assert.False(new StreamOverlaySettingsStore(paths.UiSettingsPath).LoadEnabled());
        Assert.False(new VrOverlaySettingsStore(paths.UiSettingsPath).Load().Enabled);
        Assert.Equal(sharedSettings, await File.ReadAllTextAsync(paths.SharedUiSettingsPath));
        Assert.Equal(
            current,
            await File.ReadAllTextAsync(Path.Combine(import.BackupDirectory, LegacyUiSettingsMigrator.BackupFileName))
        );
    }

    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-legacy-ui-migration-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task ImportedLegacyPreferencesAreTranslatedWithoutLosingCurrentSettings()
    {
        AppDataPaths paths = CreatePaths();
        string source = Path.Combine(temporaryDirectory, "legacy");
        string backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(paths.ConfigDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            """
            {
              "darkTheme": true,
              "themeMainBlack": false,
              "lang": "de",
              "autoShowPlotJumpInfo": false,
              "plotJumpInfoMinimal": true,
              "showPlotJumpInfoIfNextHop": true,
              "useLastUpdatedFromSpanshNotEDSM": true,
              "autoShowPlotGalMap": false,
              "galMapFactions": false,
              "hideJournalWriteTimer": true,
              "keepOverlays": true,
              "hidePlottersFromCombatSuits": true,
              "hidePlottersFromMaverickSuits": false,
              "hideMultiFloatie": true,
              "plotterScale": 16.0,
              "focusGameOnStart": false,
              "focusGameOnMinimize": false,
              "focusGameAfterFsdJump": true,
              "minimizeToTray": true,
              "preferredCommander": "Drew",
              "watchedJournalFolder": "D:\\Elite Journals",
              "buildProjectsUrl_TEST": "http://localhost:7007",
              "autoShowPlotBodyInfo": false,
              "bodyInfoBubbleSize": 321,
              "highGravityWarningLevel": 2.75,
              "useExternalData": false,
              "useExternalBioData": true,
              "autoHideBioPlotOnRepeat": false,
              "keepBioPlottersVisibleEnabled": false,
              "keepBioPlottersVisibleDuration": 345,
              "tempRange_TEST": true,
              "watchFssSettings_TEST": {
                "saveDebugImages": true,
                "yellowHorizontalTolerance": 77,
                "yellowBar": {"t":11,"color":{"R":12,"G":13,"B":14}},
                "blackArea": {"t":21,"color":{"R":22,"G":23,"B":24}},
                "whiteText": {"t":31,"color":{"R":32,"G":33,"B":34}},
                "yellowText": {"t":41,"color":{"R":42,"G":43,"B":44}}
              },
              "eddnUpload": true,
              "eddnEnvironment": "dev",
              "uploadGGG": true,
              "bioPlotSize": 4,
              "bioRingBucketOne": 2.5,
              "bioRingBucketTwo": 6.5,
              "bioRingBucketThree": 11.5,
              "skipLowValueDSS": false,
              "skipLowValueAmount": 7654321,
              "formPredictionsCurrentBodyOnly": true,
              "formPredictionsRowFontSize": 2,
              "autoShowFootCombat_TEST": true,
              "autoShowPlotMassacre_TEST": true,
              "buildProjectsSuppressOtherOverlays": true,
              "enableGuardianSites": false,
              "autoShowGuardianSummary": false,
              "autoShowRamTah": false,
              "autoZoomGuardianNearObelisks": false,
              "autoZoomGuardianInTurret": true,
              "guardianComponentMaterials_TEST": true,
              "idxGuardianPlotter": 3,
              "disableRuinsMeasurementGrid": true,
              "disableAerialAlignmentGrid": true,
              "mapShowNotes": false,
              "mapShowLegend": false,
              "blinkTigger": 134217728,
              "blinkDuration": 2500,
              "autoShowHumanSitesTest": false,
              "plotHumanSiteWidth": 720,
              "plotHumanSiteHeight": 640,
              "humanSiteZoomShip": 1.25,
              "humanSiteZoomSRV": 2.5,
              "humanSiteZoomFoot": 3.5,
              "humanSiteAutoZoomInside": false,
              "humanSiteZoomInside": 5.5,
              "humanSiteAutoZoomTool": false,
              "humanSiteZoomTool": 7.5,
              "humanSiteShow_Medkit": false,
              "humanSiteShow_Battery": false,
              "humanSiteShow_DataTerminal": false,
              "humanSiteDotsOnCollection": false,
              "collectMatsCollectionStatsTest": true,
              "autoShowPlotStationInfo_TEST": false,
              "useSystemNickNames": true,
              "enableQuests": true,
              "processScreenshots": true,
              "addBannerToScreenshots": false,
              "deleteScreenshotOriginal": true,
              "useGuardianAerialScreenshotsFolder": false,
              "screenshotSourceFolder": "C:\\Legacy Shots",
              "screenshotTargetFolder": "D:\\Converted Shots",
              "rotateAndTruncateAlphaAerialScreenshots": false,
              "screenshotBannerColor": {"A":255,"R":18,"G":171,"B":239},
              "screenshotBannerLocalTime": true,
              "aerialAltAlpha": 1100,
              "aerialAltBeta": 1500,
              "aerialAltGamma": 1700,
              "autoShowFloatie_TEST": false,
              "allowNotifications": {
                "materialCountAfterPickup": false,
                "cargoMissionRemaining": false,
                "currentBoxelSearchStatus": false,
                "showNextBoxelToSearch": false,
                "showScreenshot": false
              },
              "buildProjects_TEST": true,
              "buildProjectsTrackShipCargo": true,
              "autoShowPlotBuildCommodities": false,
              "buildProjectsOnRightScreen": false,
              "buildProjectsShowSumFC_TEST": false,
              "buildProjectsShowSumFCDelta_TEST": true,
              "buildProjectsInlineSumFC_TEST": true,
              "buildProjectsCollapseGroupsWithFCEnough_TEST": false,
              "buildProjectsHighlightAlmostFC_TEST": true,
              "logDockToDockTimes": true,
              "streamOneOverlay": true,
              "displayVR": true,
              "vrProcessName": "vrcompositor",
              "keyhook_TEST": true,
              "hookDirectX_TEST": true,
              "hookDirectXDeviceId_TEST": "b7bd7df1-251e-4335-a994-8ce36011eeb2",
              "keyActions_TEST": {
                "showJumpInfo": "CTRL J",
                "copyNextBoxel": "SHIFT C"
              }
            }
            """
        );
        const string currentSettings =
            "{\"Version\":1,\"Theme\":\"green-dark\","
            + "\"FutureRoot\":{\"Enabled\":true},"
            + "\"JumpInfo\":{\"FutureOption\":42},"
            + "\"Input\":{\"FutureOption\":\"keep\","
            + "\"Bindings\":{\"futureAction\":\"ALT Z\"}},"
            + "\"Colonization\":{\"FleetCarrierCargoSyncEnabled\":true}}";
        await File.WriteAllTextAsync(paths.UiSettingsPath, currentSettings);
        ProfileImportResult import = await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            backups
        );

        LegacyUiSettingsMigrationResult result = new LegacyUiSettingsMigrator().MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        Assert.True(result.MappedPreferenceCount >= 10);
        string backupPath = Assert.IsType<string>(result.PreviousSettingsBackupPath);
        Assert.Equal(Path.Combine(import.BackupDirectory, LegacyUiSettingsMigrator.BackupFileName), backupPath);
        Assert.Equal(currentSettings, await File.ReadAllTextAsync(backupPath));
        Assert.Equal("green-dark", new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
        Assert.Equal("de", new LocalizationSettingsStore(paths.UiSettingsPath, paths.DataDirectory).Load());
        Assert.Equal(
            new DesktopBehaviorPreferences(false, false, true, true),
            new DesktopBehaviorSettingsStore(paths.UiSettingsPath).Load()
        );
        Assert.Equal(
            new CommanderPreferencePreferences("Drew", null),
            new CommanderPreferenceSettingsStore(paths.UiSettingsPath).Load()
        );
        Assert.Equal(
            new JournalPreferences("D:\\Elite Journals"),
            new JournalSettingsStore(paths.UiSettingsPath).Load()
        );
        Assert.Equal(
            new Uri("http://localhost:7007/"),
            new RavenServiceSettingsStore(paths.UiSettingsPath).LoadServiceUri()
        );

        Assert.True(new SystemNicknameSettingsStore(paths.UiSettingsPath).LoadEnabled());
        Assert.True(new QuestSettingsStore(paths.UiSettingsPath).LoadEnabled());
        Assert.Equal(
            new NetworkPrivacyPreferences(true, true, false),
            new NetworkPrivacySettingsStore(paths.UiSettingsPath).Load()
        );
        Assert.Equal(
            new ScreenshotProcessingPreferences(
                true,
                false,
                true,
                false,
                "C:\\Legacy Shots",
                "D:\\Converted Shots",
                false,
                "#12ABEF",
                true,
                1100,
                1500,
                1700
            ),
            new ScreenshotProcessingSettingsStore(paths.UiSettingsPath).Load()
        );
        Assert.True(new DockToDockSettingsStore(paths.UiSettingsPath).LoadEnabled());
        Assert.True(new ColonizationSettingsStore(paths.UiSettingsPath).LoadEnabled());
        Assert.True(new ColonizationSettingsStore(paths.UiSettingsPath).LoadShipCargoPublishingEnabled());

        JsonObject migrated = Assert.IsType<JsonObject>(
            JsonNode.Parse(await File.ReadAllTextAsync(paths.UiSettingsPath))
        );
        foreach (
            string excluded in new[]
            {
                "OverlayBehavior",
                "OverlayScale",
                "SystemSurvey",
                "GalaxyMap",
                "PulseOverlay",
                "BiologyPredictions",
                "BiologyRewards",
                "Combat",
                "GuardianOverlays",
                "GuardianGestures",
                "HumanSite",
                "StationInfo",
                "Notifications",
                "Streaming",
                "VirtualReality",
            }
        )
        {
            Assert.False(migrated.ContainsKey(excluded), $"Imported overlay section {excluded}.");
        }
        Assert.Null(migrated["JumpInfo"]?["AutoShow"]);
        Assert.Null(migrated["Colonization"]?["Overlay"]);
        Assert.Null(migrated["Input"]?["KeyboardEnabled"]);
        Assert.Null(migrated["Input"]?["Bindings"]?["showJumpInfo"]);
        Assert.True(migrated["Colonization"]?["FleetCarrierCargoSyncEnabled"]?.GetValue<bool>());
        Assert.True(migrated["FutureRoot"]?["Enabled"]?.GetValue<bool>());
        Assert.Equal(42, migrated["JumpInfo"]?["FutureOption"]?.GetValue<int>());
        Assert.Equal("keep", migrated["Input"]?["FutureOption"]?.GetValue<string>());
        Assert.Equal("ALT Z", migrated["Input"]?["Bindings"]?["futureAction"]?.GetValue<string>());
        Assert.Equal(
            import.Manifest.ImportedAtUtc,
            migrated["LegacyImport"]?["ImportedAtUtc"]?.GetValue<DateTimeOffset>()
        );
    }

    [Fact]
    public async Task ExistingVerifiedCurrentProfileBackupDoesNotBlockMissingPreferenceMigration()
    {
        AppDataPaths paths = CreatePaths();
        string source = Path.Combine(temporaryDirectory, "current-profile");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(paths.ConfigDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "{\"darkTheme\":true,\"hideMultiFloatie\":true}"
        );
        ProfileImportResult import = await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "current-profile-backups")
        );
        const string previousSettings = "{\"Version\":1,\"Theme\":\"orange-dark\"}";
        const string importedSettings = "{\"Version\":1,\"Theme\":\"green-light\"}";
        await File.WriteAllTextAsync(
            Path.Combine(import.BackupDirectory, LegacyUiSettingsMigrator.BackupFileName),
            previousSettings
        );
        await File.WriteAllTextAsync(
            Path.Combine(import.BackupDirectory, CrossPlatformUiSettingsImporter.CompletionSignalFileName),
            "completed"
        );
        await File.WriteAllTextAsync(paths.UiSettingsPath, importedSettings);

        LegacyUiSettingsMigrationResult result = new LegacyUiSettingsMigrator().MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        Assert.Null(result.Error);
        Assert.Equal(
            previousSettings,
            await File.ReadAllTextAsync(Path.Combine(import.BackupDirectory, LegacyUiSettingsMigrator.BackupFileName))
        );
        Assert.Equal("green-light", new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
        Assert.False(new OverlayBehaviorSettingsStore(paths.UiSettingsPath).Load().HideMultiGameCommanderOverlay);
        JsonObject migrated = Assert.IsType<JsonObject>(
            JsonNode.Parse(await File.ReadAllTextAsync(paths.UiSettingsPath))
        );
        Assert.Equal(
            import.Manifest.ImportedAtUtc,
            migrated["LegacyImport"]?["ImportedAtUtc"]?.GetValue<DateTimeOffset>()
        );
    }

    [Fact]
    public async Task ExistingBackupWithoutCurrentProfileCompletionResumesFullMigration()
    {
        AppDataPaths paths = CreatePaths();
        string source = Path.Combine(temporaryDirectory, "incomplete-current-profile");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(paths.ConfigDirectory);
        await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), "{\"darkTheme\":true}");
        ProfileImportResult import = await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "incomplete-current-profile-backups")
        );
        const string currentSettings = "{\"Version\":1,\"Theme\":\"green-light\"}";
        await File.WriteAllTextAsync(paths.UiSettingsPath, currentSettings);
        await File.WriteAllTextAsync(
            Path.Combine(import.BackupDirectory, LegacyUiSettingsMigrator.BackupFileName),
            currentSettings
        );

        LegacyUiSettingsMigrationResult result = new LegacyUiSettingsMigrator().MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        Assert.Null(result.Error);
        Assert.Equal(
            Path.Combine(import.BackupDirectory, LegacyUiSettingsMigrator.BackupFileName),
            result.PreviousSettingsBackupPath
        );
        Assert.Equal("green-light", new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
    }

    [Fact]
    public async Task ImportedLegacyFssDetectorDoesNotDisableTheXpDefault()
    {
        AppDataPaths paths = CreatePaths();
        string source = Path.Combine(temporaryDirectory, "legacy-disabled-fss");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), "{\"watchFssSettings_TEST\":null}");
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups-disabled-fss")
        );

        LegacyUiSettingsMigrationResult result = new LegacyUiSettingsMigrator().MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        Assert.Equal(
            SystemSurveyPreferences.Default.FssTuningDetector.Enabled,
            new SystemSurveySettingsStore(paths.UiSettingsPath).Load().FssTuningDetector.Enabled
        );
    }

    [Fact]
    public async Task MigrationIsIdempotentAndDoesNotReplaceNewPreferences()
    {
        AppDataPaths paths = CreatePaths();
        string source = Path.Combine(temporaryDirectory, "legacy");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), "{\"darkTheme\":true}");
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups")
        );
        var migrator = new LegacyUiSettingsMigrator();
        Assert.True(migrator.MigrateIfNeeded(paths).Migrated);
        new ThemePreferenceStore(paths.UiSettingsPath).SaveThemeKey("green-light");

        LegacyUiSettingsMigrationResult second = migrator.MigrateIfNeeded(paths);

        Assert.False(second.Migrated);
        Assert.Null(second.Error);
        Assert.Equal("green-light", new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
    }

    [Fact]
    public async Task ExistingImportMarkerReceivesMissingCommanderPreferenceOnly()
    {
        AppDataPaths paths = CreatePaths();
        string source = Path.Combine(temporaryDirectory, "legacy-commander-upgrade");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "{\"preferredCommander\":\"Drew\",\"darkTheme\":true}"
        );
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups-commander-upgrade")
        );
        var migrator = new LegacyUiSettingsMigrator();
        Assert.True(migrator.MigrateIfNeeded(paths).Migrated);
        var document = new UiSettingsDocumentStore(paths.UiSettingsPath);
        document.Update(root =>
        {
            root.Remove("CommanderPreference");
            root["Theme"] = "green-light";
        });

        LegacyUiSettingsMigrationResult result = migrator.MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        Assert.Equal(1, result.MappedPreferenceCount);
        Assert.Equal(
            new CommanderPreferencePreferences("Drew", null),
            new CommanderPreferenceSettingsStore(paths.UiSettingsPath).Load()
        );
        Assert.Equal("green-light", new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
        Assert.Null(result.PreviousSettingsBackupPath);
    }

    [Fact]
    public async Task ExistingImportMarkerPreservesMissingOverlayScaleOnly()
    {
        AppDataPaths paths = CreatePaths();
        string source = Path.Combine(temporaryDirectory, "legacy-scale-upgrade");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "{\"plotterScale\":22.0,\"darkTheme\":true}"
        );
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups-scale-upgrade")
        );
        var migrator = new LegacyUiSettingsMigrator();
        Assert.True(migrator.MigrateIfNeeded(paths).Migrated);
        var document = new UiSettingsDocumentStore(paths.UiSettingsPath);
        document.Update(root =>
        {
            root.Remove("OverlayScale");
            root["Theme"] = "green-light";
        });

        LegacyUiSettingsMigrationResult result = migrator.MigrateIfNeeded(paths);

        Assert.False(result.Migrated);
        Assert.Equal(0, result.MappedPreferenceCount);
        Assert.Equal(OverlayScalePreferences.Default, new OverlayScaleSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal("green-light", new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
        Assert.Null(result.PreviousSettingsBackupPath);
    }

    [Fact]
    public async Task ExistingImportMarkerPreservesMissingMultiGamePreferenceOnly()
    {
        AppDataPaths paths = CreatePaths();
        string source = Path.Combine(temporaryDirectory, "legacy-multi-game-upgrade");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "{\"hideMultiFloatie\":true,\"darkTheme\":true}"
        );
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups-multi-game-upgrade")
        );
        var migrator = new LegacyUiSettingsMigrator();
        Assert.True(migrator.MigrateIfNeeded(paths).Migrated);
        var document = new UiSettingsDocumentStore(paths.UiSettingsPath);
        document.Update(root =>
        {
            root.Remove("OverlayBehavior");
            root["Theme"] = "green-light";
        });

        LegacyUiSettingsMigrationResult result = migrator.MigrateIfNeeded(paths);

        Assert.False(result.Migrated);
        Assert.Equal(0, result.MappedPreferenceCount);
        Assert.False(new OverlayBehaviorSettingsStore(paths.UiSettingsPath).Load().HideMultiGameCommanderOverlay);
        Assert.Equal("green-light", new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
        Assert.Null(result.PreviousSettingsBackupPath);
    }

    [Fact]
    public async Task ExistingImportMarkerReceivesMissingRavenServiceOnly()
    {
        AppDataPaths paths = CreatePaths();
        string source = Path.Combine(temporaryDirectory, "legacy-raven-upgrade");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "{\"buildProjectsUrl_TEST\":\"https://localhost:7007\"," + "\"darkTheme\":true}"
        );
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups-raven-upgrade")
        );
        var migrator = new LegacyUiSettingsMigrator();
        Assert.True(migrator.MigrateIfNeeded(paths).Migrated);
        var document = new UiSettingsDocumentStore(paths.UiSettingsPath);
        document.Update(root =>
        {
            root.Remove("RavenService");
            root["Theme"] = "green-light";
        });

        LegacyUiSettingsMigrationResult result = migrator.MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        Assert.Equal(1, result.MappedPreferenceCount);
        Assert.Equal(
            new Uri("https://localhost:7007/"),
            new RavenServiceSettingsStore(paths.UiSettingsPath).LoadServiceUri()
        );
        Assert.Equal("green-light", new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
        Assert.Null(result.PreviousSettingsBackupPath);
    }

    [Fact]
    public async Task ExistingImportMarkerPreservesMissingTimestampProviderOnly()
    {
        AppDataPaths paths = CreatePaths();
        string source = Path.Combine(temporaryDirectory, "legacy-timestamp-upgrade");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "{\"useLastUpdatedFromSpanshNotEDSM\":true," + "\"darkTheme\":true}"
        );
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups-timestamp-upgrade")
        );
        var migrator = new LegacyUiSettingsMigrator();
        Assert.True(migrator.MigrateIfNeeded(paths).Migrated);
        var document = new UiSettingsDocumentStore(paths.UiSettingsPath);
        document.Update(root =>
        {
            root.Remove("JumpInfo");
            root["Theme"] = "green-light";
        });

        LegacyUiSettingsMigrationResult result = migrator.MigrateIfNeeded(paths);

        Assert.False(result.Migrated);
        Assert.Equal(0, result.MappedPreferenceCount);
        Assert.False(new JumpInfoSettingsStore(paths.UiSettingsPath).Load().UseSpanshLastUpdated);
        Assert.Equal("green-light", new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
        Assert.Null(result.PreviousSettingsBackupPath);
    }

    [Fact]
    public async Task ExistingImportMarkerPreservesMissingFirstFootfallInference()
    {
        AppDataPaths paths = CreatePaths();
        string source = Path.Combine(temporaryDirectory, "legacy-footfall-upgrade");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            """
            {
              "inferColor": { "A": 255, "R": 12, "G": 34, "B": 56 },
              "inferTolerance": 17,
              "inferThreshold": 0.004
            }
            """
        );
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups-footfall-upgrade")
        );
        var migrator = new LegacyUiSettingsMigrator();
        Assert.True(migrator.MigrateIfNeeded(paths).Migrated);
        var document = new UiSettingsDocumentStore(paths.UiSettingsPath);
        document.Update(root => root.Remove("FirstFootfallInference"));

        LegacyUiSettingsMigrationResult result = migrator.MigrateIfNeeded(paths);

        Assert.False(result.Migrated);
        Assert.Equal(0, result.MappedPreferenceCount);
        Assert.Equal(
            FirstFootfallInferencePreferences.Default,
            new FirstFootfallInferenceSettingsStore(paths.UiSettingsPath).Load()
        );
        Assert.Null(result.PreviousSettingsBackupPath);
    }

    [Fact]
    public async Task ImportWithoutPreferencesDoesNotRemigrateSettingsLeftByAnEarlierImport()
    {
        AppDataPaths paths = CreatePaths();
        string source = Path.Combine(temporaryDirectory, "codex-only");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.ConfigDirectory);
        await File.WriteAllTextAsync(Path.Combine(source, "F123-codex.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(paths.DataDirectory, "settings.json"), """{"darkTheme":true}""");
        const string current = """{"Theme":"green-light"}""";
        await File.WriteAllTextAsync(paths.UiSettingsPath, current);
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups")
        );

        LegacyUiSettingsMigrationResult result = new LegacyUiSettingsMigrator().MigrateIfNeeded(paths);

        Assert.Null(result.Error);
        Assert.False(result.Migrated);
        Assert.Equal(current, await File.ReadAllTextAsync(paths.UiSettingsPath));
    }

    [Fact]
    public async Task MalformedLegacySettingsDoNotChangeCurrentUiSettings()
    {
        AppDataPaths paths = CreatePaths();
        string source = Path.Combine(temporaryDirectory, "legacy");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(paths.ConfigDirectory);
        await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), "{\"darkTheme\":true,");
        const string currentSettings = "{\"Version\":1,\"Theme\":\"orange-dark\"}";
        await File.WriteAllTextAsync(paths.UiSettingsPath, currentSettings);
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups")
        );

        LegacyUiSettingsMigrationResult result = new LegacyUiSettingsMigrator().MigrateIfNeeded(paths);

        Assert.False(result.Migrated);
        Assert.NotNull(result.Error);
        Assert.Equal(currentSettings, await File.ReadAllTextAsync(paths.UiSettingsPath));
        Assert.Null(result.PreviousSettingsBackupPath);
    }

    /// <summary>Imports personal flora photos without relocating downloaded caches or enabling bulk downloads.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportKeepsLocalFloraAndCurrentImagePreferencesWithoutImportingDownloadedCache(
        bool hasCurrentPreferences
    )
    {
        AppDataPaths paths = CreatePaths();
        string source = Path.Combine(temporaryDirectory, "legacy-codex-images");
        string defaultCache = Path.Combine(paths.CacheDirectory, "codex-images");
        string currentCache = Path.Combine(paths.CacheDirectory, "chosen-images");
        if (hasCurrentPreferences)
        {
            Directory.CreateDirectory(paths.ConfigDirectory);
            await File.WriteAllTextAsync(
                paths.UiSettingsPath,
                new JsonObject
                {
                    ["Version"] = 1,
                    ["CodexImages"] = new JsonObject { ["CacheDirectory"] = currentCache, ["PreDownload"] = true },
                }.ToJsonString()
            );
        }
        string sourceCache = Path.Combine(source, "codexImages");
        string sourceFlora = Path.Combine(source, "local-flora");
        Directory.CreateDirectory(sourceCache);
        Directory.CreateDirectory(sourceFlora);
        await File.WriteAllBytesAsync(Path.Combine(sourceCache, "2310101.jpg"), [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(Path.Combine(sourceFlora, "aleoida-arcus-yellow.png"), [5, 6, 7, 8]);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            new JsonObject
            {
                ["downloadCodexImageFolder"] = sourceCache,
                ["localFloraFolder"] = sourceFlora,
                ["preDownloadCodexImages"] = true,
            }.ToJsonString()
        );
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups-codex-images")
        );

        LegacyUiSettingsMigrationResult result = new LegacyUiSettingsMigrator().MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        CodexImagePreferences preferences = new CodexImageSettingsStore(paths.UiSettingsPath, defaultCache).Load();
        Assert.Equal(hasCurrentPreferences ? currentCache : defaultCache, preferences.CacheDirectory);
        Assert.Equal(Path.Combine(paths.DataDirectory, "local-flora"), preferences.LocalFloraDirectory);
        Assert.Equal(hasCurrentPreferences, preferences.PreDownload);
        Assert.False(Directory.Exists(Path.Combine(paths.DataDirectory, "codexImages")));
        Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(Path.Combine(sourceCache, "2310101.jpg")));
        Assert.Equal(
            [5, 6, 7, 8],
            await File.ReadAllBytesAsync(Path.Combine(preferences.LocalFloraDirectory!, "aleoida-arcus-yellow.png"))
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private AppDataPaths CreatePaths()
    {
        return new AppDataPaths(
            Path.Combine(temporaryDirectory, "config"),
            Path.Combine(temporaryDirectory, "data"),
            Path.Combine(temporaryDirectory, "cache"),
            []
        );
    }
}
