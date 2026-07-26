using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class GuardianOverlaySettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public GuardianOverlaySettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public GuardianOverlayPreferences Load()
    {
        var settings = documentStore.Load()["GuardianOverlays"] as JsonObject;
        var defaults = GuardianOverlayPreferences.Default;
        return new GuardianOverlayPreferences(
            GetBoolean(
                settings,
                "EnableGuardianSites",
                defaults.EnableGuardianSites),
            GetBoolean(
                settings,
                "AutoShowGuardianSummary",
                defaults.AutoShowGuardianSummary),
            GetBoolean(
                settings,
                "AutoShowRamTah",
                defaults.AutoShowRamTah),
            GetBoolean(
                settings,
                "SuppressForActiveBuildProjects",
                defaults.SuppressForActiveBuildProjects),
            GetBoolean(
                settings,
                "AutoZoomNearObelisks",
                defaults.AutoZoomNearObelisks),
            GetBoolean(
                settings,
                "AutoZoomInSrvTurret",
                defaults.AutoZoomInSrvTurret),
            GetBoolean(
                settings,
                "ShowComponentMaterials",
                defaults.ShowComponentMaterials),
            GetInteger(
                settings,
                "OverlaySizeIndex",
                defaults.OverlaySizeIndex),
            GetBoolean(
                settings,
                "DisableRuinsMeasurementGrid",
                defaults.DisableRuinsMeasurementGrid),
            GetBoolean(
                settings,
                "DisableAerialAlignmentGrid",
                defaults.DisableAerialAlignmentGrid));
    }

    public void Save(GuardianOverlayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["GuardianOverlays"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["GuardianOverlays"] = settings;
            }

            root["Version"] = 1;
            settings["EnableGuardianSites"] = preferences.EnableGuardianSites;
            settings["AutoShowGuardianSummary"] =
                preferences.AutoShowGuardianSummary;
            settings["AutoShowRamTah"] = preferences.AutoShowRamTah;
            settings["SuppressForActiveBuildProjects"] =
                preferences.SuppressForActiveBuildProjects;
            settings["AutoZoomNearObelisks"] =
                preferences.AutoZoomNearObelisks;
            settings["AutoZoomInSrvTurret"] =
                preferences.AutoZoomInSrvTurret;
            settings["ShowComponentMaterials"] =
                preferences.ShowComponentMaterials;
            settings["OverlaySizeIndex"] = Math.Clamp(
                preferences.OverlaySizeIndex,
                0,
                4);
            settings["DisableRuinsMeasurementGrid"] =
                preferences.DisableRuinsMeasurementGrid;
            settings["DisableAerialAlignmentGrid"] =
                preferences.DisableAerialAlignmentGrid;
        });
    }

    private static bool GetBoolean(
        JsonObject? settings,
        string propertyName,
        bool fallback)
    {
        return settings?[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : fallback;
    }

    private static int GetInteger(
        JsonObject? settings,
        string propertyName,
        int fallback)
    {
        return settings?[propertyName] is JsonValue value
            && value.TryGetValue<int>(out var result)
                ? Math.Clamp(result, 0, 4)
                : fallback;
    }
}

public sealed record GuardianOverlayPreferences(
    bool EnableGuardianSites,
    bool AutoShowGuardianSummary,
    bool AutoShowRamTah,
    bool SuppressForActiveBuildProjects,
    bool AutoZoomNearObelisks,
    bool AutoZoomInSrvTurret,
    bool ShowComponentMaterials,
    int OverlaySizeIndex,
    bool DisableRuinsMeasurementGrid,
    bool DisableAerialAlignmentGrid)
{
    public static GuardianOverlayPreferences Default { get; } = new(
        EnableGuardianSites: true,
        AutoShowGuardianSummary: true,
        AutoShowRamTah: true,
        SuppressForActiveBuildProjects: false,
        AutoZoomNearObelisks: true,
        AutoZoomInSrvTurret: false,
        ShowComponentMaterials: false,
        OverlaySizeIndex: 0,
        DisableRuinsMeasurementGrid: false,
        DisableAerialAlignmentGrid: false);
}
