using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class SystemSurveySettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public SystemSurveySettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public SystemSurveyPreferences Load()
    {
        var root = documentStore.Load();
        var settings = root["SystemSurvey"] as JsonObject;
        var defaults = SystemSurveyPreferences.Default;
        return new SystemSurveyPreferences(
            GetBoolean(settings, "AutoShowBodyInfo", defaults.AutoShowBodyInfo),
            GetBoolean(
                settings,
                "ShowBodyInfoInSystemMap",
                defaults.ShowBodyInfoInSystemMap),
            GetBoolean(
                settings,
                "ShowBodyInfoInOrbit",
                defaults.ShowBodyInfoInOrbit),
            GetBoolean(
                settings,
                "ShowBodyInfoAtSurface",
                defaults.ShowBodyInfoAtSurface),
            GetBoolean(
                settings,
                "HideBodyInfoInBubble",
                defaults.HideBodyInfoInBubble),
            GetInt32(
                settings,
                "BodyInfoBubbleSizeLy",
                defaults.BodyInfoBubbleSizeLy,
                0),
            GetBoolean(
                settings,
                "HideBodyInfoMaterials",
                defaults.HideBodyInfoMaterials),
            GetBoolean(
                settings,
                "AutoShowFlightWarnings",
                defaults.AutoShowFlightWarnings),
            GetDouble(
                settings,
                "HighGravityWarningLevel",
                defaults.HighGravityWarningLevel,
                0,
                50),
            GetBoolean(
                settings,
                "UseExternalData",
                defaults.UseExternalData),
            GetBoolean(
                settings,
                "UseExternalBioData",
                defaults.UseExternalBioData),
            GetBoolean(
                settings,
                "AutoShowBioSystem",
                defaults.AutoShowBioSystem),
            GetBoolean(
                settings,
                "AutoShowBioStatus",
                defaults.AutoShowBioStatus),
            GetBoolean(
                settings,
                "AutoShowPriorScans",
                defaults.AutoShowPriorScans),
            GetBoolean(
                settings,
                "SkipPriorScansLowValue",
                defaults.SkipPriorScansLowValue),
            GetInt32(
                settings,
                "PriorScanMinimumValue",
                defaults.PriorScanMinimumValue,
                0),
            GetBoolean(
                settings,
                "HideOwnCanonnSignals",
                defaults.HideOwnCanonnSignals),
            GetBoolean(
                settings,
                "ShowCanonnSignalsOnRadar",
                defaults.ShowCanonnSignalsOnRadar),
            GetBoolean(
                settings,
                "UseSmallCanonnRadarCircles",
                defaults.UseSmallCanonnRadarCircles),
            GetBoolean(
                settings,
                "AutoShowSurfaceRadar",
                defaults.AutoShowSurfaceRadar),
            GetBoolean(
                settings,
                "AutoShowMiniTrack",
                defaults.AutoShowMiniTrack),
            GetInt32(
                settings,
                "SurfaceRadarSize",
                defaults.SurfaceRadarSize,
                0,
                4),
            GetBoolean(
                settings,
                "AutoHideSurfaceRadarWithoutLandingGear",
                defaults.AutoHideSurfaceRadarWithoutLandingGear),
            GetBoolean(
                settings,
                "AutoRemoveTrackerOnSampling",
                defaults.AutoRemoveTrackerOnSampling),
            GetBoolean(
                settings,
                "AutoRemoveTrackerOnFinalSample",
                defaults.AutoRemoveTrackerOnFinalSample),
            GetBoolean(
                settings,
                "AutoTrackCompositionScans",
                defaults.AutoTrackCompositionScans),
            GetBoolean(
                settings,
                "SkipAnalyzedCompositionScans",
                defaults.SkipAnalyzedCompositionScans),
            GetBoolean(
                settings,
                "DrawBodyBiosOnlyWhenNear",
                defaults.DrawBodyBiosOnlyWhenNear),
            GetBoolean(
                settings,
                "HighlightRegionalFirsts",
                defaults.HighlightRegionalFirsts),
            GetBoolean(
                settings,
                "DimAnalyzedOrganisms",
                defaults.DimAnalyzedOrganisms),
            GetBoolean(
                settings,
                "HideGeoCountInBioSystem",
                defaults.HideGeoCountInBioSystem),
            GetBoolean(
                settings,
                "DisableBioPredictions",
                defaults.DisableBioPredictions),
            GetBoolean(
                settings,
                "ShowTemperatureRangeDebug",
                defaults.ShowTemperatureRangeDebug),
            GetBoolean(
                settings,
                "AutoShowLastFssBody",
                defaults.AutoShowLastFssBody),
            GetBoolean(settings, "AutoShowFssInfo", defaults.AutoShowFssInfo),
            GetBoolean(
                settings,
                "ShowFssInfoInSystemMap",
                defaults.ShowFssInfoInSystemMap),
            GetBoolean(
                settings,
                "ShowFssInfoInNavigationPanel",
                defaults.ShowFssInfoInNavigationPanel),
            GetBoolean(
                settings,
                "AutoShowSystemStatus",
                defaults.AutoShowSystemStatus),
            GetBoolean(settings, "HideGeoCount", defaults.HideGeoCount),
            GetInt32(
                settings,
                "FssBodyValueFloor",
                defaults.FssBodyValueFloor,
                0),
            GetBoolean(
                settings,
                "HighlightDssCandidates",
                defaults.HighlightDssCandidates),
            GetInt32(
                settings,
                "DssValueFloor",
                defaults.DssValueFloor,
                0),
            GetBoolean(
                settings,
                "SkipDistantDssCandidates",
                defaults.SkipDistantDssCandidates),
            GetInt32(
                settings,
                "DssDistanceLimitLs",
                defaults.DssDistanceLimitLs,
                0),
            GetBoolean(
                settings,
                "SkipGasGiantsForDss",
                defaults.SkipGasGiantsForDss),
            GetBoolean(settings, "SkipRingsForDss", defaults.SkipRingsForDss),
            GetBoolean(
                settings,
                "ShowNonBodySignals",
                defaults.ShowNonBodySignals),
            GetFssTuningDetectorSettings(
                settings?["FssTuningDetector"] as JsonObject,
                defaults.FssTuningDetector));
    }

    public void Save(SystemSurveyPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["SystemSurvey"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["SystemSurvey"] = settings;
            }

            root["Version"] = 1;
            settings["AutoShowBodyInfo"] = preferences.AutoShowBodyInfo;
            settings["ShowBodyInfoInSystemMap"] =
                preferences.ShowBodyInfoInSystemMap;
            settings["ShowBodyInfoInOrbit"] = preferences.ShowBodyInfoInOrbit;
            settings["ShowBodyInfoAtSurface"] =
                preferences.ShowBodyInfoAtSurface;
            settings["HideBodyInfoInBubble"] =
                preferences.HideBodyInfoInBubble;
            settings["BodyInfoBubbleSizeLy"] = preferences.BodyInfoBubbleSizeLy;
            settings["HideBodyInfoMaterials"] =
                preferences.HideBodyInfoMaterials;
            settings["AutoShowFlightWarnings"] =
                preferences.AutoShowFlightWarnings;
            settings["HighGravityWarningLevel"] =
                preferences.HighGravityWarningLevel;
            settings["UseExternalData"] = preferences.UseExternalData;
            settings["UseExternalBioData"] = preferences.UseExternalBioData;
            settings["AutoShowBioSystem"] = preferences.AutoShowBioSystem;
            settings["AutoShowBioStatus"] = preferences.AutoShowBioStatus;
            settings["AutoShowPriorScans"] = preferences.AutoShowPriorScans;
            settings["SkipPriorScansLowValue"] =
                preferences.SkipPriorScansLowValue;
            settings["PriorScanMinimumValue"] =
                preferences.PriorScanMinimumValue;
            settings["HideOwnCanonnSignals"] =
                preferences.HideOwnCanonnSignals;
            settings["ShowCanonnSignalsOnRadar"] =
                preferences.ShowCanonnSignalsOnRadar;
            settings["UseSmallCanonnRadarCircles"] =
                preferences.UseSmallCanonnRadarCircles;
            settings["AutoShowSurfaceRadar"] = preferences.AutoShowSurfaceRadar;
            settings["AutoShowMiniTrack"] = preferences.AutoShowMiniTrack;
            settings["SurfaceRadarSize"] = preferences.SurfaceRadarSize;
            settings["AutoHideSurfaceRadarWithoutLandingGear"] =
                preferences.AutoHideSurfaceRadarWithoutLandingGear;
            settings["AutoRemoveTrackerOnSampling"] =
                preferences.AutoRemoveTrackerOnSampling;
            settings["AutoRemoveTrackerOnFinalSample"] =
                preferences.AutoRemoveTrackerOnFinalSample;
            settings["AutoTrackCompositionScans"] =
                preferences.AutoTrackCompositionScans;
            settings["SkipAnalyzedCompositionScans"] =
                preferences.SkipAnalyzedCompositionScans;
            settings["DrawBodyBiosOnlyWhenNear"] =
                preferences.DrawBodyBiosOnlyWhenNear;
            settings["HighlightRegionalFirsts"] =
                preferences.HighlightRegionalFirsts;
            settings["DimAnalyzedOrganisms"] =
                preferences.DimAnalyzedOrganisms;
            settings["HideGeoCountInBioSystem"] =
                preferences.HideGeoCountInBioSystem;
            settings["DisableBioPredictions"] =
                preferences.DisableBioPredictions;
            settings["ShowTemperatureRangeDebug"] =
                preferences.ShowTemperatureRangeDebug;
            settings["AutoShowLastFssBody"] = preferences.AutoShowLastFssBody;
            settings["AutoShowFssInfo"] = preferences.AutoShowFssInfo;
            settings["ShowFssInfoInSystemMap"] =
                preferences.ShowFssInfoInSystemMap;
            settings["ShowFssInfoInNavigationPanel"] =
                preferences.ShowFssInfoInNavigationPanel;
            settings["AutoShowSystemStatus"] = preferences.AutoShowSystemStatus;
            settings["HideGeoCount"] = preferences.HideGeoCount;
            settings["FssBodyValueFloor"] = preferences.FssBodyValueFloor;
            settings["HighlightDssCandidates"] =
                preferences.HighlightDssCandidates;
            settings["DssValueFloor"] = preferences.DssValueFloor;
            settings["SkipDistantDssCandidates"] =
                preferences.SkipDistantDssCandidates;
            settings["DssDistanceLimitLs"] = preferences.DssDistanceLimitLs;
            settings["SkipGasGiantsForDss"] = preferences.SkipGasGiantsForDss;
            settings["SkipRingsForDss"] = preferences.SkipRingsForDss;
            settings["ShowNonBodySignals"] = preferences.ShowNonBodySignals;
            WriteFssTuningDetectorSettings(
                settings,
                preferences.FssTuningDetector);
        });
    }

    private static FssTuningDetectorSettings GetFssTuningDetectorSettings(
        JsonObject? source,
        FssTuningDetectorSettings fallback)
    {
        return new FssTuningDetectorSettings(
            GetBoolean(source, "Enabled", fallback.Enabled),
            GetBoolean(
                source,
                "SaveDiagnosticImages",
                fallback.SaveDiagnosticImages),
            GetFssPixelColor(
                source?["YellowBar"] as JsonObject,
                fallback.YellowBar),
            GetInt32(
                source,
                "YellowHorizontalTolerance",
                fallback.YellowHorizontalTolerance,
                0,
                255),
            GetFssPixelColor(
                source?["BlackArea"] as JsonObject,
                fallback.BlackArea),
            GetFssPixelColor(
                source?["WhiteText"] as JsonObject,
                fallback.WhiteText),
            GetFssPixelColor(
                source?["YellowText"] as JsonObject,
                fallback.YellowText));
    }

    private static FssPixelColor GetFssPixelColor(
        JsonObject? source,
        FssPixelColor fallback)
    {
        return new FssPixelColor(
            GetInt32(source, "Red", fallback.Red, 0, 255),
            GetInt32(source, "Green", fallback.Green, 0, 255),
            GetInt32(source, "Blue", fallback.Blue, 0, 255),
            GetInt32(source, "Tolerance", fallback.Tolerance, 0, 255));
    }

    private static void WriteFssTuningDetectorSettings(
        JsonObject settings,
        FssTuningDetectorSettings preferences)
    {
        var detector = settings["FssTuningDetector"] as JsonObject;
        if (detector is null)
        {
            detector = [];
            settings["FssTuningDetector"] = detector;
        }

        detector["Enabled"] = preferences.Enabled;
        detector["SaveDiagnosticImages"] = preferences.SaveDiagnosticImages;
        detector["YellowHorizontalTolerance"] =
            preferences.YellowHorizontalTolerance;
        WriteFssPixelColor(detector, "YellowBar", preferences.YellowBar);
        WriteFssPixelColor(detector, "BlackArea", preferences.BlackArea);
        WriteFssPixelColor(detector, "WhiteText", preferences.WhiteText);
        WriteFssPixelColor(detector, "YellowText", preferences.YellowText);
    }

    private static void WriteFssPixelColor(
        JsonObject detector,
        string propertyName,
        FssPixelColor color)
    {
        var target = detector[propertyName] as JsonObject;
        if (target is null)
        {
            target = [];
            detector[propertyName] = target;
        }

        target["Red"] = color.Red;
        target["Green"] = color.Green;
        target["Blue"] = color.Blue;
        target["Tolerance"] = color.Tolerance;
    }

    private static bool GetBoolean(
        JsonObject? source,
        string propertyName,
        bool fallback)
    {
        return source?[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : fallback;
    }

    private static int GetInt32(
        JsonObject? source,
        string propertyName,
        int fallback,
        int minimum,
        int maximum = int.MaxValue)
    {
        return source?[propertyName] is JsonValue value
            && value.TryGetValue<int>(out var result)
                ? Math.Clamp(result, minimum, maximum)
                : fallback;
    }

    private static double GetDouble(
        JsonObject? source,
        string propertyName,
        double fallback,
        double minimum,
        double maximum)
    {
        return source?[propertyName] is JsonValue value
            && value.TryGetValue<double>(out var result)
            && double.IsFinite(result)
                ? Math.Clamp(result, minimum, maximum)
                : fallback;
    }
}

public sealed record SystemSurveyPreferences(
    bool AutoShowBodyInfo,
    bool ShowBodyInfoInSystemMap,
    bool ShowBodyInfoInOrbit,
    bool ShowBodyInfoAtSurface,
    bool HideBodyInfoInBubble,
    int BodyInfoBubbleSizeLy,
    bool HideBodyInfoMaterials,
    bool AutoShowFlightWarnings,
    double HighGravityWarningLevel,
    bool UseExternalData,
    bool UseExternalBioData,
    bool AutoShowBioSystem,
    bool AutoShowBioStatus,
    bool AutoShowPriorScans,
    bool SkipPriorScansLowValue,
    int PriorScanMinimumValue,
    bool HideOwnCanonnSignals,
    bool ShowCanonnSignalsOnRadar,
    bool UseSmallCanonnRadarCircles,
    bool AutoShowSurfaceRadar,
    bool AutoShowMiniTrack,
    int SurfaceRadarSize,
    bool AutoHideSurfaceRadarWithoutLandingGear,
    bool AutoRemoveTrackerOnSampling,
    bool AutoRemoveTrackerOnFinalSample,
    bool AutoTrackCompositionScans,
    bool SkipAnalyzedCompositionScans,
    bool DrawBodyBiosOnlyWhenNear,
    bool HighlightRegionalFirsts,
    bool DimAnalyzedOrganisms,
    bool HideGeoCountInBioSystem,
    bool DisableBioPredictions,
    bool ShowTemperatureRangeDebug,
    bool AutoShowLastFssBody,
    bool AutoShowFssInfo,
    bool ShowFssInfoInSystemMap,
    bool ShowFssInfoInNavigationPanel,
    bool AutoShowSystemStatus,
    bool HideGeoCount,
    int FssBodyValueFloor,
    bool HighlightDssCandidates,
    int DssValueFloor,
    bool SkipDistantDssCandidates,
    int DssDistanceLimitLs,
    bool SkipGasGiantsForDss,
    bool SkipRingsForDss,
    bool ShowNonBodySignals,
    FssTuningDetectorSettings FssTuningDetector)
{
    public static SystemSurveyPreferences Default { get; } = new(
        AutoShowBodyInfo: true,
        ShowBodyInfoInSystemMap: true,
        ShowBodyInfoInOrbit: true,
        ShowBodyInfoAtSurface: false,
        HideBodyInfoInBubble: true,
        BodyInfoBubbleSizeLy: 200,
        HideBodyInfoMaterials: false,
        AutoShowFlightWarnings: true,
        HighGravityWarningLevel: 1,
        UseExternalData: true,
        UseExternalBioData: false,
        AutoShowBioSystem: true,
        AutoShowBioStatus: true,
        AutoShowPriorScans: true,
        SkipPriorScansLowValue: false,
        PriorScanMinimumValue: 1_000_000,
        HideOwnCanonnSignals: true,
        ShowCanonnSignalsOnRadar: true,
        UseSmallCanonnRadarCircles: true,
        AutoShowSurfaceRadar: true,
        AutoShowMiniTrack: false,
        SurfaceRadarSize: 3,
        AutoHideSurfaceRadarWithoutLandingGear: false,
        AutoRemoveTrackerOnSampling: true,
        AutoRemoveTrackerOnFinalSample: false,
        AutoTrackCompositionScans: true,
        SkipAnalyzedCompositionScans: true,
        DrawBodyBiosOnlyWhenNear: true,
        HighlightRegionalFirsts: false,
        DimAnalyzedOrganisms: true,
        HideGeoCountInBioSystem: false,
        DisableBioPredictions: false,
        ShowTemperatureRangeDebug: false,
        AutoShowLastFssBody: true,
        AutoShowFssInfo: true,
        ShowFssInfoInSystemMap: false,
        ShowFssInfoInNavigationPanel: false,
        AutoShowSystemStatus: true,
        HideGeoCount: false,
        FssBodyValueFloor: 10_000,
        HighlightDssCandidates: true,
        DssValueFloor: 1_000_000,
        SkipDistantDssCandidates: false,
        DssDistanceLimitLs: 100_000,
        SkipGasGiantsForDss: true,
        SkipRingsForDss: true,
        ShowNonBodySignals: false,
        FssTuningDetector: FssTuningDetectorSettings.Default);
}

public sealed record FssTuningDetectorSettings(
    bool Enabled,
    bool SaveDiagnosticImages,
    FssPixelColor YellowBar,
    int YellowHorizontalTolerance,
    FssPixelColor BlackArea,
    FssPixelColor WhiteText,
    FssPixelColor YellowText)
{
    public static FssTuningDetectorSettings Default { get; } = new(
        Enabled: true,
        SaveDiagnosticImages: false,
        YellowBar: new FssPixelColor(193, 156, 65, 60),
        YellowHorizontalTolerance: 100,
        BlackArea: new FssPixelColor(0, 0, 0, 30),
        WhiteText: new FssPixelColor(255, 255, 255, 50),
        YellowText: new FssPixelColor(233, 197, 24, 50));
}

public sealed record FssPixelColor(
    int Red,
    int Green,
    int Blue,
    int Tolerance);
