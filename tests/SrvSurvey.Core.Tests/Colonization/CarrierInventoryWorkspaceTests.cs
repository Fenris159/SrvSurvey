using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class CarrierInventoryWorkspaceTests
{
    /// <summary>
    /// Locks planner, shopping, preview, and Nexus totals to one stock quantity per carrier without consuming ship
    /// stock.
    /// </summary>
    [Fact]
    public void WorkspacesAgreeOnUniqueCarrierStockAndKeepShipStockSeparate()
    {
        var first = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Name = "ABC-123",
            Cargo = new() { ["liquid oxygen"] = 80, ["liquidoxygen"] = 60 },
        };
        var second = new ColonizationFleetCarrier
        {
            MarketId = 43,
            Name = "DEF-456",
            Cargo = new() { ["liquidoxygen"] = 10 },
        };
        ColonizationFleetCarrier[] carriers = [first, first, second];
        var project = new ColonizationProject
        {
            BuildId = "build",
            Commodities = new() { ["liquidoxygen"] = 100 },
            LinkedFleetCarriers = [new() { MarketId = 42 }, new() { MarketId = 43 }],
        };
        ColonizationCommodityPlan plan = ColonizationCommodityPlanner.Create(
            new()
            {
                Projects = [project],
                FleetCarriers = carriers,
                Construction = new(null, null, null, null, null, 100),
                ShipCargo = new(DateTimeOffset.UnixEpoch, "Cargo", "Ship", 5, [new("liquidoxygen", null, 5, 0)]),
            }
        );
        ColonizationCommodityPlanRow row = Assert.Single(plan.Rows);
        Assert.Equal(90, row.OnFleetCarriers);
        Assert.Equal(5, row.InShip);
        Assert.Equal(
            10,
            Assert
                .Single(ColonizationShopping.SubtractCarrierCargo(ColonizationShopping.Aggregate([project]), carriers))
                .Required
        );
        var preview = new ColonizationProjectPreview(
            new(project, new() { ["42"] = first.Cargo, ["43"] = second.Cargo }, null),
            DateTimeOffset.UnixEpoch
        );
        Assert.Equal(90, preview.ReadyOnCarriers);
        var nexus = new ColonizationNexus
        {
            FleetCarriers = carriers,
            Systems =
            [
                new()
                {
                    Needs = new() { ["liquidoxygen"] = 100 },
                    FleetCarrierIds = [42, 43],
                },
            ],
        };
        Assert.Equal(90, ColonizationNexusPlan.Create(nexus).Cargo.Ready);
    }

    /// <summary>Distinguishes duplicate stock observations from separate additive journal transactions.</summary>
    [Fact]
    public void SynchronizationComparesAliasesAsOneStockButAddsSeparateTransactions()
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Cargo = new() { ["liquid oxygen"] = 80, ["liquidoxygen"] = 60 },
        };
        Assert.False(
            ColonizationFleetCarrierCapiCargoSeeder.ManifestsDiffer(
                carrier.Cargo,
                new Dictionary<string, int> { ["liquidoxygen"] = 80 }
            )
        );
        var pending = new Dictionary<string, int>();
        ColonizationFleetCarrierPendingCargo.MergeDelta(
            pending,
            new Dictionary<string, int> { ["liquid oxygen"] = 5, ["liquidoxygen"] = -2 }
        );
        Assert.Equal(3, Assert.Single(pending).Value);
        Assert.True(pending.ContainsKey("liquidoxygen"));
    }
}
