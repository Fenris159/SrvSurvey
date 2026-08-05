using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

public static class ColonizationFleetCarrierCargoSynchronizer
{
    /// <summary>StationServices flag used to detect squadron fleet carriers.</summary>
    public const string StationServiceSquadronBank = "squadronBank";

    public static IReadOnlyDictionary<string, int> CreateJournalAdjustment(
        JournalEventEnvelope journalEvent,
        ColonizationDockingSnapshot dock,
        bool isInMainShip,
        bool preferShipCargoDiffForSquadron = true)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        ArgumentNullException.ThrowIfNull(dock);
        if (!string.Equals(
                dock.StationType,
                "FleetCarrier",
                StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        return journalEvent.EventName switch
        {
            "MarketBuy" => CreateMarketAdjustment(
                journalEvent.Payload,
                dock,
                sign: -1),
            "MarketSell" => CreateMarketAdjustment(
                journalEvent.Payload,
                dock,
                sign: 1),
            // Personal linked FCs always use journal transfer deltas.
            // Squadron carriers prefer ship-cargo GetDiff when available; fall back
            // to journal transfers when shared cargo is suppressed / no inventory.
            "CargoTransfer" when isInMainShip
                && !(IsSquadronFleetCarrier(dock) && preferShipCargoDiffForSquadron) =>
                CreateTransferAdjustment(journalEvent.Payload),
            _ => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// True when docked station services explicitly list squadron bank.
    /// Missing/empty StationServices is treated as non-squadron.
    /// </summary>
    public static bool IsSquadronFleetCarrier(ColonizationDockingSnapshot dock)
    {
        ArgumentNullException.ThrowIfNull(dock);
        return dock.StationServices.Contains(
            StationServiceSquadronBank,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Invert a ship cargo delta into a fleet-carrier cargo adjustment for Raven Colonial.
    /// Ship lost cargo (negative) means the FC gained it (positive).
    /// </summary>
    public static IReadOnlyDictionary<string, int> CreateSquadronCargoDiffAdjustment(
        IReadOnlyDictionary<string, int> shipDiff)
    {
        ArgumentNullException.ThrowIfNull(shipDiff);
        return CargoInventoryDiff.InvertForFleetCarrier(shipDiff);
    }

    public static IReadOnlyDictionary<string, int> CreateMarketReplacement(
        MarketSnapshot market,
        ColonizationFleetCarrier carrier)
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(carrier);
        if (market.MarketId != carrier.MarketId)
        {
            throw new ArgumentException(
                "The market snapshot does not belong to the Fleet Carrier.",
                nameof(market));
        }

        var currentCargo = carrier.Cargo
            .GroupBy(
                pair => ColonizationConstructionState.NormalizeCommodityName(
                    pair.Key),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(pair => Math.Max(0, pair.Value)),
                StringComparer.OrdinalIgnoreCase);
        var replacement = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in market.Items)
        {
            var commodity = item.Commodity;
            if (commodity.Length == 0)
            {
                continue;
            }

            var stock = Math.Max(0, item.Stock);
            var tracked = currentCargo.GetValueOrDefault(commodity);
            if ((item.Producer && tracked != stock)
                || (!item.Producer && !item.Consumer && tracked > 0))
            {
                replacement[commodity] = stock;
            }
        }

        return replacement;
    }

    private static IReadOnlyDictionary<string, int> CreateMarketAdjustment(
        System.Text.Json.JsonElement root,
        ColonizationDockingSnapshot dock,
        int sign)
    {
        var marketId = GetInt64(root, "MarketID");
        if (marketId != dock.MarketId)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var commodity = ColonizationConstructionState.NormalizeCommodityName(
            GetString(root, "Type"));
        var count = GetInt32(root, "Count");
        if (commodity.Length == 0 || count is not > 0)
        {
            throw new InvalidDataException(
                "The Fleet Carrier market event has invalid commodity data.");
        }

        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [commodity] = sign * count.Value,
        };
    }

    private static IReadOnlyDictionary<string, int> CreateTransferAdjustment(
        System.Text.Json.JsonElement root)
    {
        if (!root.TryGetProperty("Transfers", out var transfers)
            || transfers.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The Fleet Carrier cargo transfer has no Transfers array.");
        }

        var result = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var transfer in transfers.EnumerateArray())
        {
            var commodity = ColonizationConstructionState
                .NormalizeCommodityName(GetString(transfer, "Type"));
            var count = GetInt32(transfer, "Count");
            var direction = GetString(transfer, "Direction");
            if (commodity.Length == 0 || count is not > 0)
            {
                throw new InvalidDataException(
                    "The Fleet Carrier cargo transfer has invalid commodity data.");
            }

            int delta;
            if (string.Equals(
                    direction,
                    "tocarrier",
                    StringComparison.OrdinalIgnoreCase))
            {
                delta = count.Value;
            }
            else if (string.Equals(
                         direction,
                         "toship",
                         StringComparison.OrdinalIgnoreCase))
            {
                delta = -count.Value;
            }
            else
            {
                continue;
            }

            AddDelta(result, commodity, delta);
        }

        return result;
    }

    private static void AddDelta(
        IDictionary<string, int> result,
        string commodity,
        int delta)
    {
        result.TryGetValue(commodity, out var current);
        var updated = (long)current + delta;
        if (updated is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidDataException(
                "The Fleet Carrier cargo transfer exceeds supported counts.");
        }

        if (updated == 0)
        {
            result.Remove(commodity);
        }
        else
        {
            result[commodity] = (int)updated;
        }
    }

    private static string? GetString(
        System.Text.Json.JsonElement root,
        string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static int? GetInt32(
        System.Text.Json.JsonElement root,
        string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.TryGetInt32(out var result)
                ? result
                : null;
    }

    private static long? GetInt64(
        System.Text.Json.JsonElement root,
        string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.TryGetInt64(out var result)
                ? result
                : null;
    }
}
