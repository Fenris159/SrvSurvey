using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class OverlayBehaviorSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public OverlayBehaviorSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    /// <summary>Loads this commander's overlay preferences, leaving window management enabled by default.</summary>
    public OverlayBehaviorPreferences Load()
    {
        var settings = documentStore.Load()["OverlayBehavior"] as JsonObject;
        return new OverlayBehaviorPreferences(
            GetBoolean(settings, "KeepWhenGameLosesFocus", false),
            GetBoolean(settings, "HideInDominatorSuit", false),
            GetBoolean(settings, "HideInMaverickSuit", false),
            GetBoolean(settings, "HideMultiGameCommanderOverlay", false),
            GetPreferredMonitorId(settings),
            GetBoolean(settings, "LockToMonitor", false),
            GetBoolean(settings, "BypassWindowManagement", false)
        );
    }

    /// <summary>Persists overlay preferences while preserving other settings in the shared document.</summary>
    public void Save(OverlayBehaviorPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["OverlayBehavior"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["OverlayBehavior"] = settings;
            }

            root["Version"] = 1;
            settings["KeepWhenGameLosesFocus"] = preferences.KeepWhenGameLosesFocus;
            settings["HideInDominatorSuit"] = preferences.HideInDominatorSuit;
            settings["HideInMaverickSuit"] = preferences.HideInMaverickSuit;
            settings["HideMultiGameCommanderOverlay"] = preferences.HideMultiGameCommanderOverlay;
            settings["LockToMonitor"] = preferences.LockToMonitor;
            settings["BypassWindowManagement"] = preferences.BypassWindowManagement;
            if (string.IsNullOrWhiteSpace(preferences.PreferredMonitorId))
            {
                settings.Remove("PreferredMonitor");
            }
            else
            {
                settings["PreferredMonitor"] = preferences.PreferredMonitorId.Trim();
            }
        });
    }

    private static bool GetBoolean(JsonObject? settings, string propertyName, bool fallback)
    {
        return settings?[propertyName] is JsonValue value && value.TryGetValue<bool>(out bool result)
            ? result
            : fallback;
    }

    private static string? GetPreferredMonitorId(JsonObject? settings)
    {
        return
            settings?["PreferredMonitor"] is JsonValue value
            && value.TryGetValue<string>(out string? result)
            && !string.IsNullOrWhiteSpace(result)
            ? result.Trim()
            : null;
    }
}

/// <summary>Per-commander overlay choices; window-management bypass is opt-in and applied at startup.</summary>
public sealed record OverlayBehaviorPreferences(
    bool KeepWhenGameLosesFocus,
    bool HideInDominatorSuit,
    bool HideInMaverickSuit,
    bool HideMultiGameCommanderOverlay = false,
    string? PreferredMonitorId = null,
    bool LockToMonitor = false,
    bool BypassWindowManagement = false
);
