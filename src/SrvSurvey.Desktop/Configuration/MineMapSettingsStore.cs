using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class MineMapSettingsStore(string path)
{
    private const string SettingsKey = "MineMap";
    private readonly UiSettingsDocumentStore document = new(path);

    public MineMapPreferences Load()
    {
        var settings = document.Load()[SettingsKey] as JsonObject;
        return new MineMapPreferences(
            OnlyShowWhileOnGround: settings?["OnlyShowWhileOnGround"]
                is JsonValue value
                && value.TryGetValue<bool>(out var enabled)
                && enabled,
            MiningReferenceCommodities: settings?["MiningReferenceCommodities"]
                is JsonArray commodities
                    ? commodities
                        .Select(item => item?.GetValue<string>())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Cast<string>()
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                    : []);
    }

    public void Save(MineMapPreferences preferences) => document.Update(root =>
    {
        if (root[SettingsKey] is not JsonObject settings)
        {
            settings = [];
            root[SettingsKey] = settings;
        }

        settings["OnlyShowWhileOnGround"] = preferences.OnlyShowWhileOnGround;
        settings["MiningReferenceCommodities"] = new JsonArray(
            preferences.EffectiveMiningReferenceCommodities
                .Select(item => (JsonNode?)JsonValue.Create(item))
                .ToArray());
    });
}

public sealed record MineMapPreferences(
    bool OnlyShowWhileOnGround,
    IReadOnlyList<string>? MiningReferenceCommodities = null)
{
    public IReadOnlyList<string> EffectiveMiningReferenceCommodities =>
        MiningReferenceCommodities ?? [];
}
