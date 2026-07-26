using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class StreamOverlaySettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public StreamOverlaySettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public bool LoadEnabled()
    {
        var settings = documentStore.Load()["Streaming"] as JsonObject;
        return settings?["JoinedOverlayEnabled"] is JsonValue value
            && value.TryGetValue<bool>(out var enabled)
            && enabled;
    }

    public void SaveEnabled(bool enabled)
    {
        documentStore.Update(root =>
        {
            var settings = root["Streaming"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["Streaming"] = settings;
            }

            root["Version"] = 1;
            settings["JoinedOverlayEnabled"] = enabled;
        });
    }
}
