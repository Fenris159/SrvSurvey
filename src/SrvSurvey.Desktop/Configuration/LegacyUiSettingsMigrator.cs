using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.Configuration;

public sealed class LegacyUiSettingsMigrator
{
    private const string BuildProjectsSuppressOtherOverlaysKey = "buildProjectsSuppressOtherOverlays";
    private const string SuppressForActiveBuildProjectsProperty = "SuppressForActiveBuildProjects";
    private const string FirstFootfallInferenceSection = "FirstFootfallInference";
    private const string EnabledProperty = "Enabled";
    private const string AutoShowProperty = "AutoShow";

    public const string BackupFileName = "previous-cross-platform-ui.json";

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Migration is exposed as a replaceable service instance."
    )]
    [SuppressMessage(
        "Maintainability",
        "S2325:Make methods and properties static",
        Justification = "Migration is exposed as a replaceable service instance."
    )]
    public LegacyUiSettingsMigrationResult MigrateIfNeeded(AppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string manifestPath = Path.Combine(paths.DataDirectory, LegacyProfileImporter.ManifestFileName);
        string legacySettingsPath = Path.Combine(paths.DataDirectory, "settings.json");
        if (!File.Exists(manifestPath) || !File.Exists(legacySettingsPath))
        {
            return LegacyUiSettingsMigrationResult.NotRequired;
        }

        try
        {
            ProfileImportManifest manifest =
                JsonSerializer.Deserialize<ProfileImportManifest>(File.ReadAllText(manifestPath))
                ?? throw new InvalidDataException("The legacy import manifest is empty.");
            JsonObject legacy =
                JsonNode.Parse(File.ReadAllText(legacySettingsPath)) as JsonObject
                ?? throw new InvalidDataException("The imported legacy settings file is not a JSON object.");
            var store = new UiSettingsDocumentStore(paths.UiSettingsPath);
            JsonObject existing = store.Load();
            if (HasMigrationMarker(existing, manifest))
            {
                return MigrateNewPreferencesIfMissing(legacy, existing, store);
            }

            string expectedBackupPath = Path.Combine(manifest.BackupDirectory, BackupFileName);
            string completionSignalPath = Path.Combine(
                manifest.BackupDirectory,
                CrossPlatformUiSettingsImporter.CompletionSignalFileName
            );
            if (File.Exists(completionSignalPath))
            {
                // Current-profile imports activate a complete Avalonia UI
                // document after creating this verified backup. Preserve that
                // imported document and only fill preferences introduced
                // since it was written.
                string? completedImportBackupPath = File.Exists(expectedBackupPath) ? expectedBackupPath : null;
                return MigrateNewPreferencesIfMissing(legacy, existing, store, manifest, completedImportBackupPath);
            }

            string? backupPath = BackupExistingSettings(paths.UiSettingsPath, manifest);
            int mappedCount = 0;
            store.Update(root =>
            {
                root["Version"] = 1;
                mappedCount += MapTheme(legacy, root);
                mappedCount += MapSection(
                    legacy,
                    root,
                    "JumpInfo",
                    [
                        ("autoShowPlotJumpInfo", AutoShowProperty),
                        ("plotJumpInfoMinimal", "Minimal"),
                        ("showPlotJumpInfoIfNextHop", "ShowWhenNextHopSelected"),
                        ("useLastUpdatedFromSpanshNotEDSM", "UseSpanshLastUpdated"),
                    ]
                );
                mappedCount += MapSection(
                    legacy,
                    root,
                    "GalaxyMap",
                    [("autoShowPlotGalMap", AutoShowProperty), ("galMapFactions", "ShowFactions")]
                );
                mappedCount += MapPulseOverlay(legacy, root);
                mappedCount += MapSection(
                    legacy,
                    root,
                    "OverlayBehavior",
                    [
                        ("keepOverlays", "KeepWhenGameLosesFocus"),
                        ("hidePlottersFromCombatSuits", "HideInDominatorSuit"),
                        ("hidePlottersFromMaverickSuits", "HideInMaverickSuit"),
                        ("hideMultiFloatie", "HideMultiGameCommanderOverlay"),
                    ]
                );
                mappedCount += MapOverlayScale(legacy, root);
                mappedCount += MapSection(
                    legacy,
                    root,
                    "DesktopBehavior",
                    [
                        ("focusGameOnStart", "FocusGameOnStart"),
                        ("focusGameOnMinimize", "FocusGameOnMinimize"),
                        ("focusGameAfterFsdJump", "FocusGameAfterFsdJump"),
                        ("minimizeToTray", "MinimizeToTray"),
                    ]
                );
                mappedCount += MapSection(
                    legacy,
                    root,
                    "CommanderPreference",
                    [("preferredCommander", "PreferredCommanderName")]
                );
                mappedCount += MapSection(legacy, root, "Journal", [("watchedJournalFolder", "Directory")]);
                mappedCount += MapSection(legacy, root, "RavenService", [("buildProjectsUrl_TEST", "ServiceUri")]);
                mappedCount += MapSection(
                    legacy,
                    root,
                    "SystemSurvey",
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
                    ]
                );
                mappedCount += MapFssTuningDetector(legacy, root);
                mappedCount += MapFirstFootfallInference(legacy, root);
                mappedCount += MapSection(
                    legacy,
                    root,
                    "BiologyPredictions",
                    [
                        ("formPredictionsCurrentBodyOnly", "CurrentBodyOnly", 0),
                        ("formPredictionsRowFontSize", "RowSize", 1),
                    ]
                );
                mappedCount += MapSection(
                    legacy,
                    root,
                    "BiologyRewards",
                    [
                        ("bioRingBucketOne", "BucketOneMillions"),
                        ("bioRingBucketTwo", "BucketTwoMillions"),
                        ("bioRingBucketThree", "BucketThreeMillions"),
                    ]
                );
                mappedCount += MapSection(
                    legacy,
                    root,
                    "Combat",
                    [
                        ("autoShowFootCombat_TEST", "AutoShowFootCombat"),
                        ("autoShowPlotMassacre_TEST", "AutoShowMassacreMissions"),
                        (BuildProjectsSuppressOtherOverlaysKey, SuppressForActiveBuildProjectsProperty),
                    ]
                );
                mappedCount += MapSection(
                    legacy,
                    root,
                    "GuardianOverlays",
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
                    ]
                );
                mappedCount += MapSection(
                    legacy,
                    root,
                    "GuardianGestures",
                    [("blinkTigger", "BlinkTrigger"), ("blinkDuration", "BlinkDurationMilliseconds")]
                );
                mappedCount += MapSection(
                    legacy,
                    root,
                    "HumanSite",
                    [
                        ("autoShowHumanSitesTest", AutoShowProperty),
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
                    ]
                );
                mappedCount += MapSection(
                    legacy,
                    root,
                    "StationInfo",
                    [("autoShowPlotStationInfo_TEST", AutoShowProperty)]
                );
                mappedCount += MapSection(legacy, root, "SystemNicknames", [("useSystemNickNames", EnabledProperty)]);
                mappedCount += MapSection(legacy, root, "Quests", [("enableQuests", EnabledProperty)]);
                mappedCount += MapSection(
                    legacy,
                    root,
                    "Screenshots",
                    [
                        ("processScreenshots", EnabledProperty),
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
                    ]
                );
                mappedCount += MapNotifications(legacy, root);
                mappedCount += MapColonization(legacy, root);
                mappedCount += MapSection(legacy, root, "Streaming", [("streamOneOverlay", "JoinedOverlayEnabled")]);
                mappedCount += MapSection(
                    legacy,
                    root,
                    "VirtualReality",
                    [("displayVR", EnabledProperty), ("vrProcessName", "RuntimeProcessName")]
                );
                mappedCount += MapSection(
                    legacy,
                    root,
                    "NetworkPrivacy",
                    [("eddnUpload", "EddnUploadEnabled"), ("uploadGGG", "UploadGreenGasGiantCandidates")]
                );
                mappedCount += MapSection(legacy, root, "Localization", [("lang", "Language")]);
                mappedCount += MapCodexImages(legacy, root, manifest);
                mappedCount += MapSection(legacy, root, "Travel", [("logDockToDockTimes", "LogDockToDockTimes")]);
                mappedCount += MapInput(legacy, root);
                WriteMigrationMarker(root, manifest, mappedCount);
            });

            return new LegacyUiSettingsMigrationResult(true, mappedCount, backupPath, null);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or InvalidDataException
                        or InvalidOperationException
            )
        {
            return new LegacyUiSettingsMigrationResult(false, 0, null, exception.Message);
        }
    }

    private static int MapTheme(JsonObject legacy, JsonObject target)
    {
        if (!TryGetBoolean(legacy, "darkTheme", out bool dark))
        {
            return 0;
        }

        bool black = TryGetBoolean(legacy, "themeMainBlack", out bool blackValue) && blackValue;
        target["Theme"] = black
            ? "orange-dark"
            : (dark) switch
            {
                true => "blue-dark",
                false => "blue-light",
            };
        return 1;
    }

    private static LegacyUiSettingsMigrationResult MigrateNewPreferencesIfMissing(
        JsonObject legacy,
        JsonObject existing,
        UiSettingsDocumentStore store,
        ProfileImportManifest? manifest = null,
        string? backupPath = null
    )
    {
        var mappings = new (string Section, string Legacy, string Current)[]
        {
            ("CommanderPreference", "preferredCommander", "PreferredCommanderName"),
            ("GuardianGestures", "blinkTigger", "BlinkTrigger"),
            ("GuardianGestures", "blinkDuration", "BlinkDurationMilliseconds"),
            (FirstFootfallInferenceSection, "inferTolerance", "Tolerance"),
            (FirstFootfallInferenceSection, "inferThreshold", "Threshold"),
            ("OverlayBehavior", "hideMultiFloatie", "HideMultiGameCommanderOverlay"),
            ("RavenService", "buildProjectsUrl_TEST", "ServiceUri"),
            ("JumpInfo", "useLastUpdatedFromSpanshNotEDSM", "UseSpanshLastUpdated"),
        };
        (string Section, string Legacy, string Current)[] pending = mappings
            .Where(mapping =>
                legacy[mapping.Legacy] is not null
                && (existing[mapping.Section] is not JsonObject section || !section.ContainsKey(mapping.Current))
            )
            .ToArray();
        bool shouldMapColor =
            legacy["inferColor"] is JsonObject
            && (existing[FirstFootfallInferenceSection] is not JsonObject inference || !inference.ContainsKey("Color"));
        bool shouldMapOverlayScale =
            legacy["plotterScale"] is not null
            && (existing["OverlayScale"] is not JsonObject overlayScale || !overlayScale.ContainsKey("Index"));
        if (pending.Length == 0 && !shouldMapColor && !shouldMapOverlayScale && manifest is null)
        {
            return LegacyUiSettingsMigrationResult.NotRequired;
        }

        int mappedCount = 0;
        store.Update(root =>
            mappedCount = MapNewPreferences(legacy, root, pending, shouldMapColor, shouldMapOverlayScale, manifest)
        );
        return mappedCount == 0 && manifest is null
            ? LegacyUiSettingsMigrationResult.NotRequired
            : new LegacyUiSettingsMigrationResult(true, mappedCount, backupPath, null);
    }

    private static int MapNewPreferences(
        JsonObject legacy,
        JsonObject root,
        IReadOnlyList<(string Section, string Legacy, string Current)> pending,
        bool shouldMapColor,
        bool shouldMapOverlayScale,
        ProfileImportManifest? manifest
    )
    {
        int mappedCount = 0;
        foreach ((string Section, string Legacy, string Current) mapping in pending)
        {
            mappedCount += Copy(legacy, mapping.Legacy, GetOrCreateObject(root, mapping.Section), mapping.Current);
        }

        if (shouldMapColor)
        {
            mappedCount += MapFirstFootfallColor(legacy, root);
        }

        if (shouldMapOverlayScale)
        {
            mappedCount += MapOverlayScale(legacy, root);
        }

        if (manifest is not null)
        {
            WriteMigrationMarker(root, manifest, mappedCount);
        }

        return mappedCount;
    }

    private static void WriteMigrationMarker(JsonObject root, ProfileImportManifest manifest, int mappedCount)
    {
        root["LegacyImport"] = new JsonObject
        {
            ["ManifestVersion"] = manifest.Version,
            ["ImportedAtUtc"] = manifest.ImportedAtUtc,
            ["SourceDirectory"] = manifest.SourceDirectory,
            ["MappedPreferenceCount"] = mappedCount,
        };
    }

    private static int MapFirstFootfallInference(JsonObject legacy, JsonObject target)
    {
        JsonObject section = GetOrCreateObject(target, FirstFootfallInferenceSection);
        int count = Copy(legacy, "inferTolerance", section, "Tolerance");
        count += Copy(legacy, "inferThreshold", section, "Threshold");
        count += MapFirstFootfallColor(legacy, target);
        return count;
    }

    private static int MapOverlayScale(JsonObject legacy, JsonObject target)
    {
        if (!TryGetOverlayScaleIndex(legacy["plotterScale"], out int index) || !OverlayScaleCatalog.IsSupported(index))
        {
            return 0;
        }

        GetOrCreateObject(target, "OverlayScale")["Index"] = index;
        return 1;
    }

    private static bool TryGetOverlayScaleIndex(JsonNode? node, out int index)
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

        if (
            !value.TryGetValue<double>(out double numeric)
            || !double.IsFinite(numeric)
            || !double.IsInteger(numeric)
            || numeric is < int.MinValue or > int.MaxValue
        )
        {
            return false;
        }

        index = (int)numeric;
        return true;
    }

    private static int MapFirstFootfallColor(JsonObject legacy, JsonObject target)
    {
        if (legacy["inferColor"] is not JsonObject source)
        {
            return 0;
        }

        JsonObject section = GetOrCreateObject(target, FirstFootfallInferenceSection);
        JsonObject color = GetOrCreateObject(section, "Color");
        int count = Copy(source, "R", color, "Red");
        count += Copy(source, "G", color, "Green");
        count += Copy(source, "B", color, "Blue");
        return count;
    }

    private static int MapCodexImages(JsonObject legacy, JsonObject target, ProfileImportManifest manifest)
    {
        JsonObject section = GetOrCreateObject(target, "CodexImages");
        int count = Copy(legacy, "preDownloadCodexImages", section, "PreDownload");
        count += MapImportedDirectory(
            legacy,
            "downloadCodexImageFolder",
            section,
            "CacheDirectory",
            manifest,
            "codexImages"
        );
        count += MapImportedDirectory(legacy, "localFloraFolder", section, "LocalFloraDirectory", manifest, null);
        return count;
    }

    private static int MapImportedDirectory(
        JsonObject source,
        string sourceName,
        JsonObject target,
        string targetName,
        ProfileImportManifest manifest,
        string? conventionalImportedDirectory
    )
    {
        if (
            source[sourceName] is not JsonValue value
            || !value.TryGetValue<string>(out string? configuredPath)
            || string.IsNullOrWhiteSpace(configuredPath)
        )
        {
            return 0;
        }

        target[targetName] = RelocateImportedDirectory(configuredPath, manifest, conventionalImportedDirectory);
        return 1;
    }

    private static string RelocateImportedDirectory(
        string configuredPath,
        ProfileImportManifest manifest,
        string? conventionalImportedDirectory
    )
    {
        string configured = configuredPath.Trim();
        string? relative = GetImportedRelativePath(configured, manifest.SourceDirectory);
        if (relative is not null)
        {
            string relocated = Path.GetFullPath(Path.Combine(manifest.DestinationDirectory, relative));
            if (IsSameOrChildPath(relocated, manifest.DestinationDirectory))
            {
                return relocated;
            }
        }

        if (
            conventionalImportedDirectory is not null
            && string.Equals(
                GetCrossPlatformFileName(configured),
                conventionalImportedDirectory,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            string imported = Path.Combine(manifest.DestinationDirectory, conventionalImportedDirectory);
            if (Directory.Exists(imported))
            {
                return Path.GetFullPath(imported);
            }
        }

        return configured;
    }

    private static string? GetImportedRelativePath(string configuredPath, string sourceDirectory)
    {
        string configured = NormalizeDirectory(configuredPath);
        string source = NormalizeDirectory(sourceDirectory);
        if (string.Equals(configured, source, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        string prefix = source + "/";
        if (!configured.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string relative = configured[prefix.Length..];
        return string.Join(
            Path.DirectorySeparatorChar,
            relative.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        );
    }

    private static string NormalizeDirectory(string path)
    {
        return path.Trim().Replace('\\', '/').TrimEnd('/');
    }

    private static string GetCrossPlatformFileName(string path)
    {
        string normalized = NormalizeDirectory(path);
        int separator = normalized.LastIndexOf('/');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

    private static bool IsSameOrChildPath(string path, string root)
    {
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(fullPath, fullRoot, comparison)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static int MapFssTuningDetector(JsonObject legacy, JsonObject target)
    {
        if (!legacy.TryGetPropertyValue("watchFssSettings_TEST", out JsonNode? legacyDetector))
        {
            return 0;
        }

        JsonObject systemSurvey = GetOrCreateObject(target, "SystemSurvey");
        JsonObject detector = GetOrCreateObject(systemSurvey, "FssTuningDetector");
        if (legacyDetector is not JsonObject source)
        {
            detector[EnabledProperty] = false;
            return 1;
        }

        detector[EnabledProperty] = true;
        int count = 1;
        count += Copy(source, "saveDebugImages", detector, "SaveDiagnosticImages");
        count += Copy(source, "yellowHorizontalTolerance", detector, "YellowHorizontalTolerance");
        count += MapFssPixelColor(source, "yellowBar", detector, "YellowBar");
        count += MapFssPixelColor(source, "blackArea", detector, "BlackArea");
        count += MapFssPixelColor(source, "whiteText", detector, "WhiteText");
        count += MapFssPixelColor(source, "yellowText", detector, "YellowText");
        return count;
    }

    private static int MapFssPixelColor(JsonObject source, string sourceName, JsonObject target, string targetName)
    {
        if (source[sourceName] is not JsonObject watchColor || watchColor["color"] is not JsonObject color)
        {
            return 0;
        }

        JsonObject mapped = GetOrCreateObject(target, targetName);
        int count = Copy(watchColor, "t", mapped, "Tolerance");
        count += Copy(color, "R", mapped, "Red");
        count += Copy(color, "G", mapped, "Green");
        count += Copy(color, "B", mapped, "Blue");
        return count;
    }

    private static int MapColonization(JsonObject legacy, JsonObject target)
    {
        int count = 0;
        JsonObject section = GetOrCreateObject(target, "Colonization");
        count += Copy(legacy, "buildProjects_TEST", section, EnabledProperty);
        count += Copy(legacy, "buildProjectsTrackShipCargo", section, "ShipCargoPublishingEnabled");
        JsonObject overlay = GetOrCreateObject(section, "Overlay");
        count += Copy(legacy, "autoShowPlotBuildCommodities", overlay, AutoShowProperty);
        count += Copy(legacy, "buildProjectsOnRightScreen", overlay, "ShowOnRightPanel");
        count += Copy(legacy, "buildProjectsShowSumFC_TEST", overlay, "ShowFleetCarrierCargo");
        count += Copy(legacy, "buildProjectsShowSumFCDelta_TEST", overlay, "ShowFleetCarrierDelta");
        count += Copy(legacy, "buildProjectsInlineSumFC_TEST", overlay, "InlineFleetCarrierCargo");
        count += Copy(legacy, "buildProjectsCollapseGroupsWithFCEnough_TEST", overlay, "CollapseCoveredGroups");
        count += Copy(
            legacy,
            "buildProjectsHighlightAlmostFC_TEST",
            overlay,
            "HighlightAlmostCoveredFleetCarrierLoads"
        );
        return count;
    }

    private static int MapNotifications(JsonObject legacy, JsonObject target)
    {
        JsonObject section = GetOrCreateObject(target, "Notifications");
        int count = Copy(legacy, "autoShowFloatie_TEST", section, EnabledProperty);
        if (legacy["allowNotifications"] is not JsonObject notifications)
        {
            return count;
        }

        count += Copy(notifications, "materialCountAfterPickup", section, "MaterialCountAfterPickup");
        count += Copy(notifications, "cargoMissionRemaining", section, "CargoMissionRemaining");
        count += Copy(notifications, "currentBoxelSearchStatus", section, "CurrentBoxelSearchStatus");
        count += Copy(notifications, "showNextBoxelToSearch", section, "ShowNextBoxelToSearch");
        count += Copy(notifications, "showScreenshot", section, "ShowScreenshot");
        return count;
    }

    private static int MapInput(JsonObject legacy, JsonObject target)
    {
        int count = 0;
        JsonObject input = GetOrCreateObject(target, "Input");
        count += Copy(legacy, "keyhook_TEST", input, "KeyboardEnabled");
        count += Copy(legacy, "hookDirectX_TEST", input, "ControllerEnabled");
        count += Copy(legacy, "hookDirectXDeviceId_TEST", input, "ControllerDeviceId");
        if (legacy["keyActions_TEST"] is JsonObject bindings)
        {
            JsonObject targetBindings = GetOrCreateObject(input, "Bindings");
            foreach (KeyValuePair<string, JsonNode?> binding in bindings)
            {
                targetBindings[binding.Key] = binding.Value?.DeepClone();
                count++;
            }
        }

        return count;
    }

    private static int MapPulseOverlay(JsonObject legacy, JsonObject target)
    {
        if (!TryGetBoolean(legacy, "hideJournalWriteTimer", out bool hidden))
        {
            return 0;
        }

        GetOrCreateObject(target, "PulseOverlay")[EnabledProperty] = !hidden;
        return 1;
    }

    private static int MapSection(
        JsonObject legacy,
        JsonObject target,
        string sectionName,
        IReadOnlyList<(string Legacy, string Current, int Offset)> mappings
    )
    {
        JsonObject section = GetOrCreateObject(target, sectionName);
        int count = 0;
        foreach ((string Legacy, string Current, int Offset) mapping in mappings)
        {
            count += Copy(legacy, mapping.Legacy, section, mapping.Current, mapping.Offset);
        }

        return count;
    }

    private static int MapSection(
        JsonObject legacy,
        JsonObject target,
        string sectionName,
        IReadOnlyList<(string Legacy, string Current)> mappings
    )
    {
        return MapSection(
            legacy,
            target,
            sectionName,
            mappings.Select(mapping => (mapping.Legacy, mapping.Current, 0)).ToArray()
        );
    }

    private static int Copy(
        JsonObject source,
        string sourceName,
        JsonObject target,
        string targetName,
        int numericOffset = 0
    )
    {
        if (source[sourceName] is not JsonNode value)
        {
            return 0;
        }

        if (numericOffset != 0 && value is JsonValue numeric && numeric.TryGetValue<int>(out int number))
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

    private static bool TryGetBoolean(JsonObject root, string name, out bool result)
    {
        result = false;
        return root[name] is JsonValue value && value.TryGetValue(out result);
    }

    private static bool HasMigrationMarker(JsonObject settings, ProfileImportManifest manifest)
    {
        return settings["LegacyImport"] is JsonObject marker
            && marker["ImportedAtUtc"] is JsonValue importedAt
            && importedAt.TryGetValue<DateTimeOffset>(out DateTimeOffset value)
            && value == manifest.ImportedAtUtc;
    }

    private static string? BackupExistingSettings(string settingsPath, ProfileImportManifest manifest)
    {
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        Directory.CreateDirectory(manifest.BackupDirectory);
        string backupPath = Path.Combine(manifest.BackupDirectory, BackupFileName);
        string sourceHash = ComputeSha256(settingsPath);
        if (File.Exists(backupPath))
        {
            string existingBackupHash = ComputeSha256(backupPath);
            if (!string.Equals(sourceHash, existingBackupHash, StringComparison.Ordinal))
            {
                throw new IOException("The current Avalonia settings backup did not match its source.");
            }

            return backupPath;
        }

        File.Copy(settingsPath, backupPath, false);
        string backupHash = ComputeSha256(backupPath);
        if (!string.Equals(sourceHash, backupHash, StringComparison.Ordinal))
        {
            throw new IOException("The current Avalonia settings backup did not match its source.");
        }

        return backupPath;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

public sealed record LegacyUiSettingsMigrationResult(
    bool Migrated,
    int MappedPreferenceCount,
    string? PreviousSettingsBackupPath,
    string? Error
)
{
    public static LegacyUiSettingsMigrationResult NotRequired { get; } = new(false, 0, null, null);
}
