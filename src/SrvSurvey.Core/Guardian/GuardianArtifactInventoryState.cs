using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianArtifactInventoryState
{
    private static readonly IReadOnlyDictionary<string, ArtifactDefinition>
        Definitions = BuildDefinitions();
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
                -Math.Max(0, GetInt32(journalEvent.Payload, "Count") ?? 0)),
            "MarketBuy" => ApplyDelta(
                GetString(journalEvent.Payload, "Type"),
                Math.Max(0, GetInt32(journalEvent.Payload, "Count") ?? 0)),
            "MarketSell" => ApplyDelta(
                GetString(journalEvent.Payload, "Type"),
                -Math.Max(0, GetInt32(journalEvent.Payload, "Count") ?? 0)),
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
                : new ArtifactDefinition(code, code, code))
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
            var count = GetInt32(item, "Count") ?? 0;
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
            var count = GetInt32(transfer, "Count") ?? 0;
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

    private static IReadOnlyDictionary<string, ArtifactDefinition>
        BuildDefinitions()
    {
        var definitions = new[]
        {
            new ArtifactDefinition("ca", "ancientcasket", "Guardian Casket"),
            new ArtifactDefinition("or", "ancientorb", "Guardian Orb"),
            new ArtifactDefinition("re", "ancientrelic", "Guardian Relic"),
            new ArtifactDefinition("ta", "ancienttablet", "Guardian Tablet"),
            new ArtifactDefinition("to", "ancienttotem", "Guardian Totem"),
            new ArtifactDefinition("ur", "ancienturn", "Guardian Urn"),
        };
        var result = new Dictionary<string, ArtifactDefinition>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            result[definition.ShortCode] = definition;
            result[definition.CommodityName] = definition;
            result[definition.DisplayName] = definition;
            result[definition.DisplayName[9..]] = definition;
        }

        return result;
    }

    private sealed record ArtifactDefinition(
        string ShortCode,
        string CommodityName,
        string DisplayName);
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
