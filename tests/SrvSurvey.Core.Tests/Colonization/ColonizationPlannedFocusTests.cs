using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationPlannedFocusTests
{
    [Theory]
    [InlineData(true, 0, 85)]
    [InlineData(true, 99, 60)]
    [InlineData(true, 100, 25)]
    [InlineData(true, 101, 0)]
    [InlineData(false, 100, 60)]
    public void PlannedNeedsUseThePlanningCarrierWhileLiveBuildsKeepTheirLinkedCarriers(
        bool isPlanned,
        long selectedCarrier,
        int expectedCargo
    )
    {
        var project = new ColonizationProject
        {
            BuildId = "focus",
            BuildName = "Focused project",
            IsPlanned = isPlanned,
            Commodities = new() { ["steel"] = 100 },
            LinkedFleetCarriers = [new() { MarketId = 99 }],
        };
        ColonizationCommodityPlan plan = ColonizationCommodityPlanner.Create(
            new()
            {
                Projects = [project],
                PrimaryBuildId = project.BuildId,
                PlanningCarrierMarketId = selectedCarrier,
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
        ColonizationCommodityPlanRow row = Assert.Single(plan.Rows);
        Assert.Equal(100, row.Needed);
        Assert.Equal(expectedCargo, row.OnFleetCarriers);
    }
}
