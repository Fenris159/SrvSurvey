using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class OverlayVehicleSettingsStore(string path)
{
    private readonly UiSettingsDocumentStore document = new(path);

    public void MigrateFiregroupsCategory()
    {
        var stored = document.Load()["OverlayVehicleAllowLists"] as JsonObject;
        if (stored?[nameof(OverlaySettingsCategory.Firegroups)] is not null
            || stored?[nameof(OverlaySettingsCategory.Global)] is not JsonArray) return;
        document.Update(root =>
        {
            if (root["OverlayVehicleAllowLists"] is JsonObject lists
                && lists[nameof(OverlaySettingsCategory.Firegroups)] is null
                && lists[nameof(OverlaySettingsCategory.Global)] is JsonArray original)
                lists[nameof(OverlaySettingsCategory.Firegroups)] = original.DeepClone();
        });
    }

    public IReadOnlySet<string>? Load(OverlaySettingsCategory category)
    {
        var lists = document.Load()["OverlayVehicleAllowLists"] as JsonObject;
        var values = lists?[category.ToString()] as JsonArray;
        if (values is null && category == OverlaySettingsCategory.Firegroups) values = lists?[nameof(OverlaySettingsCategory.Global)] as JsonArray;
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
