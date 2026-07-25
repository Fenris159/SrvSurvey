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
}

public sealed record SystemSurveyPreferences(
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
