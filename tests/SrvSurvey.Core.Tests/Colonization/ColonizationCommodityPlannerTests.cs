using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationCommodityPlannerTests
{
    [Fact]
    public void PrimaryProjectOverridesHiddenAggregateSelection()
    {
        var primary = Project(
            "primary",
            20,
            200,
            new Dictionary<string, int> { ["steel"] = 40 });
        var other = Project(
            "other",
            30,
            300,
            new Dictionary<string, int> { ["water"] = 70 });

        var plan = ColonizationCommodityPlanner.Create(
            [other, primary],
            ["primary"],
            "primary",
            "Test Cmdr",
            [],
            shipCargo: null,
            EmptyConstruction());

        Assert.Equal("primary (no_truss)", plan.Title);
        var row = Assert.Single(plan.Rows);
        Assert.Equal("steel", row.Commodity);
        Assert.Equal(40, plan.TotalRemaining);
    }

    [Fact]
    public void LiveDepotOverridesTrackedProjectRequirements()
    {
        var tracked = Project(
            "tracked",
            42,
            99,
            new Dictionary<string, int> { ["steel"] = 900 });
        var construction = Construction(
            new ColonizationConstructionDepotSnapshot(
                DateTimeOffset.Parse("2026-07-24T12:00:00Z"),
                42,
                0.5,
                IsComplete: false,
                IsFailed: false,
                [
                    new ColonizationResourceRequirement(
                        "steel", "Localized Steel", 100, 75, 1),
                    new ColonizationResourceRequirement(
                        "water", "Localized Water", 50, 10, 1),
                ]));

        var plan = ColonizationCommodityPlanner.Create(
            [tracked],
            [],
            primaryBuildId: null,
            "Test Cmdr",
            [],
            shipCargo: null,
            construction);

        Assert.True(plan.IsAtConstructionSite);
        Assert.False(plan.IsLocalProjectUntracked);
        Assert.Equal("tracked (no_truss)", plan.Title);
        Assert.Equal(65, plan.TotalRemaining);
        Assert.Equal("Localized Steel", plan.Rows.Single(row =>
            row.Commodity == "steel").DisplayName);
    }

    [Fact]
    public void CombinesShipFleetCarrierAndAssignmentContext()
    {
        var project = Project(
            "build-1",
            42,
            99,
            new Dictionary<string, int>
            {
                ["steel"] = 100,
                ["water"] = 50,
            }) with
        {
            Commanders = new Dictionary<string, HashSet<string>>
            {
                ["Test Cmdr"] = new(
                    ["steel"],
                    StringComparer.OrdinalIgnoreCase),
                ["Other Cmdr"] = new(
                    ["water"],
                    StringComparer.OrdinalIgnoreCase),
            },
            LinkedFleetCarriers =
            [
                new ColonizationProjectFleetCarrier { MarketId = 10 },
            ],
        };
        var cargo = new CargoSnapshot(
            DateTimeOffset.Parse("2026-07-24T12:00:00Z"),
            "Cargo",
            "Ship",
            110,
            [
                new CargoItem("steel", "Steel", 110, 0),
            ]);
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 10,
            Name = "ABC-123",
            DisplayName = "Supply ship",
            Cargo = new Dictionary<string, int>
            {
                ["steel"] = 80,
                ["water"] = 20,
            },
        };
        var construction = EmptyConstruction() with
        {
            ShipCargoCapacity = 64,
        };

        var plan = ColonizationCommodityPlanner.Create(
            [project],
            [],
            primaryBuildId: null,
            "Test Cmdr",
            [carrier],
            cargo,
            construction);

        var steel = plan.Rows.Single(row => row.Commodity == "steel");
        var water = plan.Rows.Single(row => row.Commodity == "water");
        Assert.True(steel.IsAssignedToCommander);
        Assert.True(steel.ShipHasEnough);
        Assert.True(steel.HasSurplusInShip);
        Assert.True(water.IsAssignedToOther);
        Assert.Equal(20, water.OnFleetCarriers);
        Assert.Equal(150, plan.TotalRemaining);
        Assert.Equal(3, plan.TripsInCurrentShip);
        Assert.Equal(50, plan.FleetCarrierDeficit);
        Assert.Equal(1, plan.FleetCarrierDeficitTrips);
        Assert.Equal("Metals", steel.Category);
        Assert.Equal("Chemicals", water.Category);
    }

    [Fact]
    public void UntrackedConstructionSiteUsesDepotAndAllCommanderCarriers()
    {
        var construction = Construction(
            new ColonizationConstructionDepotSnapshot(
                DateTimeOffset.UtcNow,
                42,
                0.9,
                IsComplete: false,
                IsFailed: false,
                [
                    new ColonizationResourceRequirement(
                        "steel", "Steel", 100, 90, 1),
                ]));
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 500,
            Name = "ABC-123",
            Cargo = new Dictionary<string, int> { ["steel"] = 8 },
        };

        var plan = ColonizationCommodityPlanner.Create(
            [],
            [],
            primaryBuildId: null,
            "Test Cmdr",
            [carrier],
            shipCargo: null,
            construction);

        Assert.True(plan.IsLocalProjectUntracked);
        Assert.Equal("Hope", plan.Title);
        Assert.Equal(8, Assert.Single(plan.Rows).OnFleetCarriers);
        Assert.Single(plan.FleetCarriers);
    }

    [Fact]
    public void TrackedConstructionSiteUsesProjectNeedsBeforeDepotIsOpened()
    {
        var project = Project(
            "tracked",
            42,
            99,
            new Dictionary<string, int> { ["steel"] = 25 });
        var construction = EmptyConstruction() with
        {
            CurrentDock = new ColonizationDockingSnapshot(
                42,
                99,
                "Test System",
                "Orbital Construction Site: Hope",
                "Test Faction",
                ["colonisationcontribution"]),
        };

        var plan = ColonizationCommodityPlanner.Create(
            [project],
            [],
            primaryBuildId: null,
            "Test Cmdr",
            [],
            shipCargo: null,
            construction);

        Assert.True(plan.IsAtConstructionSite);
        Assert.False(plan.IsLocalProjectUntracked);
        Assert.Equal(25, Assert.Single(plan.Rows).Needed);
    }

    [Fact]
    public void UntrackedFleetCarrierWarningUsesStationTypeAndCommanderInventory()
    {
        var construction = EmptyConstruction() with
        {
            CurrentDock = new ColonizationDockingSnapshot(
                900,
                99,
                "Test System",
                "Untracked Carrier",
                "Test Faction",
                ["squadronBank"],
                DateTimeOffset.Parse("2026-07-24T12:00:00Z"),
                "FleetCarrier"),
        };

        var untracked = ColonizationCommodityPlanner.Create(
            [],
            [],
            primaryBuildId: null,
            "Test Cmdr",
            [],
            shipCargo: null,
            construction);

        Assert.True(untracked.IsDockedAtUntrackedFleetCarrier);
        Assert.True(untracked.HasContent);

        var linked = ColonizationCommodityPlanner.Create(
            [],
            [],
            primaryBuildId: null,
            "Test Cmdr",
            [new ColonizationFleetCarrier { MarketId = 900 }],
            shipCargo: null,
            construction);

        Assert.False(linked.IsDockedAtUntrackedFleetCarrier);

        var ordinaryStation = ColonizationCommodityPlanner.Create(
            [],
            [],
            primaryBuildId: null,
            "Test Cmdr",
            [],
            shipCargo: null,
            construction with
            {
                CurrentDock = construction.CurrentDock! with
                {
                    StationType = "Coriolis",
                },
            });

        Assert.False(ordinaryStation.IsDockedAtUntrackedFleetCarrier);
    }

    [Fact]
    public void CompletedConstructionStillHasVisibleCompletionState()
    {
        var construction = Construction(
            new ColonizationConstructionDepotSnapshot(
                DateTimeOffset.UtcNow,
                42,
                1,
                IsComplete: true,
                IsFailed: false,
                []));

        var plan = ColonizationCommodityPlanner.Create(
            [],
            [],
            primaryBuildId: null,
            "Test Cmdr",
            [],
            shipCargo: null,
            construction);

        Assert.True(plan.IsConstructionComplete);
        Assert.True(plan.HasContent);
        Assert.Empty(plan.Rows);
    }

    [Fact]
    public void UsesOnlyCurrentPostDockMarketStockForCarrierLoadGuidance()
    {
        var dockedAt = DateTimeOffset.Parse("2026-07-24T12:00:00Z");
        var project = Project(
            "tracked",
            42,
            99,
            new Dictionary<string, int>
            {
                ["steel"] = 100,
                ["water"] = 50,
            }) with
        {
            LinkedFleetCarriers =
            [
                new ColonizationProjectFleetCarrier { MarketId = 500 },
            ],
        };
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 500,
            Cargo = new Dictionary<string, int> { ["steel"] = 80 },
        };
        var construction = EmptyConstruction() with
        {
            CurrentDock = new ColonizationDockingSnapshot(
                900,
                99,
                "Test System",
                "Supply Station",
                "Test Faction",
                ["commodities"],
                dockedAt),
            ShipCargoCapacity = 64,
        };
        var market = new MarketSnapshot(
            dockedAt.AddSeconds(1),
            "Market",
            900,
            "Supply Station",
            "Coriolis",
            string.Empty,
            "Test System",
            [
                MarketItem("$Steel_Name;", "Localized Steel", stock: 40),
                MarketItem("$Water_Name;", "Localized Water", stock: 0),
            ]);

        var plan = ColonizationCommodityPlanner.Create(
            [project],
            [],
            primaryBuildId: null,
            "Test Cmdr",
            [carrier],
            shipCargo: null,
            construction,
            market);

        var steel = plan.Rows.Single(row => row.Commodity == "steel");
        var water = plan.Rows.Single(row => row.Commodity == "water");
        Assert.True(steel.IsAvailableAtCurrentMarket);
        Assert.False(steel.IsUnavailableAtCurrentMarket);
        Assert.True(steel.CanCompleteFleetCarrierLoad);
        Assert.Equal("Localized Steel", steel.DisplayName);
        Assert.False(water.IsAvailableAtCurrentMarket);
        Assert.True(water.IsUnavailableAtCurrentMarket);
        Assert.False(water.CanCompleteFleetCarrierLoad);

        var stalePlan = ColonizationCommodityPlanner.Create(
            [project],
            [],
            primaryBuildId: null,
            "Test Cmdr",
            [carrier],
            shipCargo: null,
            construction,
            market with { Timestamp = dockedAt });
        Assert.All(stalePlan.Rows, row =>
        {
            Assert.False(row.IsAvailableAtCurrentMarket);
            Assert.False(row.IsUnavailableAtCurrentMarket);
            Assert.False(row.CanCompleteFleetCarrierLoad);
        });
    }

    private static MarketItem MarketItem(
        string name,
        string localizedName,
        int stock)
    {
        return new MarketItem(
            1,
            name,
            localizedName,
            "$MARKET_category_metals;",
            "Metals",
            1,
            1,
            1,
            1,
            0,
            stock,
            0,
            Producer: stock > 0,
            Consumer: false,
            Rare: false);
    }

    private static ColonizationProject Project(
        string id,
        long marketId,
        long systemAddress,
        Dictionary<string, int> commodities)
    {
        return new ColonizationProject
        {
            BuildId = id,
            BuildType = "no_truss",
            BuildName = id,
            MarketId = marketId,
            SystemAddress = systemAddress,
            SystemName = "Test System",
            Commodities = commodities,
        };
    }

    private static ColonizationConstructionSnapshot EmptyConstruction()
    {
        return new ColonizationConstructionSnapshot(
            null,
            null,
            null,
            null,
            null,
            0);
    }

    private static ColonizationConstructionSnapshot Construction(
        ColonizationConstructionDepotSnapshot depot)
    {
        return new ColonizationConstructionSnapshot(
            new ColonizationDockingSnapshot(
                42,
                99,
                "Test System",
                "Orbital Construction Site: Hope",
                "Test Faction",
                ["colonisationcontribution"]),
            depot,
            null,
            null,
            null,
            0);
    }
}
