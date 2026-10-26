using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class CarrierNextJumpSettingsStore(string path)
{
    private readonly UiSettingsDocumentStore store = new(path);

    public bool Load() =>
        (store.Load()["CarrierNextJump"] as JsonObject)?["ShowInformation"] is not JsonValue value
        || !value.TryGetValue<bool>(out bool enabled)
        || enabled;

    public void Save(bool enabled) =>
        store.Update(root =>
        {
            JsonObject settings = root["CarrierNextJump"] as JsonObject ?? [];
            settings["ShowInformation"] = enabled;
            root["CarrierNextJump"] = settings;
        });
}
