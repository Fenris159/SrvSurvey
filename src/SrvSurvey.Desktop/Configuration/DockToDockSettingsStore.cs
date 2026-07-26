using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class DockToDockSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public DockToDockSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public bool LoadEnabled()
    {
        var settings = documentStore.Load()["Travel"] as JsonObject;
        return settings?["LogDockToDockTimes"] is JsonValue value
            && value.TryGetValue<bool>(out var enabled)
            && enabled;
    }

    public void SaveEnabled(bool enabled)
    {
        documentStore.Update(root =>
        {
            root["Version"] = 1;
            var settings = root["Travel"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["Travel"] = settings;
            }

            settings["LogDockToDockTimes"] = enabled;
        });
    }
}
