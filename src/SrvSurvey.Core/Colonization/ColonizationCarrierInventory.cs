namespace SrvSurvey.Core.Colonization;

/// <summary>Independent observations of the same physical carrier inventory, never extra stock to sum together.</summary>
public enum ColonizationInventorySource
{
    Raven,
    Local,
}

/// <summary>One commodity's stock and the source-specific aliases by which it was reported.</summary>
public sealed record ColonizationInventoryStock(string Commodity, long Quantity, IReadOnlyList<string> SourceKeys);

/// <summary>
/// A source's observation of one carrier. Commodity identity is shared across workspaces;
/// carrier identity, source, and original keys remain available when directing inventory actions.
/// </summary>
public sealed class ColonizationCarrierInventory
{
    /// <summary>
    /// Normalizes one source observation while retaining the original keys needed to direct later writes.
    /// </summary>
    private ColonizationCarrierInventory(
        long marketId,
        ColonizationInventorySource source,
        IEnumerable<KeyValuePair<string, long>> cargo
    )
    {
        MarketId = marketId;
        Source = source;
        Stock = ReadStock(cargo);
    }

    public long MarketId { get; }
    public ColonizationInventorySource Source { get; }
    public IReadOnlyList<ColonizationInventoryStock> Stock { get; }
    public bool HasAliases => Stock.Any(item => item.SourceKeys.Any(key => key != item.Commodity));

    /// <summary>Captures Raven source keys, accepting an explicit null manifest as an empty observation.</summary>
    public static ColonizationCarrierInventory FromRaven(ColonizationFleetCarrier carrier) =>
        new(carrier.MarketId, ColonizationInventorySource.Raven, AsLong(carrier.Cargo ?? []));

    /// <summary>Captures local quantities without merging them with the Raven observation.</summary>
    public static ColonizationCarrierInventory FromLocal(
        ColonizationFleetCarrier carrier,
        IEnumerable<KeyValuePair<string, long>> cargo
    ) => new(carrier.MarketId, ColonizationInventorySource.Local, cargo);

    /// <summary>Maps journal tokens, display names, and historical aliases to one commodity identity.</summary>
    public static string CommodityKey(string? value) =>
        ColonizationManifestCommodity.ForKey(ColonizationConstructionState.NormalizeCommodityName(value)).Key;

    /// <summary>Normalizes one stock snapshot. Alias counts describe the same stock, so retain the largest, never their sum.</summary>
    public static Dictionary<string, int> NormalizeStock(IEnumerable<KeyValuePair<string, int>> cargo) =>
        ReadStock(AsLong(cargo))
            .ToDictionary(
                item => item.Commodity,
                item => checked((int)item.Quantity),
                StringComparer.OrdinalIgnoreCase
            );

    /// <summary>Targets the Raven observation's original keys, clearing obsolete aliases and removed commodities.</summary>
    public Dictionary<string, int> CreateRavenReplacement(IEnumerable<KeyValuePair<string, int>> edited) =>
        CreateRavenWrite(edited, clearUnlisted: true);

    /// <summary>Updates only the supplied commodities, clearing their aliases while preserving unlisted stock.</summary>
    public Dictionary<string, int> CreateRavenUpdate(IEnumerable<KeyValuePair<string, int>> edited) =>
        CreateRavenWrite(edited, clearUnlisted: false);

    /// <summary>Applies one logical transaction to every existing alias without overwriting concurrent relative changes.</summary>
    public Dictionary<string, int> CreateRavenAdjustment(IReadOnlyDictionary<string, int> adjustments)
    {
        EnsureRavenSource();
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (
            IGrouping<string, KeyValuePair<string, int>> group in adjustments
                .GroupBy(item => CommodityKey(item.Key), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Key.Length > 0)
        )
        {
            long delta = group.Sum(item => (long)item.Value);
            if (delta == 0)
            {
                continue;
            }
            if (delta is < int.MinValue or > int.MaxValue)
            {
                throw new InvalidDataException("Adjusted Fleet Carrier cargo exceeds supported counts.");
            }
            ColonizationInventoryStock? stock = Stock.FirstOrDefault(item => item.Commodity == group.Key);
            foreach (string key in stock?.SourceKeys ?? [group.Key])
            {
                result[key] = (int)delta;
            }
        }
        return result;
    }

    /// <summary>
    /// Builds canonical stock updates and clears only the source aliases covered by the chosen replacement policy.
    /// </summary>
    private Dictionary<string, int> CreateRavenWrite(IEnumerable<KeyValuePair<string, int>> edited, bool clearUnlisted)
    {
        EnsureRavenSource();
        KeyValuePair<string, int>[] edits = edited.ToArray();
        if (edits.Any(item => item.Value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(edited), "Replacement cargo counts cannot be negative.");
        }
        Dictionary<string, int> canonical = NormalizeStock(edits);
        var replacement = Stock
            .Where(item => clearUnlisted || canonical.ContainsKey(item.Commodity))
            .SelectMany(item => item.SourceKeys)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(key => key, _ => 0, StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> item in canonical)
        {
            replacement[item.Key] = item.Value;
        }
        return replacement;
    }

    /// <summary>Rejects attempts to direct remote manifest writes through a local inventory observation.</summary>
    private void EnsureRavenSource()
    {
        if (Source != ColonizationInventorySource.Raven)
        {
            throw new InvalidOperationException("A local inventory observation cannot write a Raven manifest.");
        }
    }

    /// <summary>Widens manifest quantities for shared stock calculations without changing their source keys.</summary>
    private static IEnumerable<KeyValuePair<string, long>> AsLong(IEnumerable<KeyValuePair<string, int>> cargo) =>
        cargo.Select(item => new KeyValuePair<string, long>(item.Key, item.Value));

    /// <summary>
    /// Treats aliases as alternate reports of the same stock, retaining their maximum quantity and exact keys.
    /// </summary>
    private static ColonizationInventoryStock[] ReadStock(IEnumerable<KeyValuePair<string, long>> cargo) =>
        cargo
            .GroupBy(item => CommodityKey(item.Key), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0)
            .Select(group => new ColonizationInventoryStock(
                group.Key,
                Math.Max(0, group.Max(item => item.Value)),
                group.Select(item => item.Key).Distinct(StringComparer.Ordinal).ToArray()
            ))
            .ToArray();
}
