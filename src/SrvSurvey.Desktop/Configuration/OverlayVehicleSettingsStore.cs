using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class OverlayVehicleSettingsStore(string path)
{
    private readonly UiSettingsDocumentStore document = new(path);

    public IReadOnlySet<string>? Load(OverlaySettingsCategory category)
    {
        var lists = document.Load()["OverlayVehicleAllowLists"] as JsonObject;
        var values = lists?[category.ToString()] as JsonArray;
        return values?.OfType<JsonValue>()
            .Select(v => v.TryGetValue<string>(out var id) ? id : null)
            .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public void Save(OverlaySettingsCategory category, IEnumerable<string> allowed)
    {
        var values = new JsonArray(allowed.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        document.Update(root =>
        {
            if (root["OverlayVehicleAllowLists"] is not JsonObject lists)
            {
                lists = [];
                root["OverlayVehicleAllowLists"] = lists;
            }
            lists[category.ToString()] = values;
        });
    }
}
