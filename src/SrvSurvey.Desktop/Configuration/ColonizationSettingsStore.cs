using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class ColonizationSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public ColonizationSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public bool LoadEnabled()
    {
        var root = documentStore.Load();
        return root["Colonization"] is JsonObject colonization
            && colonization["Enabled"] is JsonValue enabled
            && enabled.TryGetValue<bool>(out var value)
            && value;
    }

    public ColonizationOverlayPreferences LoadOverlayPreferences()
    {
        var root = documentStore.Load();
        var colonization = root["Colonization"] as JsonObject;
        var overlay = colonization?["Overlay"] as JsonObject;
        var defaults = ColonizationOverlayPreferences.Default;
        return new ColonizationOverlayPreferences(
            GetBoolean(overlay, "AutoShow", defaults.AutoShow),
            GetBoolean(
                overlay,
                "ShowOnRightPanel",
                defaults.ShowOnRightPanel),
            GetBoolean(
                overlay,
                "ShowFleetCarrierCargo",
                defaults.ShowFleetCarrierCargo),
            GetBoolean(
                overlay,
                "ShowFleetCarrierDelta",
                defaults.ShowFleetCarrierDelta),
            GetBoolean(
                overlay,
                "InlineFleetCarrierCargo",
                defaults.InlineFleetCarrierCargo),
            GetBoolean(
                overlay,
                "CollapseCoveredGroups",
                defaults.CollapseCoveredGroups));
    }

    public void SaveEnabled(bool enabled)
    {
        documentStore.Update(root =>
        {
            var colonization = root["Colonization"] as JsonObject;
            if (colonization is null)
            {
                colonization = [];
                root["Colonization"] = colonization;
            }

            root["Version"] = 1;
            colonization["Enabled"] = enabled;
        });
    }

    public void SaveOverlayPreferences(
        ColonizationOverlayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var colonization = root["Colonization"] as JsonObject;
            if (colonization is null)
            {
                colonization = [];
                root["Colonization"] = colonization;
            }

            var overlay = colonization["Overlay"] as JsonObject;
            if (overlay is null)
            {
                overlay = [];
                colonization["Overlay"] = overlay;
            }

            root["Version"] = 1;
            overlay["AutoShow"] = preferences.AutoShow;
            overlay["ShowOnRightPanel"] = preferences.ShowOnRightPanel;
            overlay["ShowFleetCarrierCargo"] =
                preferences.ShowFleetCarrierCargo;
            overlay["ShowFleetCarrierDelta"] =
                preferences.ShowFleetCarrierDelta;
            overlay["InlineFleetCarrierCargo"] =
                preferences.InlineFleetCarrierCargo;
            overlay["CollapseCoveredGroups"] =
                preferences.CollapseCoveredGroups;
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
}

public sealed record ColonizationOverlayPreferences(
    bool AutoShow,
    bool ShowOnRightPanel,
    bool ShowFleetCarrierCargo,
    bool ShowFleetCarrierDelta,
    bool InlineFleetCarrierCargo,
    bool CollapseCoveredGroups)
{
    public static ColonizationOverlayPreferences Default { get; } = new(
        AutoShow: true,
        ShowOnRightPanel: true,
        ShowFleetCarrierCargo: true,
        ShowFleetCarrierDelta: false,
        InlineFleetCarrierCargo: false,
        CollapseCoveredGroups: true);
}
