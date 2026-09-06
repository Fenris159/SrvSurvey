using System.Text.Json.Nodes;
using System.Text.Json;

namespace SrvSurvey.Desktop.Configuration;

public sealed class SurfaceMiningSettingsStore(string path)
{
    private readonly UiSettingsDocumentStore documentStore = new(path);

    public MiningDetectionSettings LoadDetection()
    {
        try
        {
            var mining = documentStore.Load()["SurfaceMining"] as JsonObject;
            return (mining?["Detection"]
                ?.Deserialize<MiningDetectionSettings>() ?? new()).Normalize();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public void SaveDetection(MiningDetectionSettings value) => documentStore.Update(root =>
    {
        var mining = root["SurfaceMining"] as JsonObject;
        if (mining is null)
        {
            mining = new JsonObject();
            root["SurfaceMining"] = mining;
        }
        mining["Detection"] = JsonSerializer.SerializeToNode(value.Normalize());
    });

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
