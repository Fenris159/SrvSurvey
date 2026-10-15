namespace SrvSurvey.Core.Routes;

/// <summary>Reinterprets reported jump costs without changing the original Spansh itinerary.</summary>
public static class FleetCarrierFuelPlanner
{
    private const double TankCapacityTonnes = 1000;
    private const string ImportedRestockNote = "Carrier tritium restock required";

    /// <summary>
    /// Spends fuel on arrival and transfers market tritium only when the next jump needs it.
    /// Balances include that stop's transfer; unfunded arrivals have no predicted tank balance.
    /// </summary>
    public static FleetCarrierFuelPlan Calculate(IReadOnlyList<FollowRouteHop> hops)
    {
        ArgumentNullException.ThrowIfNull(hops);
        if (!HasRequiredData(hops))
        {
            return new(hops, false, null);
        }

        double tank = hops[0].Carrier!.FuelRemainingTonnes!.Value;
        double market = hops[0].Carrier!.TritiumInMarketTonnes!.Value;
        var result = new FollowRouteHop[hops.Count];
        int? unfundedHop = null;
        for (int index = 0; index < hops.Count; index++)
        {
            FollowRouteHop hop = hops[index];
            FollowRouteCarrierHop carrier = hop.Carrier!;
            double cost = index == 0 ? 0 : carrier.FuelUsedTonnes!.Value;
            if (unfundedHop is null && cost > tank)
            {
                unfundedHop = index;
            }

            double transfer = 0;
            if (unfundedHop is null)
            {
                tank -= cost;
                transfer = CalculateTransfer(hops, index, tank, market);
                tank += transfer;
                market -= transfer;
            }

            result[index] = hop with
            {
                Notes = RemoveImportedRestockNote(hop.Notes),
                Carrier = carrier with
                {
                    FuelRemainingTonnes = unfundedHop is null ? tank : null,
                    TritiumInMarketTonnes = market,
                    MustRestock = transfer > 0,
                    RestockAmountTonnes = transfer > 0 ? transfer : null,
                },
            };
        }

        return new(result, true, unfundedHop);
    }

    /// <summary>Fills available tank space from market stock only when the next departure requires it.</summary>
    private static double CalculateTransfer(IReadOnlyList<FollowRouteHop> hops, int index, double tank, double market)
    {
        if (index + 1 >= hops.Count || tank >= hops[index + 1].Carrier!.FuelUsedTonnes!.Value)
        {
            return 0;
        }

        return Math.Min(TankCapacityTonnes - tank, market);
    }

    /// <summary>Requires starting inventories and a usable fuel cost for every subsequent arrival.</summary>
    private static bool HasRequiredData(IReadOnlyList<FollowRouteHop> hops) =>
        hops.Count > 0
        && hops[0].Carrier is { FuelRemainingTonnes: { } tank, TritiumInMarketTonnes: { } market }
        && IsQuantity(tank)
        && tank <= TankCapacityTonnes
        && IsQuantity(market)
        && hops.Skip(1).All(hop => hop.Carrier?.FuelUsedTonnes is { } cost && IsQuantity(cost));

    /// <summary>Checks that a fuel measurement is finite and nonnegative.</summary>
    private static bool IsQuantity(double value) => double.IsFinite(value) && value >= 0;

    /// <summary>Removes the importer-generated warning while preserving other stop notes.</summary>
    private static string? RemoveImportedRestockNote(string? notes)
    {
        if (notes is null)
        {
            return null;
        }

        string remaining = string.Join(
            '\n',
            notes
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => !line.Equals(ImportedRestockNote, StringComparison.Ordinal))
        );
        return string.IsNullOrWhiteSpace(remaining) ? null : remaining;
    }
}

public sealed record FleetCarrierFuelPlan(
    IReadOnlyList<FollowRouteHop> Hops,
    bool HasRequiredData,
    int? UnfundedHopIndex
);
