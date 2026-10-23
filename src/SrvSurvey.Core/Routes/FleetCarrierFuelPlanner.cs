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

    /// <summary>Builds one pass from a captured observation; journal updates never mutate this forecast.</summary>
    public static FleetCarrierFuelPlan Calculate(
        IReadOnlyList<FollowRouteHop> hops,
        FleetCarrierFuelSnapshot fuel,
        bool restockOnlyWhenLow,
        int startIndex = 0
    )
    {
        ArgumentNullException.ThrowIfNull(hops);
        ArgumentNullException.ThrowIfNull(fuel);
        if (!fuel.IsComplete || startIndex < 0 || startIndex >= hops.Count)
        {
            return new(hops.Select(WithoutForecast).ToArray(), false, null);
        }
        double[] distances = new double[hops.Count];
        for (int index = startIndex + 1; index < hops.Count; index++)
        {
            if (
                hops[index - 1].DistanceTo(hops[index]) is not { } distance
                || !double.IsFinite(distance)
                || distance > 500.01
            )
            {
                return new(hops.Select(WithoutForecast).ToArray(), false, null);
            }
            distances[index] = distance;
        }
        PassResult pass = Simulate(hops, distances, fuel, restockOnlyWhenLow, startIndex, 0);
        if (pass.UnfundedIndex is null)
        {
            return new(pass.Hops, true, null, 0, pass.Consumed);
        }
        double? additional = FindAdditionalFuel(hops, distances, fuel, restockOnlyWhenLow, startIndex);
        return new(
            pass.Hops,
            true,
            pass.UnfundedIndex,
            additional,
            additional is { } amount ? fuel.TankTonnes + fuel.StoredTritiumTonnes + amount : null,
            additional > fuel.Capacity - fuel.UsedCapacityTonnes
        );
    }

    /// <summary>Uses the same mass inputs and rounded tritium formula as Spansh's carrier router.</summary>
    public static double JumpFuel(FollowRouteCarrierKind kind, double distance, double usedCapacity, double tankFuel)
    {
        double mass = kind == FollowRouteCarrierKind.Squadron ? 15000 : 25000;
        return Math.Round(5 + distance * (mass + usedCapacity + tankFuel) / 200000, MidpointRounding.AwayFromZero);
    }

    private static double? FindAdditionalFuel(
        IReadOnlyList<FollowRouteHop> hops,
        double[] distances,
        FleetCarrierFuelSnapshot fuel,
        bool low,
        int startIndex
    )
    {
        int upper = 1;
        while (upper < 1000000 && Simulate(hops, distances, fuel, low, startIndex, upper).UnfundedIndex is not null)
        {
            upper *= 2;
        }
        if (upper >= 1000000)
        {
            // A route too long to carry all its fuel needs intermediate supplies. Quote a conservative budget.
            double budget = distances
                .Skip(startIndex + 1)
                .Sum(distance => JumpFuel(fuel.Kind, distance, fuel.UsedCapacityTonnes!.Value, TankCapacityTonnes));
            return Math.Max(1, Math.Ceiling(budget - fuel.TankTonnes!.Value - fuel.StoredTritiumTonnes!.Value));
        }
        int lower = 0;
        while (lower + 1 < upper)
        {
            int middle = lower + (upper - lower) / 2;
            if (Simulate(hops, distances, fuel, low, startIndex, middle).UnfundedIndex is null)
            {
                upper = middle;
            }
            else
            {
                lower = middle;
            }
        }
        return upper;
    }

    private static PassResult Simulate(
        IReadOnlyList<FollowRouteHop> hops,
        double[] distances,
        FleetCarrierFuelSnapshot fuel,
        bool low,
        int startIndex,
        double extra
    )
    {
        double tank = fuel.TankTonnes!.Value;
        double stock = fuel.StoredTritiumTonnes!.Value + extra;
        double used = fuel.UsedCapacityTonnes!.Value + extra;
        double consumed = 0;
        FollowRouteHop[] rows = hops.Select(WithoutForecast).ToArray();
        double remaining = distances.Sum();
        double arrivalCost = 0;
        for (int index = startIndex; index < hops.Count; index++)
        {
            double cost = index + 1 < hops.Count ? JumpFuel(fuel.Kind, distances[index + 1], used, tank) : 0;
            double transfer =
                index + 1 < hops.Count && (!low || tank < cost) ? Math.Min(TankCapacityTonnes - tank, stock) : 0;
            tank += transfer;
            stock -= transfer;
            used -= transfer;
            FollowRouteHop hop = rows[index];
            FollowRouteCarrierHop carrier = hop.Carrier ?? new(null, null, null, null, null, false, false, false, null);
            rows[index] = hop with
            {
                Carrier = carrier with
                {
                    DistanceLy = distances[index],
                    RemainingLy = remaining,
                    FuelRemainingTonnes = tank,
                    TritiumInMarketTonnes = stock,
                    FuelUsedTonnes = arrivalCost,
                    MustRestock = transfer > 0,
                    RestockAmountTonnes = transfer > 0 ? transfer : null,
                },
            };
            if (index + 1 < hops.Count && cost > tank)
            {
                return new(rows, index + 1, consumed);
            }
            tank -= cost;
            consumed += cost;
            arrivalCost = cost;
            if (index + 1 < hops.Count)
            {
                remaining -= distances[index + 1];
            }
        }
        return new(rows, null, consumed);
    }

    private sealed record PassResult(IReadOnlyList<FollowRouteHop> Hops, int? UnfundedIndex, double Consumed);

    /// <summary>Retains system information without presenting forward-only fuel forecasts on a reverse pass.</summary>
    public static FollowRouteHop WithoutForecast(FollowRouteHop hop)
    {
        ArgumentNullException.ThrowIfNull(hop);
        return hop.Carrier is not { } carrier
            ? hop
            : hop with
            {
                Carrier = carrier with
                {
                    DistanceLy = null,
                    RemainingLy = null,
                    FuelRemainingTonnes = null,
                    TritiumInMarketTonnes = null,
                    FuelUsedTonnes = null,
                    MustRestock = false,
                    RestockAmountTonnes = null,
                },
                Notes = RemoveImportedRestockNote(hop.Notes),
            };
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
    int? UnfundedHopIndex,
    double? AdditionalFuelTonnes = null,
    double? FuelRequiredTonnes = null,
    bool RequiresFuelStops = false
);
