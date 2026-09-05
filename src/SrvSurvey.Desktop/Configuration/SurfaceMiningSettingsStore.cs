using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class SurfaceMiningSettingsStore(string path)
{
    private readonly UiSettingsDocumentStore documentStore = new(path);

    public bool LoadAutoClearRigsOnShipBoarding()
    {
        var settings = documentStore.Load()["SurfaceMining"] as JsonObject;
        return settings?["AutoClearRigsOnShipBoarding"] is not JsonValue value
            || !value.TryGetValue<bool>(out var enabled)
            || enabled;
    }

    public void SaveAutoClearRigsOnShipBoarding(bool enabled)
    {
        documentStore.Update(root =>
        {
            root["Version"] = 1;
            var settings = root["SurfaceMining"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["SurfaceMining"] = settings;
            }

            settings["AutoClearRigsOnShipBoarding"] = enabled;
        });
    }
}
