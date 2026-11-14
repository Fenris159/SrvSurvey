using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Configuration;

/// <summary>Stores the opt-in prototype and panel angles without changing desktop or headset layouts.</summary>
public sealed class CockpitOverlaySettingsStore(string path)
{
    private readonly UiSettingsDocumentStore documentStore = new(path);

    /// <summary>Loads only finite supported poses, leaving the experimental mode off by default.</summary>
    public CockpitOverlayPreferences Load()
    {
        var settings = documentStore.Load()["CockpitPanelsPrototype"] as JsonObject;
        bool enabled = settings?["Enabled"] is JsonValue value && value.TryGetValue<bool>(out bool flag) && flag;
        var poses = new Dictionary<string, CockpitPanelPose>(StringComparer.Ordinal);
        if (settings?["Panels"] is JsonObject panels)
        {
            foreach (KeyValuePair<string, JsonNode?> entry in panels)
            {
                try
                {
                    if (entry.Value?.Deserialize<CockpitPanelPose>() is { IsValid: true } pose)
                    {
                        poses[entry.Key] = pose;
                    }
                }
                catch (JsonException)
                {
                    // An invalid experimental panel cannot prevent loading the user's normal settings.
                }
            }
        }
        return new CockpitOverlayPreferences(enabled, poses);
    }

    /// <summary>Atomically updates this feature's section while preserving unrelated settings.</summary>
    public void Save(CockpitOverlayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var panels = new JsonObject();
        foreach (KeyValuePair<string, CockpitPanelPose> entry in preferences.Panels)
        {
            if (!entry.Value.IsValid)
            {
                throw new ArgumentException(
                    "Cockpit panel angles or depth are outside the supported range.",
                    nameof(preferences)
                );
            }
            panels[entry.Key] = JsonSerializer.SerializeToNode(entry.Value);
        }
        documentStore.Update(root =>
            root["CockpitPanelsPrototype"] = new JsonObject { ["Enabled"] = preferences.Enabled, ["Panels"] = panels }
        );
    }
}

/// <summary>Independent desktop cockpit preferences; headset calibration remains separate.</summary>
public sealed record CockpitOverlayPreferences(bool Enabled, IReadOnlyDictionary<string, CockpitPanelPose> Panels);
