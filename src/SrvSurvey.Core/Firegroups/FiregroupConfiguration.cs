using System.Text.Json;

namespace SrvSurvey.Core.Firegroups;

public sealed record FiregroupModule(string Slot, string Symbol, string Name)
{
    public string Display => string.IsNullOrEmpty(Slot) ? Name : $"{Name} · {Slot}";
}

public sealed record FiregroupShip(string Key, string Type, long? Id, string Name, IReadOnlyList<FiregroupModule> Modules)
{
    public string Display => string.IsNullOrWhiteSpace(Name) ? $"{Type} · #{Id}" : $"{Name} · {Type}";
}

public sealed record FiregroupAssignment(int Number, IReadOnlyList<FiregroupModule> Primary, IReadOnlyList<FiregroupModule> Secondary)
{
    public string Label => $"Group {(char)('A' + Number)}";
}

public sealed record FiregroupProfile(string Id, string Name, FiregroupShip Ship, IReadOnlyList<FiregroupAssignment> Groups);

public sealed record FiregroupDocument
{
    public int Version { get; init; } = 1;
    public List<FiregroupShip> Ships { get; init; } = [];
    public List<FiregroupProfile> Profiles { get; init; } = [];
    public Dictionary<string, string> ActiveProfiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool LegacyImported { get; set; }
}

public static class FiregroupLoadout
{
    private static readonly Dictionary<string, string> Names = ReadNames();

    public static bool IsExcluded(FiregroupModule module) =>
        module.Symbol.Contains("shieldbooster", StringComparison.OrdinalIgnoreCase)
        || module.Symbol.Contains("pointdefence", StringComparison.OrdinalIgnoreCase)
        || module.Symbol.Contains("shieldcellbank", StringComparison.OrdinalIgnoreCase);

    public static FiregroupShip? Parse(JsonElement root)
    {
        var type = Text(root, "Ship");
        if (string.IsNullOrWhiteSpace(type) || !root.TryGetProperty("Modules", out var modules) || modules.ValueKind != JsonValueKind.Array) return null;
        long? id = root.TryGetProperty("ShipID", out var shipId) && shipId.ValueKind == JsonValueKind.Number && shipId.TryGetInt64(out var value) ? value : null;
        var name = Text(root, "ShipName");
        var key = $"{type.ToLowerInvariant()}:{(id is not null ? id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : name)}";
        var equipped = new List<FiregroupModule>();
        foreach (var module in modules.EnumerateArray())
        {
            var symbol = Text(module, "Item").ToLowerInvariant();
            var slot = Text(module, "Slot");
            if (slot.Length == 0 || !Names.TryGetValue(symbol, out var label)) continue;
            equipped.Add(new FiregroupModule(slot, symbol, label));
        }
        return new(key, type.ToLowerInvariant(), id, name, equipped.DistinctBy(m => m.Slot).ToArray());
    }

    private static string Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static Dictionary<string, string> ReadNames()
    {
        using var stream = typeof(FiregroupLoadout).Assembly.GetManifestResourceStream("SrvSurvey.Core.Resources.firegroup-modules.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
}
