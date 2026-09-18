using SrvSurvey.Core.Frontier;

namespace SrvSurvey.Core.Colonization;

/// <summary>
/// EDMC-parity policy for seeding RavenColonial fleet-carrier cargo from Frontier CAPI.
/// CAPI is laggy: accept at most one full-manifest seed per carrier per session, then
/// rely on journal deltas. Reject while docked (market/journal is fresher).
/// </summary>
public static class ColonizationFleetCarrierCapiCargoSeeder
{
    public static IReadOnlyDictionary<string, int> CreateCargoTotals(FrontierCarrierSnapshot carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (FrontierInventorySnapshot item in carrier.Cargo)
        {
            string commodity = ColonizationConstructionState.NormalizeCommodityName(item.Name);
            if (commodity.Length == 0 || item.Quantity <= 0)
            {
                continue;
            }

            totals.TryGetValue(commodity, out int current);
            totals[commodity] = current + item.Quantity;
        }

        return totals;
    }

    public static bool ManifestsDiffer(IReadOnlyDictionary<string, int>? left, IReadOnlyDictionary<string, int>? right)
    {
        Dictionary<string, int> a = Normalize(left);
        Dictionary<string, int> b = Normalize(right);
        if (a.Count != b.Count)
        {
            return true;
        }

        foreach (KeyValuePair<string, int> pair in a)
        {
            if (!b.TryGetValue(pair.Key, out int other) || other != pair.Value)
            {
                return true;
            }
        }

        return false;
    }

    public static (bool Accepted, string Reason) ShouldAcceptSnapshot(
        long marketId,
        bool isDocked,
        ISet<long> alreadySeededMarketIds,
        DateTimeOffset? capiTimestamp,
        IReadOnlyDictionary<string, int>? existingLinkedCargo
    )
    {
        ArgumentNullException.ThrowIfNull(alreadySeededMarketIds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(marketId);

        if (isDocked)
        {
            return (false, "player_docked");
        }

        if (alreadySeededMarketIds.Contains(marketId))
        {
            return (false, "already_received");
        }

        if (capiTimestamp is null)
        {
            return (false, "missing_capi_timestamp");
        }

        Dictionary<string, int> existing = Normalize(existingLinkedCargo);
        if (existing.Count == 0)
        {
            return (true, "server_cargo_missing");
        }

        return (true, "capi_accepted");
    }

    public static long? ResolveLinkedMarketId(
        FrontierCarrierSnapshot carrier,
        IEnumerable<ColonizationFleetCarrier> linkedCarriers
    )
    {
        ArgumentNullException.ThrowIfNull(carrier);
        ArgumentNullException.ThrowIfNull(linkedCarriers);

        if (carrier.Market?.Id is > 0 and long marketId)
        {
            return linkedCarriers.Any(c => c.MarketId == marketId) ? marketId : null;
        }

        string callsign = carrier.Callsign?.Trim() ?? string.Empty;
        if (callsign.Length == 0)
        {
            return null;
        }

        return linkedCarriers
            .FirstOrDefault(c => string.Equals(c.Name, callsign, StringComparison.OrdinalIgnoreCase))
            ?.MarketId;
    }

    private static Dictionary<string, int> Normalize(IReadOnlyDictionary<string, int>? cargo)
    {
        if (cargo is null || cargo.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, int> pair in cargo)
        {
            string commodity = ColonizationConstructionState.NormalizeCommodityName(pair.Key);
            if (commodity.Length == 0 || pair.Value <= 0)
            {
                continue;
            }

            normalized.TryGetValue(commodity, out int current);
            normalized[commodity] = current + pair.Value;
        }

        return normalized;
    }
}
