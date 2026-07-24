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
}
