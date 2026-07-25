using System.Text.Json.Nodes;
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
              "autoShowPlotBodyInfo": false,
              "bodyInfoBubbleSize": 321,
              "highGravityWarningLevel": 2.75,
              "useExternalData": false,
              "bioPlotSize": 4,
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
              "buildProjects_TEST": true,
              "autoShowPlotBuildCommodities": false,
              "buildProjectsOnRightScreen": false,
              "buildProjectsShowSumFC_TEST": false,
              "buildProjectsShowSumFCDelta_TEST": true,
              "buildProjectsInlineSumFC_TEST": true,
              "buildProjectsCollapseGroupsWithFCEnough_TEST": false,
              "buildProjectsHighlightAlmostFC_TEST": true,
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

        var survey = new SystemSurveySettingsStore(paths.UiSettingsPath).Load();
        Assert.False(survey.AutoShowBodyInfo);
        Assert.Equal(321, survey.BodyInfoBubbleSizeLy);
        Assert.Equal(2.75, survey.HighGravityWarningLevel);
        Assert.False(survey.UseExternalData);
        Assert.Equal(4, survey.SurfaceRadarSize);
        Assert.False(survey.HighlightDssCandidates);
        Assert.Equal(7_654_321, survey.DssValueFloor);
        Assert.Equal(
            new BiologyPredictionsPreferences(true, 3),
            new BiologyPredictionsSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new CombatPreferences(true, true, true),
            new CombatSettingsStore(paths.UiSettingsPath).Load());
        Assert.Equal(
            new GuardianOverlayPreferences(false, false, false, true),
            new GuardianOverlaySettingsStore(paths.UiSettingsPath).Load());
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

        var colonization = new ColonizationSettingsStore(paths.UiSettingsPath);
        Assert.True(colonization.LoadEnabled());
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
