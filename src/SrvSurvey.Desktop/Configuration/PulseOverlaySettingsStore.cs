using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class PulseOverlaySettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public PulseOverlaySettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public PulseOverlayPreferences Load()
    {
        var settings = documentStore.Load()["PulseOverlay"] as JsonObject;
        return new PulseOverlayPreferences(
            GetBoolean(settings, "Enabled", true));
    }

    public void Save(PulseOverlayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["PulseOverlay"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["PulseOverlay"] = settings;
            }

            root["Version"] = 1;
            settings["Enabled"] = preferences.Enabled;
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
}

public sealed record PulseOverlayPreferences(bool Enabled);
