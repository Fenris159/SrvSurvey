using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class OverlayVehicleSettingsStore(string path)
{
    private const string SettingsKey = "OverlayVehicleAllowLists";

    private const string FiregroupsMigration = "FiregroupsOverlayExceptionsMigrated";
    private readonly UiSettingsDocumentStore document = new(path);

    public void MigrateFiregroupsCategory()
    {
        var stored = document.Load();
        if (IsFiregroupsMigrated(stored)) return;
        document.Update(root =>
        {
            if (root[SettingsKey] is JsonObject lists
                && lists[nameof(OverlaySettingsCategory.Firegroups)] is null
                && lists[nameof(OverlaySettingsCategory.Global)] is JsonArray original)
                lists[nameof(OverlaySettingsCategory.Firegroups)] = original.DeepClone();
            root[FiregroupsMigration] = true;
        });
    }

    public IReadOnlySet<string>? Load(OverlaySettingsCategory category)
    {
        var root = document.Load();
        var lists = root[SettingsKey] as JsonObject;
        var values = lists?[category.ToString()] as JsonArray;
        if (values is null && category == OverlaySettingsCategory.Firegroups && !IsFiregroupsMigrated(root)) values = lists?[nameof(OverlaySettingsCategory.Global)] as JsonArray;
        return values?.OfType<JsonValue>()
            .Select(v => v.TryGetValue<string>(out var id) ? id : null)
            .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsFiregroupsMigrated(JsonObject root) => root[FiregroupsMigration] is JsonValue value
        && value.TryGetValue<bool>(out var migrated) && migrated;

    public void Save(OverlaySettingsCategory category, IEnumerable<string> allowed)
    {
        var values = new JsonArray(allowed.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        document.Update(root =>
        {
            if (root[SettingsKey] is not JsonObject lists)
            {
                lists = [];
                root[SettingsKey] = lists;
            }
            lists[category.ToString()] = values;
        });
    }
}
