using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianArtifactInventoryState
{
    private const string CountPropertyName = "Count";

    private static readonly Dictionary<string, ArtifactDefinition> Definitions =
        BuildDefinitions();
    private readonly Dictionary<string, int> counts = new(
        StringComparer.OrdinalIgnoreCase);

    public int Version { get; private set; }

    public IReadOnlyDictionary<string, int> Counts => counts;

    public bool Reset(CargoSnapshot? cargo)
    {
        var replacement = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in cargo?.Inventory ?? [])
        {
            if (TryResolve(item.Name, out var definition) && item.Count > 0)
            {
                replacement[definition.CommodityName] = item.Count;
            }
        }

        if (counts.Count == replacement.Count
            && counts.All(entry => replacement.GetValueOrDefault(entry.Key)
                == entry.Value))
        {
            return false;
        }

        counts.Clear();
        foreach (var entry in replacement)
        {
            counts[entry.Key] = entry.Value;
        }

        Version++;
        return true;
    }

    public bool Apply(
        JournalEventEnvelope journalEvent,
        bool isInSrv = false)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        var changed = journalEvent.EventName switch
        {
            "CollectCargo" => ApplyDelta(
                GetString(journalEvent.Payload, "Type"),
                1),
            "EjectCargo" => ApplyDelta(
                GetString(journalEvent.Payload, "Type"),
                -Math.Max(0, GetInt32(journalEvent.Payload, CountPropertyName) ?? 0)),
            "MarketBuy" => ApplyDelta(
                GetString(journalEvent.Payload, "Type"),
                Math.Max(0, GetInt32(journalEvent.Payload, CountPropertyName) ?? 0)),
            "MarketSell" => ApplyDelta(
                GetString(journalEvent.Payload, "Type"),
                -Math.Max(0, GetInt32(journalEvent.Payload, CountPropertyName) ?? 0)),
            "CargoTransfer" => ApplyTransfers(
                journalEvent.Payload,
                isInSrv),
            "Cargo" => ApplyCargoEvent(journalEvent.Payload),
            _ => false,
        };
        if (changed)
        {
            Version++;
        }

        return changed;
    }

    public int GetCount(string itemCodeOrName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemCodeOrName);
        return TryResolve(itemCodeOrName, out var definition)
            ? counts.GetValueOrDefault(definition.CommodityName)
            : 0;
    }

    public IReadOnlyList<GuardianArtifactRequirement> GetRequirements(
        IEnumerable<string> itemCodes)
    {
        ArgumentNullException.ThrowIfNull(itemCodes);
        return itemCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => TryResolve(code, out var definition)
                ? definition
                : new ArtifactDefinition(code, code, code, []))
            .GroupBy(
                definition => definition.CommodityName,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var definition = group.First();
                var required = group.Count();
                return new GuardianArtifactRequirement(
                    definition.ShortCode,
                    definition.CommodityName,
                    definition.DisplayName,
                    required,
                    counts.GetValueOrDefault(definition.CommodityName));
            })
            .OrderBy(requirement => requirement.DisplayName)
            .ToArray();
    }

    public bool HasItems(IEnumerable<string> itemCodes)
    {
        return GetRequirements(itemCodes).All(requirement => requirement.IsMet);
    }

    private bool ApplyCargoEvent(JsonElement root)
    {
        if (!root.TryGetProperty("Inventory", out var inventory)
            || inventory.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var replacement = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in inventory.EnumerateArray())
        {
            var name = GetString(item, "Name");
            var count = GetInt32(item, CountPropertyName) ?? 0;
            if (TryResolve(name, out var definition) && count > 0)
            {
                replacement[definition.CommodityName] = (int)Math.Min(
                    int.MaxValue,
                    (long)replacement.GetValueOrDefault(
                        definition.CommodityName)
                    + count);
            }
        }

        if (counts.Count == replacement.Count
            && counts.All(entry => replacement.GetValueOrDefault(entry.Key)
                == entry.Value))
        {
            return false;
        }

        counts.Clear();
        foreach (var entry in replacement)
        {
            counts[entry.Key] = entry.Value;
        }

        return true;
    }

    private bool ApplyDelta(string? itemName, int delta)
    {
        if (delta == 0 || !TryResolve(itemName, out var definition))
        {
            return false;
        }

        var previous = counts.GetValueOrDefault(definition.CommodityName);
        var next = (int)Math.Clamp(
            (long)previous + delta,
            0,
            int.MaxValue);
        if (next == previous)
        {
            return false;
        }

        if (next == 0)
        {
            counts.Remove(definition.CommodityName);
        }
        else
        {
            counts[definition.CommodityName] = next;
        }

        return true;
    }

    private bool ApplyTransfers(JsonElement root, bool isInSrv)
    {
        if (!root.TryGetProperty("Transfers", out var transfers)
            || transfers.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var changed = false;
        foreach (var transfer in transfers.EnumerateArray())
        {
            var count = GetInt32(transfer, CountPropertyName) ?? 0;
            var direction = GetString(transfer, "Direction");
            if (count <= 0)
            {
                continue;
            }

            var delta = isInSrv
                ? direction switch
                {
                    "tosrv" => count,
                    "toship" => -count,
                    _ => 0,
                }
                : direction switch
                {
                    "toship" => count,
                    "tocarrier" => -count,
                    _ => 0,
                };
            changed |= ApplyDelta(
                GetString(transfer, "Type"),
                delta);
        }

        return changed;
    }

    private static bool TryResolve(
        string? itemCodeOrName,
        out ArtifactDefinition definition)
    {
        return Definitions.TryGetValue(itemCodeOrName ?? string.Empty, out definition!);
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static int? GetInt32(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
                ? number
                : null;
    }

    private static Dictionary<string, ArtifactDefinition>
        BuildDefinitions()
    {
        var definitions = new[]
        {
            new ArtifactDefinition(
                "ca",
                "ancientcasket",
                "Guardian Casket",
                ["casket"]),
            new ArtifactDefinition(
                "or",
                "ancientorb",
                "Guardian Orb",
                ["orb"]),
            new ArtifactDefinition(
                "re",
                "ancientrelic",
                "Guardian Relic",
                ["relic"]),
            new ArtifactDefinition(
                "ta",
                "ancienttablet",
                "Guardian Tablet",
                ["tablet"]),
            new ArtifactDefinition(
                "to",
                "ancienttotem",
                "Guardian Totem",
                ["totem"]),
            new ArtifactDefinition(
                "ur",
                "ancienturn",
                "Guardian Urn",
                ["urn"]),
            new ArtifactDefinition(
                "se",
                "unknownartifact",
                "Thargoid Sensor",
                ["sensor"]),
            new ArtifactDefinition(
                "pr",
                "unknownartifact2",
                "Thargoid Probe",
                ["probe"]),
            new ArtifactDefinition(
                "li",
                "unknownartifact3",
                "Thargoid Link",
                ["link"]),
            new ArtifactDefinition(
                "cy",
                "thargoidtissuesampletype1",
                "Cyclops Tissue Sample",
                ["cyclops"]),
            new ArtifactDefinition(
                "ba",
                "thargoidtissuesampletype2",
                "Basilisk Tissue Sample",
                ["basilisk"]),
            new ArtifactDefinition(
                "me",
                "thargoidtissuesampletype3",
                "Medusa Tissue Sample",
                ["medusa"]),
        };
        var result = new Dictionary<string, ArtifactDefinition>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            result[definition.ShortCode] = definition;
            result[definition.CommodityName] = definition;
            result[definition.DisplayName] = definition;
            foreach (var alias in definition.Aliases)
            {
                result[alias] = definition;
            }
        }

        return result;
    }

    private sealed record ArtifactDefinition(
        string ShortCode,
        string CommodityName,
        string DisplayName,
        IReadOnlyList<string> Aliases);
}

public sealed record GuardianArtifactRequirement(
    string ShortCode,
    string CommodityName,
    string DisplayName,
    int Required,
    int Available)
{
    public bool IsMet => Available >= Required;
}
