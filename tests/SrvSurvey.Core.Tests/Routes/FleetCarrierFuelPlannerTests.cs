using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Tests.Routes;

public sealed class FleetCarrierFuelPlannerTests
{
    /// <summary>Tank fuel decreases between occasional full transfers, conserving total tritium.</summary>
    [Fact]
    public void DefersFullTransfersUntilTheNextJumpCannotBeFunded()
    {
        FollowRouteHop[] original = CreateHops(1000, 2500, [0, 300, 300, 300, 300, 300, 300, 300]);
        FollowRouteHop[] before = original.ToArray();

        FleetCarrierFuelPlan plan = FleetCarrierFuelPlanner.Calculate(original);

        Assert.True(plan.HasRequiredData);
        Assert.Null(plan.UnfundedHopIndex);
        Assert.Equal(
            [1000, 700, 400, 1000, 700, 400, 1000, 700],
            plan.Hops.Select(hop => hop.Carrier!.FuelRemainingTonnes)
        );
        Assert.Equal(
            [2500, 2500, 2500, 1600, 1600, 1600, 700, 700],
            plan.Hops.Select(hop => hop.Carrier!.TritiumInMarketTonnes)
        );
        Assert.Equal(
            [3, 6],
            plan.Hops.Select((hop, index) => (hop, index))
                .Where(row => row.hop.Carrier!.MustRestock)
                .Select(row => row.index)
        );
        Assert.Equal(
            [900, 900],
            plan.Hops.Where(hop => hop.Carrier!.MustRestock).Select(hop => hop.Carrier!.RestockAmountTonnes)
        );
        Assert.Equal(before, original);
        for (int index = 0; index < plan.Hops.Count; index++)
        {
            FollowRouteCarrierHop carrier = plan.Hops[index].Carrier!;
            Assert.Equal(3500 - 300 * index, carrier.FuelRemainingTonnes + carrier.TritiumInMarketTonnes);
            Assert.Equal(original[index].Carrier!.FuelUsedTonnes, carrier.FuelUsedTonnes);
        }
    }

    /// <summary>A partial final transfer spends only available stock; later fuel is never fabricated.</summary>
    [Fact]
    public void UsesRemainingMarketStockThenFlagsTheFirstUnfundedArrival()
    {
        FleetCarrierFuelPlan plan = FleetCarrierFuelPlanner.Calculate(CreateHops(200, 350, [0, 300, 100, 200, 100]));

        Assert.Equal(3, plan.UnfundedHopIndex);
        Assert.Equal([550, 250, 150, null, null], plan.Hops.Select(hop => hop.Carrier!.FuelRemainingTonnes));
        Assert.All(plan.Hops, hop => Assert.Equal(0, hop.Carrier!.TritiumInMarketTonnes));
        Assert.Equal(350, plan.Hops[0].Carrier!.RestockAmountTonnes);
        Assert.True(plan.Hops[0].Carrier!.MustRestock);
        Assert.All(
            plan.Hops.Skip(1),
            hop =>
            {
                Assert.False(hop.Carrier!.MustRestock);
                Assert.Null(hop.Carrier.RestockAmountTonnes);
            }
        );
    }

    /// <summary>Exact jump fuel and final arrivals do not trigger unnecessary transfers.</summary>
    [Theory]
    [InlineData(300d, 300d, 0d)]
    [InlineData(1000d, 990d, 10d)]
    [InlineData(0d, 0d, 0d)]
    public void DoesNotTransferForAnAffordableJumpOrAfterTheFinalJump(
        double startingFuel,
        double cost,
        double remaining
    )
    {
        FleetCarrierFuelPlan plan = FleetCarrierFuelPlanner.Calculate(CreateHops(startingFuel, 2000, [0, cost]));

        Assert.Null(plan.UnfundedHopIndex);
        Assert.Equal(remaining, plan.Hops[1].Carrier!.FuelRemainingTonnes);
        Assert.All(plan.Hops, hop => Assert.False(hop.Carrier!.MustRestock));
        Assert.All(plan.Hops, hop => Assert.Equal(2000, hop.Carrier!.TritiumInMarketTonnes));
    }

    /// <summary>Invalid or incomplete source quantities leave the original data available for display.</summary>
    [Theory]
    [InlineData(null, 0d, 100d)]
    [InlineData(1000d, null, 100d)]
    [InlineData(1000d, 500d, null)]
    [InlineData(-1d, 500d, 100d)]
    [InlineData(1001d, 500d, 100d)]
    [InlineData(1000d, -1d, 100d)]
    [InlineData(1000d, 500d, -1d)]
    [InlineData(double.NaN, 500d, 100d)]
    [InlineData(1000d, double.PositiveInfinity, 100d)]
    [InlineData(1000d, 500d, double.NaN)]
    public void RejectsUnknownOrInvalidFuelData(double? tank, double? market, double? cost)
    {
        FollowRouteHop[] original = CreateHops(tank, market, [0, cost]);

        FleetCarrierFuelPlan plan = FleetCarrierFuelPlanner.Calculate(original);

        Assert.False(plan.HasRequiredData);
        Assert.Same(original, plan.Hops);
        Assert.Null(plan.UnfundedHopIndex);
    }

    /// <summary>Generated Spansh restock warnings disappear with their replaced recommendations.</summary>
    [Fact]
    public void ReplacesRestockGuidanceWithoutChangingOtherNotesOrLogistics()
    {
        FollowRouteHop[] original = CreateHops(1000, 500, [0, 300]);
        original[0] = original[0] with { Notes = "Keep this note\r\nCarrier tritium restock required" };
        original[1] = original[1] with { Notes = "Carrier tritium restock required" };

        FleetCarrierFuelPlan plan = FleetCarrierFuelPlanner.Calculate(original);

        Assert.Equal("Keep this note", plan.Hops[0].Notes);
        Assert.Null(plan.Hops[1].Notes);
        Assert.Equal(original[0].Name, plan.Hops[0].Name);
        Assert.Equal(original[0].Carrier!.DistanceLy, plan.Hops[0].Carrier!.DistanceLy);
        Assert.True(plan.Hops[0].Carrier!.HasIcyRing);
        Assert.False(plan.Hops[0].Carrier!.MustRestock);
        Assert.True(original[0].Carrier!.MustRestock);
        Assert.Contains("restock", original[0].Notes);
    }

    /// <summary>Missing stops and oversized jumps cannot produce a plausible funded itinerary.</summary>
    [Fact]
    public void HandlesEmptyMissingCarrierAndOversizedJumpInputs()
    {
        Assert.Throws<ArgumentNullException>(() => FleetCarrierFuelPlanner.Calculate(null!));
        Assert.False(FleetCarrierFuelPlanner.Calculate([]).HasRequiredData);
        FollowRouteHop[] hops = CreateHops(1000, 500, [0, 1200]);
        Assert.Equal(1, FleetCarrierFuelPlanner.Calculate(hops).UnfundedHopIndex);
        hops[1] = hops[1] with { Carrier = null };
        Assert.False(FleetCarrierFuelPlanner.Calculate(hops).HasRequiredData);
        hops[0] = hops[0] with { Carrier = null };
        Assert.False(FleetCarrierFuelPlanner.Calculate(hops).HasRequiredData);
    }

    private static FollowRouteHop[] CreateHops(double? tank, double? market, double?[] costs) =>
        costs
            .Select(
                (cost, index) =>
                    new FollowRouteHop(
                        "Stop " + index,
                        index + 1,
                        null,
                        null,
                        false,
                        false,
                        Carrier: new(100, 1000, tank, market, cost, true, false, true, 999)
                    )
            )
            .ToArray();
}
