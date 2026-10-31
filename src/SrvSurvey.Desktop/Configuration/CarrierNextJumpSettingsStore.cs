using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class CarrierNextJumpSettingsStore(string path, bool squadron = false)
{
    private readonly UiSettingsDocumentStore store = new(path);
    private readonly string section = squadron ? "SquadronCarrierNextJump" : "CarrierNextJump";

    public bool Load() =>
        (store.Load()[section] as JsonObject)?["ShowInformation"] is not JsonValue value
        || !value.TryGetValue<bool>(out bool enabled)
        || enabled;

    public void Save(bool enabled) =>
        store.Update(root =>
        {
            JsonObject settings = root[section] as JsonObject ?? [];
            settings["ShowInformation"] = enabled;
            root[section] = settings;
        });
}
