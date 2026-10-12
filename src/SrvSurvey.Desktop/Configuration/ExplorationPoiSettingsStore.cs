using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class ExplorationPoiSettingsStore(string path)
{
    private readonly UiSettingsDocumentStore store = new(path);

    /// <summary>Restores the passive current-system POI overlay preference.</summary>
    public bool LoadAutoShow()
    {
        if (
            store.Load()["ExplorationPois"] is JsonObject section
            && section["AutoShow"] is JsonValue value
            && value.TryGetValue<bool>(out bool enabled)
        )
        {
            return enabled;
        }
        return true;
    }

    /// <summary>Persists the passive POI overlay choice without replacing other settings sections.</summary>
    public void SaveAutoShow(bool value) =>
        store.Update(root =>
        {
            root["ExplorationPois"] = new JsonObject { ["AutoShow"] = value };
        });
}
