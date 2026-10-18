using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationReviewRegressionTests
{
    private static readonly ColonizationBuildCatalog Catalog = ColonizationBuildCatalog.LoadEmbedded();

    [Fact]
    public void KiloReviewStatuslessLegacyStationsAreNotPlans()
    {
        ColonizationSystemRecord system = System(
            new ColonizationSystemSite
            {
                Id = "legacy",
                Name = "Existing Port",
                BuildType = "asteroid",
                MarketId = 42,
            }
        );
        Assert.False(system.Sites[0].HasExplicitStatus);
        Assert.Empty(ColonizationPlannedProjects.Create(system, Catalog, [], "Cmdr", []));
    }

    [Fact]
    public void KiloReviewIdlessPlansHaveDistinctStableIdentities()
    {
        var first = new ColonizationSystemSite
        {
            Name = "First Port",
            BuildType = "asteroid",
            Status = ColonizationSystemSiteStatus.Plan,
        };
        ColonizationSystemSite second = first with { Name = "Second Port" };
        IReadOnlyList<ColonizationProject> projects = ColonizationPlannedProjects.Create(
            System(first, second),
            Catalog,
            [],
            "Cmdr",
            []
        );
        Assert.Equal(2, projects.Select(project => project.BuildId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        IReadOnlyList<ColonizationProject> reversed = ColonizationPlannedProjects.Create(
            System(second, first),
            Catalog,
            [],
            "Cmdr",
            []
        );
        Assert.Equal(projects[0].BuildId, reversed[1].BuildId);
        Assert.Equal(projects[1].BuildId, reversed[0].BuildId);
        Assert.Equal(
            projects.Sum(project => project.RemainingRequired),
            ColonizationShopping.Aggregate(projects).Sum(need => need.Required)
        );
    }

    [Theory]
    [InlineData(0, 85)]
    [InlineData(99, 60)]
    [InlineData(100, 85)]
    [InlineData(101, 60)]
    public void KiloReviewMixedPlansRetainLiveCarrierLinks(long planningCarrier, int expectedCargo)
    {
        var live = new ColonizationProject
        {
            BuildId = "live",
            Commodities = new() { ["steel"] = 100 },
            LinkedFleetCarriers = [new() { MarketId = 99 }],
        };
        var plan = new ColonizationProject
        {
            BuildId = "plan",
            IsPlanned = true,
            Commodities = new() { ["steel"] = 100 },
        };
        ColonizationCommodityPlan result = ColonizationCommodityPlanner.Create(
            new()
            {
                Projects = [live, plan],
                PlanningCarrierMarketId = planningCarrier,
                FleetCarriers =
                [
                    new()
                    {
                        MarketId = 99,
                        Cargo = new() { ["steel"] = 60 },
                    },
                    new()
                    {
                        MarketId = 100,
                        Cargo = new() { ["steel"] = 25 },
                    },
                ],
                Construction = new(null, null, null, null, null, 0),
            }
        );
        Assert.Equal(expectedCargo, Assert.Single(result.Rows).OnFleetCarriers);
    }

    private static ColonizationSystemRecord System(params ColonizationSystemSite[] sites) =>
        new()
        {
            Name = "Sol",
            SystemAddress = 42,
            Architect = "Cmdr",
            Sites = sites.ToList(),
        };
}
