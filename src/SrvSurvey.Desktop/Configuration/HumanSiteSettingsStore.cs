using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class HumanSiteSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public HumanSiteSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public HumanSitePreferences Load()
    {
        var settings = documentStore.Load()["HumanSite"] as JsonObject;
        var defaults = HumanSitePreferences.Default;
        return new HumanSitePreferences(
            GetBoolean(settings, "AutoShow", defaults.AutoShow),
            GetInt32(settings, "Width", defaults.Width, 320, 1600),
            GetInt32(settings, "Height", defaults.Height, 320, 1400),
            GetDouble(settings, "ShipZoom", defaults.ShipZoom, 0.2, 15),
            GetDouble(settings, "SrvZoom", defaults.SrvZoom, 0.2, 15),
            GetDouble(settings, "FootZoom", defaults.FootZoom, 0.2, 15),
            GetBoolean(
                settings,
                "AutoZoomInside",
                defaults.AutoZoomInside),
            GetDouble(settings, "InsideZoom", defaults.InsideZoom, 0.2, 15),
            GetBoolean(settings, "AutoZoomTool", defaults.AutoZoomTool),
            GetDouble(settings, "ToolZoom", defaults.ToolZoom, 0.2, 15),
            GetBoolean(settings, "ShowMedkits", defaults.ShowMedkits),
            GetBoolean(settings, "ShowBatteries", defaults.ShowBatteries),
            GetBoolean(
                settings,
                "ShowDataTerminals",
                defaults.ShowDataTerminals),
            GetBoolean(
                settings,
                "ShowCollectedMaterials",
                defaults.ShowCollectedMaterials),
            GetBoolean(
                settings,
                "SuppressForActiveBuildProjects",
                defaults.SuppressForActiveBuildProjects));
    }

    public void Save(HumanSitePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["HumanSite"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["HumanSite"] = settings;
            }

            root["Version"] = 1;
            settings["AutoShow"] = preferences.AutoShow;
            settings["Width"] = preferences.Width;
            settings["Height"] = preferences.Height;
            settings["ShipZoom"] = preferences.ShipZoom;
            settings["SrvZoom"] = preferences.SrvZoom;
            settings["FootZoom"] = preferences.FootZoom;
            settings["AutoZoomInside"] = preferences.AutoZoomInside;
            settings["InsideZoom"] = preferences.InsideZoom;
            settings["AutoZoomTool"] = preferences.AutoZoomTool;
            settings["ToolZoom"] = preferences.ToolZoom;
            settings["ShowMedkits"] = preferences.ShowMedkits;
            settings["ShowBatteries"] = preferences.ShowBatteries;
            settings["ShowDataTerminals"] = preferences.ShowDataTerminals;
            settings["ShowCollectedMaterials"] =
                preferences.ShowCollectedMaterials;
            settings["SuppressForActiveBuildProjects"] =
                preferences.SuppressForActiveBuildProjects;
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
        int minimum,
        int maximum)
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

public sealed record HumanSitePreferences(
    bool AutoShow,
    int Width,
    int Height,
    double ShipZoom,
    double SrvZoom,
    double FootZoom,
    bool AutoZoomInside,
    double InsideZoom,
    bool AutoZoomTool,
    double ToolZoom,
    bool ShowMedkits,
    bool ShowBatteries,
    bool ShowDataTerminals,
    bool ShowCollectedMaterials,
    bool SuppressForActiveBuildProjects)
{
    public static HumanSitePreferences Default { get; } = new(
        AutoShow: true,
        Width: 500,
        Height: 600,
        ShipZoom: 1,
        SrvZoom: 1.5,
        FootZoom: 2,
        AutoZoomInside: true,
        InsideZoom: 4,
        AutoZoomTool: true,
        ToolZoom: 6,
        ShowMedkits: true,
        ShowBatteries: true,
        ShowDataTerminals: true,
        ShowCollectedMaterials: true,
        SuppressForActiveBuildProjects: true);
}
