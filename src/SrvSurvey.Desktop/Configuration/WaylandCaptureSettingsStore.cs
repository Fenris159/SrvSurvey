using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class WaylandCaptureSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public WaylandCaptureSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public WaylandCapturePreferences Load()
    {
        var settings = documentStore.Load()["WaylandCapture"] as JsonObject;
        return new WaylandCapturePreferences(GetBoolean(settings, "Enabled", fallback: false));
    }

    public void Save(WaylandCapturePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            root["Version"] = 1;
            var settings = root["WaylandCapture"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["WaylandCapture"] = settings;
            }

            settings["Enabled"] = preferences.Enabled;
        });
    }

    private static bool GetBoolean(JsonObject? settings, string propertyName, bool fallback)
    {
        return settings?[propertyName] is JsonValue value && value.TryGetValue<bool>(out bool result)
            ? result
            : fallback;
    }
}

public sealed record WaylandCapturePreferences(bool Enabled);
