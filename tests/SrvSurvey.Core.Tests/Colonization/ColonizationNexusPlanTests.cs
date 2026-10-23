using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationNexusPlanTests
{
    [Fact]
    public void UnknownCarrierCargoPreservesRequirementsWithoutInventingReadiness()
    {
        var nexus = new ColonizationNexus
        {
            FleetCarriers =
            [
                new() { MarketId = 42, Cargo = null! },
                new()
                {
                    MarketId = 43,
                    Cargo = new() { ["steel"] = 20 },
                },
            ],
            Systems =
            [
                new()
                {
                    Name = "Alpha",
                    Total = 120,
                    Progress = 20,
                    Needs = new() { ["steel"] = 100 },
                },
            ],
        };
        var plan = ColonizationNexusPlan.Create(nexus);
        Assert.Equal(100, plan.Cargo.Remaining);
        Assert.Null(plan.Cargo.Ready);
        Assert.Null(plan.Cargo.CoveredPercent);
        ColonizationNexusSystemPlan system = Assert.Single(plan.Systems);
        Assert.Equal(100, Assert.Single(system.Needs).Required);
        Assert.Null(system.Cargo.Ready);
        Assert.Null(Assert.Single(system.Needs).Missing);
        Assert.Null(Assert.Single(plan.Needs).Ready);
        var report = ColonizationProjectPreview.CreateNexusReport(nexus, "Alpha", DateTimeOffset.UnixEpoch);
        Assert.Equal(2, report.Carriers.Count);
        Assert.Null(report.Carriers[0].Cargo);
        Assert.Null(report.CarrierDeficit);
        Assert.Null(report.ReadyOnCarriers);
        Assert.Equal(100, report.Remaining);
    }

    [Fact]
    public void EverySystemUsesAllNexusCarriersRegardlessOfAssignments()
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Cargo = new() { ["steel"] = 120, ["water"] = 20 },
        };
        var spare = new ColonizationFleetCarrier
        {
            MarketId = 43,
            Cargo = new() { ["steel"] = 1000 },
        };
        var source = new ColonizationNexus
        {
            Hubs = [1],
            FleetCarriers = [carrier, carrier, spare],
            Systems =
            [
                new()
                {
                    SystemAddress = 1,
                    Total = 200,
                    Progress = 100,
                    Needs = new() { ["steel"] = 100 },
                    FleetCarrierIds = [42, 42, 999],
                },
                new()
                {
                    SystemAddress = 2,
                    Total = 100,
                    Needs = new() { ["steel"] = 100 },
                    FleetCarrierIds = [42],
                },
                new()
                {
                    SystemAddress = 3,
                    Total = 20,
                    Needs = new() { ["water"] = 20 },
                    BuildTypes = new() { ["outpost"] = 2 },
                },
            ],
        };
        var plan = ColonizationNexusPlan.Create(source);
        Assert.Equal([100L, 100L, 20L], plan.Systems.Select(system => system.Cargo.Ready));
        Assert.Equal([0L, 0L, 0L], plan.Systems.Select(system => Assert.Single(system.Needs).Missing));
        Assert.Equal(320, plan.Cargo.Total);
        Assert.Equal(100, plan.Cargo.Delivered);
        Assert.Equal(220, plan.Cargo.Remaining);
        Assert.Equal(220, plan.Cargo.Ready);
        Assert.Equal(31.25, plan.Cargo.DeliveredPercent);
        Assert.Equal(100, plan.Cargo.CoveredPercent);
        Assert.Equal(0, plan.CompletedSystems);
        Assert.False(plan.HasIncompleteRequirements);
        Assert.True(plan.Systems[0].IsHub);
        Assert.False(plan.Systems[1].IsHub);
        Assert.False(plan.Systems[2].IsHub);
        Assert.Equal(120, carrier.Cargo["steel"]);
        Assert.All(plan.Needs, need => Assert.Equal(0, need.Missing));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SystemEstimatesShareThePoolButCombinedNeedsCountStockOnce(bool reverse)
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Cargo = new() { ["steel"] = 120 },
        };
        ColonizationNexusSystem[] systems =
        [
            new()
            {
                SystemAddress = 1,
                Needs = new() { ["steel"] = 100 },
            },
            new()
            {
                SystemAddress = 2,
                Needs = new() { ["steel"] = 80 },
            },
        ];
        var plan = ColonizationNexusPlan.Create(
            new() { FleetCarriers = [carrier, carrier], Systems = reverse ? systems.Reverse().ToArray() : systems }
        );
        Assert.Equal(
            [100L, 80L],
            plan.Systems.OrderBy(system => system.System.SystemAddress).Select(system => system.Cargo.Ready)
        );
        Assert.All(plan.Systems, system => Assert.Equal(0, Assert.Single(system.Needs).Missing));
        Assert.Equal(new ColonizationNexusNeed("steel", 180, 120), Assert.Single(plan.Needs));
        Assert.Equal(120, plan.Cargo.Ready);
        Assert.Equal(120, carrier.Cargo["steel"]);
    }

    [Fact]
    public void DoesNotReplaceNexusRequirementsWithItsActiveBuild()
    {
        var source = new ColonizationNexus
        {
            Systems =
            [
                new()
                {
                    Total = 1000,
                    Progress = 100,
                    Needs = new() { ["steel"] = 900 },
                    Builds = [new() { MaximumRequired = 200, RemainingRequired = 100 }],
                },
            ],
        };
        var plan = ColonizationNexusPlan.Create(source);
        Assert.Equal(10, plan.Cargo.DeliveredPercent);
        Assert.Equal(900, Assert.Single(plan.Needs).Required);
        Assert.Equal(0, plan.Cargo.Ready);
    }

    [Fact]
    public void DistinguishesCompletedPlannedAndUnreportedRequirements()
    {
        var source = new ColonizationNexus
        {
            Systems =
            [
                new() { Total = 100, Progress = 100 },
                new()
                {
                    Progress = 10,
                    Needs = new() { ["steel"] = 90 },
                },
                new() { Total = 80, Progress = 20 },
                new(),
            ],
        };
        var plan = ColonizationNexusPlan.Create(source);
        Assert.Equal(1, plan.CompletedSystems);
        Assert.True(plan.Systems[0].Cargo.IsComplete);
        Assert.Equal(100, plan.Systems[1].Cargo.Total);
        Assert.Equal(60, plan.Systems[2].Cargo.Remaining);
        Assert.False(plan.Systems[3].Cargo.IsKnown);
        Assert.Equal(0, plan.Systems[3].Cargo.DeliveredPercent);
        Assert.Equal(0, plan.Systems[3].Cargo.CoveredPercent);
        Assert.True(plan.HasIncompleteRequirements);
        Assert.False(plan.Cargo.IsKnown);
    }

    [Fact]
    public void NormalizesAliasesAndClampsInvalidCargoCounts()
    {
        var source = new ColonizationNexus
        {
            FleetCarriers =
            [
                new()
                {
                    MarketId = 1,
                    Cargo = new()
                    {
                        ["$steel_name;"] = 30,
                        ["steel"] = 20,
                        ["water"] = -1,
                    },
                },
            ],
            Systems =
            [
                new()
                {
                    Total = 100,
                    Progress = -20,
                    Needs = new()
                    {
                        ["$steel_name;"] = 80,
                        ["steel"] = 100,
                        [""] = 5,
                        ["water"] = -1,
                    },
                    FleetCarrierIds = [1],
                },
            ],
        };
        var plan = ColonizationNexusPlan.Create(source);
        Assert.Equal(new ColonizationNexusNeed("steel", 100, 30), Assert.Single(plan.Needs));
        Assert.Equal(0, plan.Cargo.Delivered);
        Assert.Equal(70, Assert.Single(plan.Needs).Missing);
        var empty = ColonizationNexusPlan.Create(new());
        Assert.Empty(empty.Systems);
        Assert.False(empty.Cargo.IsKnown);
        Assert.Throws<ArgumentNullException>(() => ColonizationNexusPlan.Create(null!));
    }
}
