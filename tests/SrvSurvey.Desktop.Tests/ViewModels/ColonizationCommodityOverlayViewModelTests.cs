using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationCommodityOverlayViewModelTests
{
    [Fact]
    public void AutoShowsForSupportedEliteContextsAndHidesForGalaxyMap()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        var plan = Plan();

        viewModel.Apply(plan, Status(GuiFocus.StationServices));
        Assert.True(viewModel.ShouldAutoShow);

        viewModel.Apply(plan, Status(GuiFocus.InternalPanel));
        Assert.True(viewModel.ShouldAutoShow);

        viewModel.Apply(plan, Status(GuiFocus.GalaxyMap));
        Assert.False(viewModel.ShouldAutoShow);

        viewModel.Apply(
            plan,
            Status(GuiFocus.StationServices) with
            {
                Flags = StatusFlags.Docked | StatusFlags.FsdJump,
            });
        Assert.False(viewModel.ShouldAutoShow);
    }

    [Fact]
    public void DockedConstructionSiteShowsAtNormalCockpitFocus()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        var plan = Plan() with { IsAtConstructionSite = true };

        viewModel.Apply(plan, Status(GuiFocus.NoFocus));

        Assert.True(viewModel.ShouldAutoShow);
    }

    [Fact]
    public void CollapsesFleetCarrierCoveredGroupsAndShortcutExpandsThem()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        var covered = Plan() with
        {
            Rows =
            [
                new ColonizationCommodityPlanRow(
                    "steel",
                    "Steel",
                    "Metals",
                    100,
                    0,
                    100,
                    false,
                    false),
            ],
            FleetCarriers =
            [
                new ColonizationFleetCarrier
                {
                    MarketId = 1,
                    Name = "ABC-123",
                },
            ],
        };
        viewModel.Apply(covered, Status(GuiFocus.InternalPanel));

        var collapsed = Assert.Single(viewModel.Groups);
        Assert.True(collapsed.IsCollapsed);
        Assert.Empty(collapsed.Rows);

        viewModel.ToggleSatisfiedGroups();

        var expanded = Assert.Single(viewModel.Groups);
        Assert.False(expanded.IsCollapsed);
        Assert.Single(expanded.Rows);
    }

    [Fact]
    public void PresentsAssignmentTotalsAndFleetCarrierDeficit()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.Apply(Plan(), Status(GuiFocus.InternalPanel));

        var row = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.Equal("PIN", row.AssignmentText);
        Assert.Equal("100", row.NeededText);
        Assert.Equal("20", row.InShipText);
        Assert.Contains("100 remaining", viewModel.RemainingSummary);
        Assert.Contains("80 deficit", viewModel.FleetCarrierSummary);
    }

    private static ColonizationCommodityPlan Plan()
    {
        return new ColonizationCommodityPlan(
            "Test build (no_truss)",
            ["Test build (no_truss)"],
            [
                new ColonizationCommodityPlanRow(
                    "steel",
                    "Steel",
                    "Metals",
                    100,
                    20,
                    20,
                    true,
                    false),
            ],
            [
                new ColonizationFleetCarrier
                {
                    MarketId = 1,
                    Name = "ABC-123",
                    DisplayName = "Supply carrier",
                },
            ],
            100,
            2,
            80,
            2,
            false,
            false,
            false,
            false);
    }

    private static EliteStatus Status(GuiFocus focus)
    {
        return new EliteStatus
        {
            GuiFocus = focus,
            Flags = StatusFlags.Docked,
        };
    }
}
