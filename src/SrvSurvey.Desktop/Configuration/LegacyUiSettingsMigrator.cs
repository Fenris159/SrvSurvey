using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.Configuration;

public sealed class LegacyUiSettingsMigrator
{
    private const string BuildProjectsSuppressOtherOverlaysKey =
        "buildProjectsSuppressOtherOverlays";
    private const string SuppressForActiveBuildProjectsProperty =
        "SuppressForActiveBuildProjects";

    public const string BackupFileName = "previous-cross-platform-ui.json";

    public LegacyUiSettingsMigrationResult MigrateIfNeeded(AppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var manifestPath = Path.Combine(
            paths.DataDirectory,
            LegacyProfileImporter.ManifestFileName);
        var legacySettingsPath = Path.Combine(paths.DataDirectory, "settings.json");
        if (!File.Exists(manifestPath) || !File.Exists(legacySettingsPath))
        {
            return LegacyUiSettingsMigrationResult.NotRequired;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<ProfileImportManifest>(
                File.ReadAllText(manifestPath))
                ?? throw new InvalidDataException(
                    "The legacy import manifest is empty.");
            var legacy = JsonNode.Parse(File.ReadAllText(legacySettingsPath))
                as JsonObject
                ?? throw new InvalidDataException(
                    "The imported legacy settings file is not a JSON object.");
            var store = new UiSettingsDocumentStore(paths.UiSettingsPath);
            var existing = store.Load();
            if (HasMigrationMarker(existing, manifest))
            {
                return MigrateNewPreferencesIfMissing(
                    legacy,
                    existing,
                    store);
            }

            var backupPath = BackupExistingSettings(paths.UiSettingsPath, manifest);
            var mappedCount = 0;
            store.Update(root =>
            {
                root["Version"] = 1;
                mappedCount += MapTheme(legacy, root);
                mappedCount += MapSection(legacy, root, "JumpInfo",
                [
                    ("autoShowPlotJumpInfo", "AutoShow"),
                    ("plotJumpInfoMinimal", "Minimal"),
                    ("showPlotJumpInfoIfNextHop", "ShowWhenNextHopSelected"),
                    ("useLastUpdatedFromSpanshNotEDSM", "UseSpanshLastUpdated"),
                ]);
                mappedCount += MapSection(legacy, root, "GalaxyMap",
                [
                    ("autoShowPlotGalMap", "AutoShow"),
                    ("galMapFactions", "ShowFactions"),
                ]);
                mappedCount += MapPulseOverlay(legacy, root);
                mappedCount += MapSection(legacy, root, "OverlayBehavior",
                [
                    ("keepOverlays", "KeepWhenGameLosesFocus"),
                    ("hidePlottersFromCombatSuits", "HideInDominatorSuit"),
                    ("hidePlottersFromMaverickSuits", "HideInMaverickSuit"),
                    ("hideMultiFloatie", "HideMultiGameCommanderOverlay"),
                ]);
                mappedCount += MapOverlayScale(legacy, root);
                mappedCount += MapSection(legacy, root, "DesktopBehavior",
                [
                    ("focusGameOnStart", "FocusGameOnStart"),
                    ("focusGameOnMinimize", "FocusGameOnMinimize"),
                    ("focusGameAfterFsdJump", "FocusGameAfterFsdJump"),
                    ("minimizeToTray", "MinimizeToTray"),
                ]);
                mappedCount += MapSection(legacy, root, "CommanderPreference",
                [
                    ("preferredCommander", "PreferredCommanderName"),
                ]);
                mappedCount += MapSection(legacy, root, "Journal",
                [
                    ("watchedJournalFolder", "Directory"),
                ]);
                mappedCount += MapSection(legacy, root, "RavenService",
                [
                    ("buildProjectsUrl_TEST", "ServiceUri"),
                ]);
                mappedCount += MapSection(legacy, root, "SystemSurvey",
                [
                    ("autoShowPlotBodyInfo", "AutoShowBodyInfo"),
                    ("autoShowPlotBodyInfoInMap", "ShowBodyInfoInSystemMap"),
                    ("autoShowPlotBodyInfoInOrbit", "ShowBodyInfoInOrbit"),
                    ("autoShowPlotBodyInfoAtSurface", "ShowBodyInfoAtSurface"),
                    ("autoHidePlotBodyInfoInBubble", "HideBodyInfoInBubble"),
                    ("bodyInfoBubbleSize", "BodyInfoBubbleSizeLy"),
                    ("bodyInfoHideMats", "HideBodyInfoMaterials"),
                    ("autoShowFlightWarnings", "AutoShowFlightWarnings"),
                    ("highGravityWarningLevel", "HighGravityWarningLevel"),
                    ("useExternalData", "UseExternalData"),
                    ("useExternalBioData", "UseExternalBioData"),
                    ("autoShowPlotBioSystem", "AutoShowBioSystem"),
                    ("autoShowBioSummary", "AutoShowBioStatus"),
                    ("autoHideBioPlotOnRepeat", "AutoHideBioPlotOnRepeat"),
                    ("keepBioPlottersVisibleEnabled", "KeepBioPlottersVisibleAfterDss"),
                    ("keepBioPlottersVisibleDuration", "BioPlotterDssDurationSeconds"),
                    ("autoLoadPriorScans", "AutoShowPriorScans"),
                    ("skipPriorScansLowValue", "SkipPriorScansLowValue"),
                    ("skipPriorScansLowValueAmount", "PriorScanMinimumValue"),
                    ("hideMyOwnCanonnSignals", "HideOwnCanonnSignals"),
                    ("showCanonnSignalsOnRadar", "ShowCanonnSignalsOnRadar"),
                    ("useSmallCirclesWithCanonn", "UseSmallCanonnRadarCircles"),
                    ("autoShowBioPlot", "AutoShowSurfaceRadar"),
                    ("autoShowPlotMiniTrack_TEST", "AutoShowMiniTrack"),
                    ("bioPlotSize", "SurfaceRadarSize"),
                    ("autoHideBioPlotNoGear", "AutoHideSurfaceRadarWithoutLandingGear"),
                    ("autoRemoveTrackerOnSampling", "AutoRemoveTrackerOnSampling"),
                    ("autoRemoveTrackerOnFinalSample", "AutoRemoveTrackerOnFinalSample"),
                    ("autoTrackCompBioScans", "AutoTrackCompositionScans"),
                    ("skipAnalyzedCompBioScans", "SkipAnalyzedCompositionScans"),
                    ("drawBodyBiosOnlyWhenNear", "DrawBodyBiosOnlyWhenNear"),
                    ("highlightRegionalFirsts", "HighlightRegionalFirsts"),
                    ("dimIfAnalyzed", "DimAnalyzedOrganisms"),
                    ("hideGeoCountInBioSystem", "HideGeoCountInBioSystem"),
                    ("disableBioPredictions", "DisableBioPredictions"),
                    ("tempRange_TEST", "ShowTemperatureRangeDebug"),
                    ("autoShowPlotFSS", "AutoShowLastFssBody"),
                    ("autoShowPlotFSSInfo", "AutoShowFssInfo"),
                    ("autoShowPlotFSSInfoInSystemMap", "ShowFssInfoInSystemMap"),
                    ("autoShowPlotFSSInfoInNavPanel", "ShowFssInfoInNavigationPanel"),
                    ("autoShowPlotSysStatus", "AutoShowSystemStatus"),
                    ("hideGeoCountInFssInfo", "HideGeoCount"),
                    ("hideFssLowValueAmount", "FssBodyValueFloor"),
                    ("skipLowValueDSS", "HighlightDssCandidates"),
                    ("skipLowValueAmount", "DssValueFloor"),
                    ("skipHighDistanceDSS", "SkipDistantDssCandidates"),
                    ("skipHighDistanceDSSValue", "DssDistanceLimitLs"),
                    ("skipGasGiantDSS", "SkipGasGiantsForDss"),
                    ("skipRingsDSS", "SkipRingsForDss"),
                    ("showNonBodySignals", "ShowNonBodySignals"),
                    (BuildProjectsSuppressOtherOverlaysKey, SuppressForActiveBuildProjectsProperty),
                ]);
                mappedCount += MapFssTuningDetector(legacy, root);
                mappedCount += MapFirstFootfallInference(legacy, root);
                mappedCount += MapSection(legacy, root, "BiologyPredictions",
                [
                    ("formPredictionsCurrentBodyOnly", "CurrentBodyOnly", 0),
                    ("formPredictionsRowFontSize", "RowSize", 1),
                ]);
                mappedCount += MapSection(legacy, root, "BiologyRewards",
                [
                    ("bioRingBucketOne", "BucketOneMillions"),
                    ("bioRingBucketTwo", "BucketTwoMillions"),
                    ("bioRingBucketThree", "BucketThreeMillions"),
                ]);
                mappedCount += MapSection(legacy, root, "Combat",
                [
                    ("autoShowFootCombat_TEST", "AutoShowFootCombat"),
                    ("autoShowPlotMassacre_TEST", "AutoShowMassacreMissions"),
                    (BuildProjectsSuppressOtherOverlaysKey, SuppressForActiveBuildProjectsProperty),
                ]);
                mappedCount += MapSection(legacy, root, "GuardianOverlays",
                [
                    ("enableGuardianSites", "EnableGuardianSites"),
                    ("autoShowGuardianSummary", "AutoShowGuardianSummary"),
                    ("autoShowRamTah", "AutoShowRamTah"),
                    (BuildProjectsSuppressOtherOverlaysKey, SuppressForActiveBuildProjectsProperty),
                    ("autoZoomGuardianNearObelisks", "AutoZoomNearObelisks"),
                    ("autoZoomGuardianInTurret", "AutoZoomInSrvTurret"),
                    ("guardianComponentMaterials_TEST", "ShowComponentMaterials"),
                    ("idxGuardianPlotter", "OverlaySizeIndex"),
                    ("disableRuinsMeasurementGrid", "DisableRuinsMeasurementGrid"),
                    ("disableAerialAlignmentGrid", "DisableAerialAlignmentGrid"),
                    ("mapShowNotes", "ShowMapNotes"),
                    ("mapShowLegend", "ShowMapLegend"),
                ]);
                mappedCount += MapSection(legacy, root, "GuardianGestures",
                [
                    ("blinkTigger", "BlinkTrigger"),
                    ("blinkDuration", "BlinkDurationMilliseconds"),
                ]);
                mappedCount += MapSection(legacy, root, "HumanSite",
                [
                    ("autoShowHumanSitesTest", "AutoShow"),
                    ("plotHumanSiteWidth", "Width"),
                    ("plotHumanSiteHeight", "Height"),
                    ("humanSiteZoomShip", "ShipZoom"),
                    ("humanSiteZoomSRV", "SrvZoom"),
                    ("humanSiteZoomFoot", "FootZoom"),
                    ("humanSiteAutoZoomInside", "AutoZoomInside"),
                    ("humanSiteZoomInside", "InsideZoom"),
                    ("humanSiteAutoZoomTool", "AutoZoomTool"),
                    ("humanSiteZoomTool", "ToolZoom"),
                    ("humanSiteShow_Medkit", "ShowMedkits"),
                    ("humanSiteShow_Battery", "ShowBatteries"),
                    ("humanSiteShow_DataTerminal", "ShowDataTerminals"),
                    ("humanSiteDotsOnCollection", "ShowCollectedMaterials"),
                    ("collectMatsCollectionStatsTest", "TrackMaterialCollection"),
                    (BuildProjectsSuppressOtherOverlaysKey, SuppressForActiveBuildProjectsProperty),
                ]);
                mappedCount += MapSection(legacy, root, "StationInfo",
                [
                    ("autoShowPlotStationInfo_TEST", "AutoShow"),
                ]);
                mappedCount += MapSection(legacy, root, "SystemNicknames",
                [
                    ("useSystemNickNames", "Enabled"),
                ]);
                mappedCount += MapSection(legacy, root, "Quests",
                [
                    ("enableQuests", "Enabled"),
                ]);
                mappedCount += MapSection(legacy, root, "Screenshots",
                [
                    ("processScreenshots", "Enabled"),
                    ("addBannerToScreenshots", "AddBanner"),
                    ("deleteScreenshotOriginal", "DeleteOriginal"),
                    ("useGuardianAerialScreenshotsFolder", "UseGuardianAerialFolder"),
                    ("screenshotSourceFolder", "SourceFolder"),
                    ("screenshotTargetFolder", "TargetFolder"),
                    ("rotateAndTruncateAlphaAerialScreenshots", "RotateAlphaAerial"),
                    ("screenshotBannerColor", "BannerColor"),
                    ("screenshotBannerLocalTime", "BannerLocalTime"),
                    ("aerialAltAlpha", "AerialAltitudeAlpha"),
                    ("aerialAltBeta", "AerialAltitudeBeta"),
                    ("aerialAltGamma", "AerialAltitudeGamma"),
                ]);
                mappedCount += MapNotifications(legacy, root);
                mappedCount += MapColonization(legacy, root);
                mappedCount += MapSection(legacy, root, "Streaming",
                [
                    ("streamOneOverlay", "JoinedOverlayEnabled"),
                ]);
                mappedCount += MapSection(legacy, root, "VirtualReality",
                [
                    ("displayVR", "Enabled"),
                    ("vrProcessName", "RuntimeProcessName"),
                ]);
                mappedCount += MapSection(legacy, root, "NetworkPrivacy",
                [
                    ("eddnUpload", "EddnUploadEnabled"),
                    ("uploadGGG", "UploadGreenGasGiantCandidates"),
                ]);
                mappedCount += MapLegacyEddnSchemaMode(legacy, root);
                mappedCount += MapSection(legacy, root, "Inara",
                [
                    ("inaraUpload", "UploadEnabled"),
                    ("inaraDeveloperTestMode", "DeveloperTestMode"),
                ]);
                mappedCount += MapSection(legacy, root, "Localization",
                [
                    ("lang", "Language"),
                ]);
                mappedCount += MapCodexImages(legacy, root, manifest);
                mappedCount += MapSection(legacy, root, "Travel",
                [
                    ("logDockToDockTimes", "LogDockToDockTimes"),
                ]);
                mappedCount += MapInput(legacy, root);
                root["LegacyImport"] = new JsonObject
                {
                    ["ManifestVersion"] = manifest.Version,
                    ["ImportedAtUtc"] = manifest.ImportedAtUtc,
                    ["SourceDirectory"] = manifest.SourceDirectory,
                    ["MappedPreferenceCount"] = mappedCount,
                };
            });

            return new LegacyUiSettingsMigrationResult(
                true,
                mappedCount,
                backupPath,
                null);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidDataException
                or InvalidOperationException)
        {
            return new LegacyUiSettingsMigrationResult(
                false,
                0,
                null,
                exception.Message);
        }
    }

    private static int MapTheme(JsonObject legacy, JsonObject target)
    {
        if (!TryGetBoolean(legacy, "darkTheme", out var dark))
        {
            return 0;
        }

        var black = TryGetBoolean(legacy, "themeMainBlack", out var blackValue)
            && blackValue;
        target["Theme"] = black ? "orange-dark" : dark ? "blue-dark" : "blue-light";
        return 1;
    }

    private static LegacyUiSettingsMigrationResult
        MigrateNewPreferencesIfMissing(
            JsonObject legacy,
            JsonObject existing,
            UiSettingsDocumentStore store)
    {
        var mappings = new (string Section, string Legacy, string Current)[]
        {
            ("CommanderPreference", "preferredCommander", "PreferredCommanderName"),
            ("GuardianGestures", "blinkTigger", "BlinkTrigger"),
            ("GuardianGestures", "blinkDuration", "BlinkDurationMilliseconds"),
            ("FirstFootfallInference", "inferTolerance", "Tolerance"),
            ("FirstFootfallInference", "inferThreshold", "Threshold"),
            ("OverlayBehavior", "hideMultiFloatie", "HideMultiGameCommanderOverlay"),
            ("RavenService", "buildProjectsUrl_TEST", "ServiceUri"),
            ("JumpInfo", "useLastUpdatedFromSpanshNotEDSM", "UseSpanshLastUpdated"),
            ("Inara", "inaraUpload", "UploadEnabled"),
            ("Inara", "inaraDeveloperTestMode", "DeveloperTestMode"),
        };
        var pending = mappings.Where(mapping =>
            legacy[mapping.Legacy] is not null
            && (existing[mapping.Section] is not JsonObject section
                || !section.ContainsKey(mapping.Current)))
            .ToArray();
        var shouldMapColor = legacy["inferColor"] is JsonObject
            && (existing["FirstFootfallInference"] is not JsonObject inference
                || !inference.ContainsKey("Color"));
        var shouldMapOverlayScale = legacy["plotterScale"] is not null
            && (existing["OverlayScale"] is not JsonObject overlayScale
                || !overlayScale.ContainsKey("Index"));
        if (pending.Length == 0
            && !shouldMapColor
            && !shouldMapOverlayScale)
        {
            return LegacyUiSettingsMigrationResult.NotRequired;
        }

        var mappedCount = 0;
        store.Update(root =>
        {
            foreach (var mapping in pending)
            {
                mappedCount += Copy(
                    legacy,
                    mapping.Legacy,
                    GetOrCreateObject(root, mapping.Section),
                    mapping.Current);
            }

            if (shouldMapColor)
            {
                mappedCount += MapFirstFootfallColor(legacy, root);
            }

            if (shouldMapOverlayScale)
            {
                mappedCount += MapOverlayScale(legacy, root);
            }
        });
        return mappedCount == 0
            ? LegacyUiSettingsMigrationResult.NotRequired
            : new LegacyUiSettingsMigrationResult(true, mappedCount, null, null);
    }

    private static int MapFirstFootfallInference(
        JsonObject legacy,
        JsonObject target)
    {
        var section = GetOrCreateObject(target, "FirstFootfallInference");
        var count = Copy(legacy, "inferTolerance", section, "Tolerance");
        count += Copy(legacy, "inferThreshold", section, "Threshold");
        count += MapFirstFootfallColor(legacy, target);
        return count;
    }

    private static int MapOverlayScale(
        JsonObject legacy,
        JsonObject target)
    {
        if (!TryGetOverlayScaleIndex(legacy["plotterScale"], out var index)
            || !OverlayScaleCatalog.IsSupported(index))
        {
            return 0;
        }

        GetOrCreateObject(target, "OverlayScale")["Index"] = index;
        return 1;
    }

    private static bool TryGetOverlayScaleIndex(
        JsonNode? node,
        out int index)
    {
        index = 0;
        if (node is not JsonValue value)
        {
            return false;
        }

        if (value.TryGetValue<int>(out index))
        {
            return true;
        }

        if (!value.TryGetValue<double>(out var numeric)
            || !double.IsFinite(numeric)
            || numeric != Math.Truncate(numeric)
            || numeric is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        index = (int)numeric;
        return true;
    }

    private static int MapFirstFootfallColor(
        JsonObject legacy,
        JsonObject target)
    {
        if (legacy["inferColor"] is not JsonObject source)
        {
            return 0;
        }

        var section = GetOrCreateObject(target, "FirstFootfallInference");
        var color = GetOrCreateObject(section, "Color");
        var count = Copy(source, "R", color, "Red");
        count += Copy(source, "G", color, "Green");
        count += Copy(source, "B", color, "Blue");
        return count;
    }

    private static int MapCodexImages(
        JsonObject legacy,
        JsonObject target,
        ProfileImportManifest manifest)
    {
        var section = GetOrCreateObject(target, "CodexImages");
        var count = Copy(
            legacy,
            "preDownloadCodexImages",
            section,
            "PreDownload");
        count += MapImportedDirectory(
            legacy,
            "downloadCodexImageFolder",
            section,
            "CacheDirectory",
            manifest,
            "codexImages");
        count += MapImportedDirectory(
            legacy,
            "localFloraFolder",
            section,
            "LocalFloraDirectory",
            manifest,
            null);
        return count;
    }

    private static int MapImportedDirectory(
        JsonObject source,
        string sourceName,
        JsonObject target,
        string targetName,
        ProfileImportManifest manifest,
        string? conventionalImportedDirectory)
    {
        if (source[sourceName] is not JsonValue value
            || !value.TryGetValue<string>(out var configuredPath)
            || string.IsNullOrWhiteSpace(configuredPath))
        {
            return 0;
        }

        target[targetName] = RelocateImportedDirectory(
            configuredPath,
            manifest,
            conventionalImportedDirectory);
        return 1;
    }

    private static string RelocateImportedDirectory(
        string configuredPath,
        ProfileImportManifest manifest,
        string? conventionalImportedDirectory)
    {
        var configured = configuredPath.Trim();
        var relative = GetImportedRelativePath(
            configured,
            manifest.SourceDirectory);
        if (relative is not null)
        {
            var relocated = Path.GetFullPath(Path.Combine(
                manifest.DestinationDirectory,
                relative));
            if (IsSameOrChildPath(relocated, manifest.DestinationDirectory))
            {
                return relocated;
            }
        }

        if (conventionalImportedDirectory is not null
            && string.Equals(
                GetCrossPlatformFileName(configured),
                conventionalImportedDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            var imported = Path.Combine(
                manifest.DestinationDirectory,
                conventionalImportedDirectory);
            if (Directory.Exists(imported))
            {
                return Path.GetFullPath(imported);
            }
        }

        return configured;
    }

    private static string? GetImportedRelativePath(
        string configuredPath,
        string sourceDirectory)
    {
        var configured = NormalizeDirectory(configuredPath);
        var source = NormalizeDirectory(sourceDirectory);
        if (string.Equals(configured, source, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var prefix = source + "/";
        if (!configured.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = configured[prefix.Length..];
        return string.Join(
            Path.DirectorySeparatorChar,
            relative.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
    }

    private static string NormalizeDirectory(string path)
    {
        return path.Trim()
            .Replace('\\', '/')
            .TrimEnd('/');
    }

    private static string GetCrossPlatformFileName(string path)
    {
        var normalized = NormalizeDirectory(path);
        var separator = normalized.LastIndexOf('/');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

    private static bool IsSameOrChildPath(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(fullPath, fullRoot, comparison)
            || fullPath.StartsWith(
                fullRoot + Path.DirectorySeparatorChar,
                comparison);
    }

    private static int MapFssTuningDetector(
        JsonObject legacy,
        JsonObject target)
    {
        if (!legacy.TryGetPropertyValue(
                "watchFssSettings_TEST",
                out var legacyDetector))
        {
            return 0;
        }

        var systemSurvey = GetOrCreateObject(target, "SystemSurvey");
        var detector = GetOrCreateObject(
            systemSurvey,
            "FssTuningDetector");
        if (legacyDetector is not JsonObject source)
        {
            detector["Enabled"] = false;
            return 1;
        }

        detector["Enabled"] = true;
        var count = 1;
        count += Copy(
            source,
            "saveDebugImages",
            detector,
            "SaveDiagnosticImages");
        count += Copy(
            source,
            "yellowHorizontalTolerance",
            detector,
            "YellowHorizontalTolerance");
        count += MapFssPixelColor(source, "yellowBar", detector, "YellowBar");
        count += MapFssPixelColor(source, "blackArea", detector, "BlackArea");
        count += MapFssPixelColor(source, "whiteText", detector, "WhiteText");
        count += MapFssPixelColor(source, "yellowText", detector, "YellowText");
        return count;
    }

    private static int MapFssPixelColor(
        JsonObject source,
        string sourceName,
        JsonObject target,
        string targetName)
    {
        if (source[sourceName] is not JsonObject watchColor
            || watchColor["color"] is not JsonObject color)
        {
            return 0;
        }

        var mapped = GetOrCreateObject(target, targetName);
        var count = Copy(watchColor, "t", mapped, "Tolerance");
        count += Copy(color, "R", mapped, "Red");
        count += Copy(color, "G", mapped, "Green");
        count += Copy(color, "B", mapped, "Blue");
        return count;
    }

    private static int MapColonization(JsonObject legacy, JsonObject target)
    {
        var count = 0;
        var section = GetOrCreateObject(target, "Colonization");
        count += Copy(legacy, "buildProjects_TEST", section, "Enabled");
        count += Copy(
            legacy,
            "buildProjectsTrackShipCargo",
            section,
            "ShipCargoPublishingEnabled");
        var overlay = GetOrCreateObject(section, "Overlay");
        count += Copy(legacy, "autoShowPlotBuildCommodities", overlay, "AutoShow");
        count += Copy(legacy, "buildProjectsOnRightScreen", overlay, "ShowOnRightPanel");
        count += Copy(legacy, "buildProjectsShowSumFC_TEST", overlay, "ShowFleetCarrierCargo");
        count += Copy(legacy, "buildProjectsShowSumFCDelta_TEST", overlay, "ShowFleetCarrierDelta");
        count += Copy(legacy, "buildProjectsInlineSumFC_TEST", overlay, "InlineFleetCarrierCargo");
        count += Copy(legacy, "buildProjectsCollapseGroupsWithFCEnough_TEST", overlay, "CollapseCoveredGroups");
        count += Copy(legacy, "buildProjectsHighlightAlmostFC_TEST", overlay, "HighlightAlmostCoveredFleetCarrierLoads");
        return count;
    }

    private static int MapNotifications(JsonObject legacy, JsonObject target)
    {
        var section = GetOrCreateObject(target, "Notifications");
        var count = Copy(legacy, "autoShowFloatie_TEST", section, "Enabled");
        if (legacy["allowNotifications"] is not JsonObject notifications)
        {
            return count;
        }

        count += Copy(
            notifications,
            "materialCountAfterPickup",
            section,
            "MaterialCountAfterPickup");
        count += Copy(
            notifications,
            "cargoMissionRemaining",
            section,
            "CargoMissionRemaining");
        count += Copy(
            notifications,
            "currentBoxelSearchStatus",
            section,
            "CurrentBoxelSearchStatus");
        count += Copy(
            notifications,
            "showNextBoxelToSearch",
            section,
            "ShowNextBoxelToSearch");
        count += Copy(
            notifications,
            "showScreenshot",
            section,
            "ShowScreenshot");
        return count;
    }

    private static int MapInput(JsonObject legacy, JsonObject target)
    {
        var count = 0;
        var input = GetOrCreateObject(target, "Input");
        count += Copy(legacy, "keyhook_TEST", input, "KeyboardEnabled");
        count += Copy(legacy, "hookDirectX_TEST", input, "ControllerEnabled");
        count += Copy(legacy, "hookDirectXDeviceId_TEST", input, "ControllerDeviceId");
        if (legacy["keyActions_TEST"] is JsonObject bindings)
        {
            var targetBindings = GetOrCreateObject(input, "Bindings");
            foreach (var binding in bindings)
            {
                targetBindings[binding.Key] = binding.Value?.DeepClone();
                count++;
            }
        }

        return count;
    }

    private static int MapPulseOverlay(JsonObject legacy, JsonObject target)
    {
        if (!TryGetBoolean(legacy, "hideJournalWriteTimer", out var hidden))
        {
            return 0;
        }

        GetOrCreateObject(target, "PulseOverlay")["Enabled"] = !hidden;
        return 1;
    }

    private static int MapSection(
        JsonObject legacy,
        JsonObject target,
        string sectionName,
        IReadOnlyList<(string Legacy, string Current, int Offset)> mappings)
    {
        var section = GetOrCreateObject(target, sectionName);
        var count = 0;
        foreach (var mapping in mappings)
        {
            count += Copy(
                legacy,
                mapping.Legacy,
                section,
                mapping.Current,
                mapping.Offset);
        }

        return count;
    }

    private static int MapSection(
        JsonObject legacy,
        JsonObject target,
        string sectionName,
        IReadOnlyList<(string Legacy, string Current)> mappings)
    {
        return MapSection(
            legacy,
            target,
            sectionName,
            mappings.Select(mapping => (mapping.Legacy, mapping.Current, 0)).ToArray());
    }

    private static int MapLegacyEddnSchemaMode(
        JsonObject legacy,
        JsonObject target)
    {
        if (legacy["eddnEnvironment"] is not JsonValue value
            || !value.TryGetValue<string>(out var environment))
        {
            return 0;
        }

        GetOrCreateObject(target, "NetworkPrivacy")["EddnUseTestSchemas"] =
            !string.Equals(
                environment?.Trim(),
                "live",
                StringComparison.OrdinalIgnoreCase);
        return 1;
    }

    private static int Copy(
        JsonObject source,
        string sourceName,
        JsonObject target,
        string targetName,
        int numericOffset = 0)
    {
        if (source[sourceName] is not JsonNode value)
        {
            return 0;
        }

        if (numericOffset != 0
            && value is JsonValue numeric
            && numeric.TryGetValue<int>(out var number))
        {
            target[targetName] = number + numericOffset;
        }
        else
        {
            target[targetName] = value.DeepClone();
        }

        return 1;
    }

    private static JsonObject GetOrCreateObject(JsonObject root, string name)
    {
        if (root[name] is JsonObject value)
        {
            return value;
        }

        value = [];
        root[name] = value;
        return value;
    }

    private static bool TryGetBoolean(
        JsonObject root,
        string name,
        out bool result)
    {
        result = false;
        return root[name] is JsonValue value
            && value.TryGetValue(out result);
    }

    private static bool HasMigrationMarker(
        JsonObject settings,
        ProfileImportManifest manifest)
    {
        return settings["LegacyImport"] is JsonObject marker
            && marker["ImportedAtUtc"] is JsonValue importedAt
            && importedAt.TryGetValue<DateTimeOffset>(out var value)
            && value == manifest.ImportedAtUtc;
    }

    private static string? BackupExistingSettings(
        string settingsPath,
        ProfileImportManifest manifest)
    {
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        Directory.CreateDirectory(manifest.BackupDirectory);
        var backupPath = Path.Combine(manifest.BackupDirectory, BackupFileName);
        if (!File.Exists(backupPath))
        {
            File.Copy(settingsPath, backupPath, false);
        }

        var sourceHash = ComputeSha256(settingsPath);
        var backupHash = ComputeSha256(backupPath);
        if (!string.Equals(sourceHash, backupHash, StringComparison.Ordinal))
        {
            throw new IOException(
                "The current Avalonia settings backup did not match its source.");
        }

        return backupPath;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

public sealed record LegacyUiSettingsMigrationResult(
    bool Migrated,
    int MappedPreferenceCount,
    string? PreviousSettingsBackupPath,
    string? Error)
{
    public static LegacyUiSettingsMigrationResult NotRequired { get; } =
        new(false, 0, null, null);
}
