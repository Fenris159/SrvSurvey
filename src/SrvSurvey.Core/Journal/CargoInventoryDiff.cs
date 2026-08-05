namespace SrvSurvey.Core.Journal;

/// <summary>
/// Pure inventory snapshot/diff helpers for squadron fleet-carrier cargo tracking.
/// Kept free of UI/logging so unit tests can cover the arithmetic used by
/// <see cref="CargoInventoryState"/>.
/// Commodity names are matched case-insensitively to match journal mutation handlers.
/// </summary>
public static class CargoInventoryDiff
{
    /// <summary>Comparer used for all cargo name maps (matches CollectCargo / CargoTransfer lookups).</summary>
    public static StringComparer NameComparer { get; } = StringComparer.OrdinalIgnoreCase;

    /// <summary>Create an empty name→count map with case-insensitive keys.</summary>
    public static Dictionary<string, int> CreateCountMap() => new(NameComparer);

    /// <summary>Copy commodity counts from inventory items into a destination dictionary (cleared first).</summary>
    public static void CopyFromInventory(
        Dictionary<string, int> destination,
        IEnumerable<CargoItem>? inventory)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        if (inventory is null)
        {
            return;
        }

        foreach (var entry in inventory)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            destination[entry.Name] = entry.Count;
        }
    }

    /// <summary>Copy name→count pairs into a destination dictionary (cleared first).</summary>
    public static void CopyFromCounts(
        Dictionary<string, int> destination,
        IReadOnlyDictionary<string, int> source)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);
        destination.Clear();
        foreach (var pair in source)
        {
            destination[pair.Key] = pair.Value;
        }
    }

    /// <summary>
    /// Ship cargo delta: <paramref name="after"/> − <paramref name="before"/> (non-zero entries only).
    /// Missing commodities in <paramref name="after"/> contribute a negative delta equal to their before count.
    /// Names are compared case-insensitively.
    /// </summary>
    public static Dictionary<string, int> Compute(
        IReadOnlyDictionary<string, int> before,
        IReadOnlyDictionary<string, int> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var diffs = CreateCountMap();
        // O(after) name set so removed-commodity detection is O(before), not O(before×after).
        var afterNames = new HashSet<string>(after.Count, NameComparer);
        foreach (var entry in after)
        {
            afterNames.Add(entry.Key);
            var delta = entry.Value - before.GetValueOrDefault(entry.Key);
            if (delta != 0)
            {
                diffs[entry.Key] = delta;
            }
        }

        foreach (var entry in before.Where(item => !afterNames.Contains(item.Key)))
        {
            diffs[entry.Key] = -entry.Value;
        }

        return diffs;
    }

    /// <summary>
    /// Ship cargo delta from an after inventory list.
    /// Missing commodities contribute a negative delta equal to their before count.
    /// </summary>
    public static Dictionary<string, int> Compute(
        IReadOnlyDictionary<string, int> before,
        IReadOnlyList<CargoItem>? after)
    {
        ArgumentNullException.ThrowIfNull(before);
        var afterMap = ToCountMap(after);
        return Compute(before, afterMap);
    }

    /// <summary>Invert a ship cargo delta into a fleet-carrier cargo delta (multiply by -1).</summary>
    public static Dictionary<string, int> InvertForFleetCarrier(
        IReadOnlyDictionary<string, int> shipDiff)
    {
        ArgumentNullException.ThrowIfNull(shipDiff);
        return shipDiff.ToDictionary(
            pair => pair.Key,
            pair => -pair.Value,
            NameComparer);
    }

    /// <summary>Map inventory items to name → count for logging/debug dumps.</summary>
    public static Dictionary<string, int> ToCountMap(IReadOnlyList<CargoItem>? inventory)
    {
        var map = CreateCountMap();
        if (inventory is null || inventory.Count == 0)
        {
            return map;
        }

        foreach (var entry in inventory)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            map[entry.Name] = entry.Count;
        }

        return map;
    }
}
