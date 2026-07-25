using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class QuestSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public QuestSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public bool LoadEnabled()
    {
        var settings = documentStore.Load()["Quests"] as JsonObject;
        return settings?["Enabled"] is JsonValue enabled
            && enabled.TryGetValue<bool>(out var value)
            && value;
    }

    public void SaveEnabled(bool enabled)
    {
        documentStore.Update(root =>
        {
            var settings = root["Quests"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["Quests"] = settings;
            }

            root["Version"] = 1;
            settings["Enabled"] = enabled;
        });
    }
}
