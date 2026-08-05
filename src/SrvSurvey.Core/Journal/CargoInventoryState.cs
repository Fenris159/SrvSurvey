using System.Text.Json;

namespace SrvSurvey.Core.Journal;

/// <summary>
/// Live ship cargo projection from journal deltas and Cargo.json snapshots.
/// Inventory / lastInventory updates are serialized so squadron-FC
/// diff tracking cannot race with CargoTransfer mutations or companion-file reloads.
/// </summary>
public sealed class CargoInventoryState
{
    /// <summary>Lock for all Inventory / lastInventory readers and writers.</summary>
    private readonly Lock syncRoot = new();

    private readonly Dictionary<string, CargoItemState> inventory = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> lastInventory =
        CargoInventoryDiff.CreateCountMap();
    private DateTimeOffset timestamp;
    private string eventName = "Cargo";
    private string vessel = string.Empty;
    private bool hasState;

    /// <summary>
    /// When true, <see cref="Reset"/> / cargo-event replaces will not overwrite
    /// <see cref="lastInventory"/>. Set by <see cref="CaptureBeforeSnapshot"/> so CargoTransfer
    /// can freeze the true before-state before it mutates live inventory; cleared by
    /// <see cref="GetDiff"/>.
    /// </summary>
    private bool preserveLastInventory;

    /// <summary>True when a transfer path has frozen lastInventory until GetDiff consumes it.</summary>
    public bool HasPreservedSnapshot
    {
        get
        {
            lock (syncRoot)
            {
                return preserveLastInventory;
            }
        }
    }

    /// <summary>
    /// Capture the current inventory as the "before" snapshot for a later GetDiff, and hold it
    /// across subsequent Reset/file reloads. Call this before any mutation that would otherwise
    /// corrupt the before-state (e.g. CargoTransfer on a squadron FC).
    /// </summary>
    public void CaptureBeforeSnapshot()
    {
        lock (syncRoot)
        {
            CopyCurrentToLastInventoryUnlocked();
            preserveLastInventory = true;
        }
    }

    /// <summary>
    /// Drop a held before-snapshot without computing a diff (e.g. when skipNextCargoEvent
    /// intentionally suppresses squadron supplyFC processing).
    /// </summary>
    public void ClearPreservedSnapshot()
    {
        lock (syncRoot)
        {
            preserveLastInventory = false;
        }
    }

    /// <summary>
    /// Ship cargo delta: current inventory − lastInventory (non-zero entries only).
    /// Rebases lastInventory to the current counts and clears any preserved snapshot so the
    /// computed diff is consumed once. Call while the new inventory is already applied;
    /// release the lock before network I/O or logging.
    /// </summary>
    public Dictionary<string, int> GetDiff()
    {
        lock (syncRoot)
        {
            var after = CreateCountMapUnlocked();
            var diffs = CargoInventoryDiff.Compute(lastInventory, after);
            // Consume once: subsequent GetDiff calls baseline from this after-state.
            CargoInventoryDiff.CopyFromCounts(lastInventory, after);
            preserveLastInventory = false;
            return diffs;
        }
    }

    public bool Reset(CargoSnapshot? snapshot)
    {
        lock (syncRoot)
        {
            if (snapshot is null)
            {
                if (!hasState && inventory.Count == 0)
                {
                    return false;
                }

                inventory.Clear();
                timestamp = default;
                eventName = "Cargo";
                vessel = string.Empty;
                hasState = false;
                // Commander/context clears must not leave a frozen before-snapshot forever.
                preserveLastInventory = false;
                lastInventory.Clear();
                return true;
            }

            if (!preserveLastInventory)
            {
                CopyCurrentToLastInventoryUnlocked();
            }

            var replacement = CreateInventory(snapshot.Inventory);
            var changed = !hasState
                || timestamp != snapshot.Timestamp
                || !string.Equals(eventName, snapshot.EventName, StringComparison.Ordinal)
                || !string.Equals(vessel, snapshot.Vessel, StringComparison.Ordinal)
                || !InventoryEquals(inventory, replacement);
            inventory.Clear();
            foreach (var item in replacement)
            {
                inventory[item.Key] = item.Value;
            }

            timestamp = snapshot.Timestamp;
            eventName = snapshot.EventName;
            vessel = snapshot.Vessel;
            hasState = true;
            return changed;
        }
    }

    public bool Apply(
        JournalEventEnvelope journalEvent,
        bool isInSrv = false)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        lock (syncRoot)
        {
            var root = journalEvent.Payload;
            var changed = journalEvent.EventName switch
            {
                "CollectCargo" => ApplyDelta(
                    GetString(root, "Type"),
                    GetString(root, "Type_Localised"),
                    1),
                "EjectCargo" => ApplyDelta(
                    GetString(root, "Type"),
                    GetString(root, "Type_Localised"),
                    -Math.Max(0, GetInt32(root, "Count") ?? 0)),
                "MarketBuy" => ApplyDelta(
                    GetString(root, "Type"),
                    GetString(root, "Type_Localised"),
                    Math.Max(0, GetInt32(root, "Count") ?? 0)),
                "MarketSell" => ApplyDelta(
                    GetString(root, "Type"),
                    GetString(root, "Type_Localised"),
                    -Math.Max(0, GetInt32(root, "Count") ?? 0)),
                "CargoTransfer" => ApplyTransfers(root, isInSrv),
                "ColonisationContribution" => ApplyContributions(root),
                "Cargo" => ApplyCargoEvent(root),
                _ => false,
            };
            if (!changed)
            {
                return false;
            }

            timestamp = journalEvent.Timestamp ?? timestamp;
            eventName = journalEvent.EventName;
            hasState = true;
            return true;
        }
    }

    public CargoSnapshot? CreateSnapshot()
    {
        lock (syncRoot)
        {
            if (!hasState)
            {
                return null;
            }

            var items = inventory.Values
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(item => new CargoItem(
                    item.Name,
                    item.LocalizedName,
                    item.Count,
                    item.Stolen))
                .ToArray();
            var total = (int)Math.Min(
                int.MaxValue,
                items.Sum(item => (long)item.Count));
            return new CargoSnapshot(
                timestamp,
                eventName,
                vessel,
                total,
                items);
        }
    }

    private bool ApplyCargoEvent(JsonElement root)
    {
        if (!root.TryGetProperty("Inventory", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (!preserveLastInventory)
        {
            CopyCurrentToLastInventoryUnlocked();
        }

        var replacement = CreateInventory(items);
        var updatedVessel = GetString(root, "Vessel") ?? vessel;
        if (hasState
            && string.Equals(vessel, updatedVessel, StringComparison.Ordinal)
            && InventoryEquals(inventory, replacement))
        {
            return false;
        }

        inventory.Clear();
        foreach (var item in replacement)
        {
            inventory[item.Key] = item.Value;
        }

        vessel = updatedVessel;
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
                GetString(transfer, "Type_Localised"),
                delta);
        }

        return changed;
    }

    private bool ApplyContributions(JsonElement root)
    {
        if (!root.TryGetProperty("Contributions", out var contributions)
            || contributions.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var changed = false;
        foreach (var contribution in contributions.EnumerateArray())
        {
            changed |= ApplyDelta(
                NormalizeCommodityName(GetString(contribution, "Name")),
                GetString(contribution, "Name_Localised"),
                -Math.Max(0, GetInt32(contribution, "Amount") ?? 0));
        }

        return changed;
    }

    private bool ApplyDelta(
        string? name,
        string? localizedName,
        int delta)
    {
        if (string.IsNullOrWhiteSpace(name) || delta == 0)
        {
            return false;
        }

        var normalized = name.Trim();
        var previous = inventory.GetValueOrDefault(normalized);
        var previousCount = previous?.Count ?? 0;
        var nextCount = (int)Math.Clamp(
            (long)previousCount + delta,
            0,
            int.MaxValue);
        if (nextCount == previousCount)
        {
            return false;
        }

        if (nextCount == 0)
        {
            inventory.Remove(normalized);
            return true;
        }

        inventory[normalized] = new CargoItemState(
            previous?.Name ?? normalized,
            !string.IsNullOrWhiteSpace(localizedName)
                ? localizedName
                : previous?.LocalizedName,
            nextCount,
            Math.Min(previous?.Stolen ?? 0, nextCount));
        return true;
    }

    private void CopyCurrentToLastInventoryUnlocked()
    {
        lastInventory.Clear();
        foreach (var item in inventory)
        {
            lastInventory[item.Key] = item.Value.Count;
        }
    }

    private Dictionary<string, int> CreateCountMapUnlocked()
    {
        var map = CargoInventoryDiff.CreateCountMap();
        foreach (var item in inventory)
        {
            map[item.Key] = item.Value.Count;
        }

        return map;
    }

    private static Dictionary<string, CargoItemState> CreateInventory(
        IEnumerable<CargoItem> items)
    {
        var replacement = new Dictionary<string, CargoItemState>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            AddSnapshotItem(
                replacement,
                item.Name,
                item.LocalizedName,
                item.Count,
                item.Stolen);
        }

        return replacement;
    }

    private static Dictionary<string, CargoItemState> CreateInventory(
        JsonElement items)
    {
        var replacement = new Dictionary<string, CargoItemState>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.EnumerateArray())
        {
            AddSnapshotItem(
                replacement,
                GetString(item, "Name"),
                GetString(item, "Name_Localised"),
                GetInt32(item, "Count") ?? 0,
                GetInt32(item, "Stolen") ?? 0);
        }

        return replacement;
    }

    private static void AddSnapshotItem(
        IDictionary<string, CargoItemState> target,
        string? name,
        string? localizedName,
        int count,
        int stolen)
    {
        if (string.IsNullOrWhiteSpace(name) || count <= 0)
        {
            return;
        }

        var normalized = name.Trim();
        target.TryGetValue(normalized, out var previous);
        var nextCount = (int)Math.Min(
            int.MaxValue,
            (long)(previous?.Count ?? 0) + count);
        var nextStolen = (int)Math.Min(
            nextCount,
            Math.Min(
                int.MaxValue,
                (long)(previous?.Stolen ?? 0) + Math.Max(0, stolen)));
        target[normalized] = new CargoItemState(
            previous?.Name ?? normalized,
            !string.IsNullOrWhiteSpace(localizedName)
                ? localizedName
                : previous?.LocalizedName,
            nextCount,
            nextStolen);
    }

    private static bool InventoryEquals(
        IReadOnlyDictionary<string, CargoItemState> left,
        IReadOnlyDictionary<string, CargoItemState> right)
    {
        return left.Count == right.Count
            && left.All(item => right.TryGetValue(item.Key, out var value)
                && item.Value == value);
    }

    private static string NormalizeCommodityName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var normalized = name.Trim();
        if (normalized.StartsWith('$'))
        {
            normalized = normalized[1..];
        }

        if (normalized.EndsWith("_name;", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^6];
        }

        return normalized.ToLowerInvariant();
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

    private sealed record CargoItemState(
        string Name,
        string? LocalizedName,
        int Count,
        int Stolen);
}
