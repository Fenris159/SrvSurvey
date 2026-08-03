using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class ReleaseUpdateSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public ReleaseUpdateSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public bool LoadUseDevelopmentReleases()
    {
        var settings = documentStore.Load()["ReleaseUpdates"] as JsonObject;
        return settings?["UseDevelopmentReleases"] is JsonValue value
            && value.TryGetValue<bool>(out var enabled)
                ? enabled
                : true;
    }

    public void SaveUseDevelopmentReleases(bool enabled)
    {
        documentStore.Update(root =>
        {
            root["Version"] = 1;
            var settings = root["ReleaseUpdates"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["ReleaseUpdates"] = settings;
            }

            settings["UseDevelopmentReleases"] = enabled;
        });
    }
}
