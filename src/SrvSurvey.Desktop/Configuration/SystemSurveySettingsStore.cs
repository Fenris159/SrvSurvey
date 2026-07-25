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
            GetDouble(
                settings,
                "HighGravityWarningLevel",
                defaults.HighGravityWarningLevel,
                0,
                50),
            GetBoolean(
                settings,
                "AutoShowBioSystem",
                defaults.AutoShowBioSystem),
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
                defaults.ShowNonBodySignals));
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
            settings["HighGravityWarningLevel"] =
                preferences.HighGravityWarningLevel;
            settings["AutoShowBioSystem"] = preferences.AutoShowBioSystem;
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
        });
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
        int minimum)
    {
        return source?[propertyName] is JsonValue value
            && value.TryGetValue<int>(out var result)
                ? Math.Max(minimum, result)
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
    double HighGravityWarningLevel,
    bool AutoShowBioSystem,
    bool DrawBodyBiosOnlyWhenNear,
    bool HighlightRegionalFirsts,
    bool DimAnalyzedOrganisms,
    bool HideGeoCountInBioSystem,
    bool DisableBioPredictions,
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
    bool ShowNonBodySignals)
{
    public static SystemSurveyPreferences Default { get; } = new(
        AutoShowBodyInfo: true,
        ShowBodyInfoInSystemMap: true,
        ShowBodyInfoInOrbit: true,
        ShowBodyInfoAtSurface: false,
        HideBodyInfoInBubble: true,
        BodyInfoBubbleSizeLy: 200,
        HideBodyInfoMaterials: false,
        HighGravityWarningLevel: 1,
        AutoShowBioSystem: true,
        DrawBodyBiosOnlyWhenNear: true,
        HighlightRegionalFirsts: false,
        DimAnalyzedOrganisms: true,
        HideGeoCountInBioSystem: false,
        DisableBioPredictions: false,
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
        ShowNonBodySignals: false);
}
