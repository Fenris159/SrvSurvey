using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class SystemNicknameSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public SystemNicknameSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public bool LoadEnabled()
    {
        var settings = documentStore.Load()["SystemNicknames"] as JsonObject;
        return settings?["Enabled"] is JsonValue enabled
            && enabled.TryGetValue<bool>(out var value)
            && value;
    }

    public void SaveEnabled(bool enabled)
    {
        documentStore.Update(root =>
        {
            var settings = root["SystemNicknames"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["SystemNicknames"] = settings;
            }

            root["Version"] = 1;
            settings["Enabled"] = enabled;
        });
    }
}
