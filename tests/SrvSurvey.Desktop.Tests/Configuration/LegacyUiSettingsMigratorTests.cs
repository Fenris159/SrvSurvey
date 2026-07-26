using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class LegacyUiSettingsMigratorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-legacy-ui-migration-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ImportedLegacyPreferencesAreTranslatedWithoutLosingCurrentSettings()
    {
        var paths = CreatePaths();
        var source = Path.Combine(temporaryDirectory, "legacy");
        var backups = Path.Combine(temporaryDirectory, "backups");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(paths.ConfigDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            """
            {
              "darkTheme": true,
              "themeMainBlack": false,
              "autoShowPlotJumpInfo": false,
              "plotJumpInfoMinimal": true,
              "showPlotJumpInfoIfNextHop": true,
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
              "eddnEnvironment": "live",
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
            """);
        const string currentSettings =
            "{\"Version\":1,\"Theme\":\"green-dark\","
            + "\"FutureRoot\":{\"Enabled\":true},"
            + "\"JumpInfo\":{\"FutureOption\":42},"
            + "\"Input\":{\"FutureOption\":\"keep\","
            + "\"Bindings\":{\"futureAction\":\"ALT Z\"}},"
            + "\"Colonization\":{\"FleetCarrierCargoSyncEnabled\":true}}";
        await File.WriteAllTextAsync(paths.UiSettingsPath, currentSettings);
        var import = await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            backups);

        var result = new LegacyUiSettingsMigrator().MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        Assert.True(result.MappedPreferenceCount >= 45);
        var backupPath = Assert.IsType<string>(
            result.PreviousSettingsBackupPath);
        Assert.Equal(
            Path.Combine(import.BackupDirectory, LegacyUiSettingsMigrator.BackupFileName),
            backupPath);
        Assert.Equal(currentSettings, await File.ReadAllTextAsync(backupPath));
        Assert.Equal(
            "blue-dark",
            new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
        Assert.Equal(
            new JumpInfoPreferences(false, true, true),
            new JumpInfoSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new GalaxyMapPreferences(false, false),
            new GalaxyMapSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new PulseOverlayPreferences(false),
            new PulseOverlaySettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new OverlayBehaviorPreferences(true, true, false, true),
            new OverlayBehaviorSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new OverlayScalePreferences(16),
            new OverlayScaleSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new DesktopBehaviorPreferences(false, false, true, true),
            new DesktopBehaviorSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new CommanderPreferencePreferences("Drew", null),
            new CommanderPreferenceSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new JournalPreferences("D:\\Elite Journals"),
            new JournalSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new Uri("http://localhost:7007/"),
            new RavenServiceSettingsStore(paths.UiSettingsPath)
                .LoadServiceUri());

        var survey = new SystemSurveySettingsStore(paths.UiSettingsPath).Load();
        Assert.False(survey.AutoShowBodyInfo);
        Assert.Equal(321, survey.BodyInfoBubbleSizeLy);
        Assert.Equal(2.75, survey.HighGravityWarningLevel);
        Assert.False(survey.UseExternalData);
        Assert.True(survey.UseExternalBioData);
        Assert.False(survey.AutoHideBioPlotOnRepeat);
        Assert.False(survey.KeepBioPlottersVisibleAfterDss);
        Assert.Equal(345, survey.BioPlotterDssDurationSeconds);
        Assert.True(survey.ShowTemperatureRangeDebug);
        Assert.Equal(
            new FssTuningDetectorSettings(
                true,
                true,
                new FssPixelColor(12, 13, 14, 11),
                77,
                new FssPixelColor(22, 23, 24, 21),
                new FssPixelColor(32, 33, 34, 31),
                new FssPixelColor(42, 43, 44, 41)),
            survey.FssTuningDetector);
        Assert.Equal(4, survey.SurfaceRadarSize);
        Assert.False(survey.HighlightDssCandidates);
        Assert.Equal(7_654_321, survey.DssValueFloor);
        Assert.Equal(
            new BiologyPredictionsPreferences(true, 3),
            new BiologyPredictionsSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new BiologyRewardThresholds(2.5, 6.5, 11.5),
            new BiologyRewardSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new CombatPreferences(true, true, true),
            new CombatSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new GuardianOverlayPreferences(
                false,
                false,
                false,
                true,
                false,
                true,
                true,
                3,
                true,
                true,
                false,
                false),
            new GuardianOverlaySettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new GuardianGesturePreferences(StatusFlags.HudInAnalysisMode, 2_500),
            new GuardianGestureSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new HumanSitePreferences(
                false,
                720,
                640,
                1.25,
                2.5,
                3.5,
                false,
                5.5,
                false,
                7.5,
                false,
                false,
                false,
                false,
                true,
                true),
            new HumanSiteSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new StationInfoPreferences(false),
            new StationInfoSettingsStore(paths.UiSettingsPath).Load());
        Assert.True(
            new SystemNicknameSettingsStore(paths.UiSettingsPath).LoadEnabled());
        Assert.True(new QuestSettingsStore(paths.UiSettingsPath).LoadEnabled());
        Assert.Equal(
            new NetworkPrivacyPreferences(true, "live", true),
            new NetworkPrivacySettingsStore(paths.UiSettingsPath).Load());
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
                1700),
            new ScreenshotProcessingSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new NotificationPreferences(
                false,
                false,
                false,
                false,
                false,
                false),
            new NotificationSettingsStore(paths.UiSettingsPath).Load());

        var colonization = new ColonizationSettingsStore(paths.UiSettingsPath);
        Assert.True(colonization.LoadEnabled());
        Assert.True(colonization.LoadShipCargoPublishingEnabled());
        Assert.True(colonization.LoadFleetCarrierCargoSyncEnabled());
        Assert.Equal(
            new ColonizationOverlayPreferences(
                false,
                false,
                false,
                true,
                true,
                false,
                true),
            colonization.LoadOverlayPreferences());

        var input = new GlobalInputSettingsStore(paths.UiSettingsPath).Load();
        Assert.True(input.KeyboardEnabled);
        Assert.True(input.ControllerEnabled);
        Assert.Equal(
            "b7bd7df1-251e-4335-a994-8ce36011eeb2",
            input.ControllerDeviceId);
        Assert.Equal("CTRL J", input.Bindings[GlobalInputAction.ShowJumpInfo]);
        Assert.Equal("SHIFT C", input.Bindings[GlobalInputAction.CopyNextBoxel]);
        Assert.True(
            new StreamOverlaySettingsStore(paths.UiSettingsPath).LoadEnabled());
        Assert.Equal(
            new VrOverlayPreferences(true, "vrcompositor"),
            new VrOverlaySettingsStore(paths.UiSettingsPath).Load());
        Assert.True(
            new DockToDockSettingsStore(paths.UiSettingsPath).LoadEnabled());

        var migrated = Assert.IsType<JsonObject>(
            JsonNode.Parse(await File.ReadAllTextAsync(paths.UiSettingsPath)));
        Assert.True(migrated["FutureRoot"]?["Enabled"]?.GetValue<bool>());
        Assert.Equal(42, migrated["JumpInfo"]?["FutureOption"]?.GetValue<int>());
        Assert.Equal("keep", migrated["Input"]?["FutureOption"]?.GetValue<string>());
        Assert.Equal(
            "ALT Z",
            migrated["Input"]?["Bindings"]?["futureAction"]?.GetValue<string>());
        Assert.Equal(
            import.Manifest.ImportedAtUtc,
            migrated["LegacyImport"]?["ImportedAtUtc"]
                ?.GetValue<DateTimeOffset>());
    }

    [Fact]
    public async Task ExplicitlyDisabledLegacyFssDetectorRemainsDisabled()
    {
        var paths = CreatePaths();
        var source = Path.Combine(temporaryDirectory, "legacy-disabled-fss");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "{\"watchFssSettings_TEST\":null}");
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups-disabled-fss"));

        var result = new LegacyUiSettingsMigrator().MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        Assert.False(
            new SystemSurveySettingsStore(paths.UiSettingsPath)
                .Load()
                .FssTuningDetector
                .Enabled);
    }

    [Fact]
    public async Task MigrationIsIdempotentAndDoesNotReplaceNewPreferences()
    {
        var paths = CreatePaths();
        var source = Path.Combine(temporaryDirectory, "legacy");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "{\"darkTheme\":true}");
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups"));
        var migrator = new LegacyUiSettingsMigrator();
        Assert.True(migrator.MigrateIfNeeded(paths).Migrated);
        new ThemePreferenceStore(paths.UiSettingsPath).SaveThemeKey("green-light");

        var second = migrator.MigrateIfNeeded(paths);

        Assert.False(second.Migrated);
        Assert.Null(second.Error);
        Assert.Equal(
            "green-light",
            new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
    }

    [Fact]
    public async Task ExistingImportMarkerReceivesMissingCommanderPreferenceOnly()
    {
        var paths = CreatePaths();
        var source = Path.Combine(temporaryDirectory, "legacy-commander-upgrade");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "{\"preferredCommander\":\"Drew\",\"darkTheme\":true}");
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups-commander-upgrade"));
        var migrator = new LegacyUiSettingsMigrator();
        Assert.True(migrator.MigrateIfNeeded(paths).Migrated);
        var document = new UiSettingsDocumentStore(paths.UiSettingsPath);
        document.Update(root =>
        {
            root.Remove("CommanderPreference");
            root["Theme"] = "green-light";
        });

        var result = migrator.MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        Assert.Equal(1, result.MappedPreferenceCount);
        Assert.Equal(
            new CommanderPreferencePreferences("Drew", null),
            new CommanderPreferenceSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            "green-light",
            new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
        Assert.Null(result.PreviousSettingsBackupPath);
    }

    [Fact]
    public async Task ExistingImportMarkerReceivesMissingOverlayScaleOnly()
    {
        var paths = CreatePaths();
        var source = Path.Combine(temporaryDirectory, "legacy-scale-upgrade");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "{\"plotterScale\":22.0,\"darkTheme\":true}");
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups-scale-upgrade"));
        var migrator = new LegacyUiSettingsMigrator();
        Assert.True(migrator.MigrateIfNeeded(paths).Migrated);
        var document = new UiSettingsDocumentStore(paths.UiSettingsPath);
        document.Update(root =>
        {
            root.Remove("OverlayScale");
            root["Theme"] = "green-light";
        });

        var result = migrator.MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        Assert.Equal(1, result.MappedPreferenceCount);
        Assert.Equal(
            new OverlayScalePreferences(22),
            new OverlayScaleSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            "green-light",
            new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
        Assert.Null(result.PreviousSettingsBackupPath);
    }

    [Fact]
    public async Task ExistingImportMarkerReceivesMissingMultiGamePreferenceOnly()
    {
        var paths = CreatePaths();
        var source = Path.Combine(temporaryDirectory, "legacy-multi-game-upgrade");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "{\"hideMultiFloatie\":true,\"darkTheme\":true}");
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups-multi-game-upgrade"));
        var migrator = new LegacyUiSettingsMigrator();
        Assert.True(migrator.MigrateIfNeeded(paths).Migrated);
        var document = new UiSettingsDocumentStore(paths.UiSettingsPath);
        document.Update(root =>
        {
            var behavior = Assert.IsType<JsonObject>(root["OverlayBehavior"]);
            behavior.Remove("HideMultiGameCommanderOverlay");
            root["Theme"] = "green-light";
        });

        var result = migrator.MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        Assert.Equal(1, result.MappedPreferenceCount);
        Assert.True(new OverlayBehaviorSettingsStore(paths.UiSettingsPath)
            .Load()
            .HideMultiGameCommanderOverlay);
        Assert.Equal(
            "green-light",
            new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
        Assert.Null(result.PreviousSettingsBackupPath);
    }

    [Fact]
    public async Task ExistingImportMarkerReceivesMissingRavenServiceOnly()
    {
        var paths = CreatePaths();
        var source = Path.Combine(temporaryDirectory, "legacy-raven-upgrade");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "{\"buildProjectsUrl_TEST\":\"https://localhost:7007\","
                + "\"darkTheme\":true}");
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups-raven-upgrade"));
        var migrator = new LegacyUiSettingsMigrator();
        Assert.True(migrator.MigrateIfNeeded(paths).Migrated);
        var document = new UiSettingsDocumentStore(paths.UiSettingsPath);
        document.Update(root =>
        {
            root.Remove("RavenService");
            root["Theme"] = "green-light";
        });

        var result = migrator.MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        Assert.Equal(1, result.MappedPreferenceCount);
        Assert.Equal(
            new Uri("https://localhost:7007/"),
            new RavenServiceSettingsStore(paths.UiSettingsPath)
                .LoadServiceUri());
        Assert.Equal(
            "green-light",
            new ThemePreferenceStore(paths.UiSettingsPath).LoadThemeKey());
        Assert.Null(result.PreviousSettingsBackupPath);
    }

    [Fact]
    public async Task ExistingImportMarkerReceivesMissingFirstFootfallInference()
    {
        var paths = CreatePaths();
        var source = Path.Combine(temporaryDirectory, "legacy-footfall-upgrade");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            """
            {
              "inferColor": { "A": 255, "R": 12, "G": 34, "B": 56 },
              "inferTolerance": 17,
              "inferThreshold": 0.004
            }
            """);
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups-footfall-upgrade"));
        var migrator = new LegacyUiSettingsMigrator();
        Assert.True(migrator.MigrateIfNeeded(paths).Migrated);
        var document = new UiSettingsDocumentStore(paths.UiSettingsPath);
        document.Update(root => root.Remove("FirstFootfallInference"));

        var result = migrator.MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        Assert.Equal(5, result.MappedPreferenceCount);
        Assert.Equal(
            FirstFootfallInferencePreferences.Default with
            {
                Red = 12,
                Green = 34,
                Blue = 56,
                Tolerance = 17,
                Threshold = 0.004,
            },
            new FirstFootfallInferenceSettingsStore(paths.UiSettingsPath)
                .Load());
        Assert.Null(result.PreviousSettingsBackupPath);
    }

    [Fact]
    public async Task MalformedLegacySettingsDoNotChangeCurrentUiSettings()
    {
        var paths = CreatePaths();
        var source = Path.Combine(temporaryDirectory, "legacy");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(paths.ConfigDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            "{\"darkTheme\":true,");
        const string currentSettings =
            "{\"Version\":1,\"Theme\":\"orange-dark\"}";
        await File.WriteAllTextAsync(paths.UiSettingsPath, currentSettings);
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups"));

        var result = new LegacyUiSettingsMigrator().MigrateIfNeeded(paths);

        Assert.False(result.Migrated);
        Assert.NotNull(result.Error);
        Assert.Equal(currentSettings, await File.ReadAllTextAsync(paths.UiSettingsPath));
        Assert.Null(result.PreviousSettingsBackupPath);
    }

    [Fact]
    public async Task ImportedCodexImageDirectoriesMoveWithVerifiedProfile()
    {
        var paths = CreatePaths();
        var source = Path.Combine(temporaryDirectory, "legacy-codex-images");
        var sourceCache = Path.Combine(source, "codexImages");
        var sourceFlora = Path.Combine(source, "local-flora");
        Directory.CreateDirectory(sourceCache);
        Directory.CreateDirectory(sourceFlora);
        await File.WriteAllBytesAsync(
            Path.Combine(sourceCache, "2310101.jpg"),
            [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(
            Path.Combine(sourceFlora, "aleoida-arcus-yellow.png"),
            [5, 6, 7, 8]);
        await File.WriteAllTextAsync(
            Path.Combine(source, "settings.json"),
            new JsonObject
            {
                ["downloadCodexImageFolder"] = sourceCache,
                ["localFloraFolder"] = sourceFlora,
                ["preDownloadCodexImages"] = true,
            }.ToJsonString());
        await new LegacyProfileImporter().ImportAsync(
            source,
            paths.DataDirectory,
            Path.Combine(temporaryDirectory, "backups-codex-images"));

        var result = new LegacyUiSettingsMigrator().MigrateIfNeeded(paths);

        Assert.True(result.Migrated);
        var preferences = new CodexImageSettingsStore(
            paths.UiSettingsPath,
            paths.CacheDirectory).Load();
        Assert.Equal(
            Path.Combine(paths.DataDirectory, "codexImages"),
            preferences.CacheDirectory);
        Assert.Equal(
            Path.Combine(paths.DataDirectory, "local-flora"),
            preferences.LocalFloraDirectory);
        Assert.True(preferences.PreDownload);
        Assert.Equal(
            [1, 2, 3, 4],
            await File.ReadAllBytesAsync(Path.Combine(
                preferences.CacheDirectory,
                "2310101.jpg")));
        Assert.Equal(
            [5, 6, 7, 8],
            await File.ReadAllBytesAsync(Path.Combine(
                preferences.LocalFloraDirectory!,
                "aleoida-arcus-yellow.png")));
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
            []);
    }
}
