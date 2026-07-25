using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationCommodityOverlayViewModelTests
{
    [Fact]
    public void AutoShowsForSupportedEliteContextsAndHidesForGalaxyMap()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        var plan = Plan();

        viewModel.Apply(
            plan,
            Status(GuiFocus.StationServices),
            updatedHasMarketSinceDocking: true);
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
    public void StationServicesRequiresMarketOrConstructionSite()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();

        viewModel.Apply(Plan(), Status(GuiFocus.StationServices));

        Assert.False(viewModel.ShouldAutoShow);

        viewModel.Apply(
            Plan(),
            Status(GuiFocus.StationServices),
            updatedHasMarketSinceDocking: true);

        Assert.True(viewModel.ShouldAutoShow);
    }

    [Fact]
    public void SquadronBankMusicShowsLinkedProjectCargo()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();

        viewModel.Apply(
            Plan(),
            Status(GuiFocus.NoFocus),
            updatedIsSquadronBankOpen: true);

        Assert.True(viewModel.ShouldAutoShow);
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

    [Fact]
    public void PreferencesControlAutoShowDeltaAndInlineColumns()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.Apply(Plan(), Status(GuiFocus.InternalPanel));
        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                AutoShow = false,
                ShowFleetCarrierDelta = true,
            });

        Assert.False(viewModel.ShouldAutoShow);
        var deltaRow = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.Equal("-80", deltaRow.OnFleetCarriersText);
        Assert.Equal("FC Δ", viewModel.FleetCarrierColumnHeader);

        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                InlineFleetCarrierCargo = true,
            });

        var inlineRow = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.Equal("20", inlineRow.OnFleetCarriersText);
        Assert.Empty(inlineRow.InShipText);
        Assert.Equal("HAVE", viewModel.FleetCarrierColumnHeader);
        Assert.Empty(viewModel.ShipColumnHeader);
    }

    [Fact]
    public void MarketGuidanceDimsUnavailableRowsAndHighlightsCarrierLoads()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        var plan = Plan() with
        {
            Rows =
            [
                new ColonizationCommodityPlanRow(
                    "steel",
                    "Steel",
                    "Metals",
                    100,
                    20,
                    80,
                    false,
                    false,
                    IsAvailableAtCurrentMarket: true,
                    IsUnavailableAtCurrentMarket: false,
                    CanCompleteFleetCarrierLoad: true),
                new ColonizationCommodityPlanRow(
                    "water",
                    "Water",
                    "Chemicals",
                    50,
                    0,
                    0,
                    false,
                    false,
                    IsAvailableAtCurrentMarket: false,
                    IsUnavailableAtCurrentMarket: true,
                    CanCompleteFleetCarrierLoad: false),
            ],
        };
        viewModel.Apply(plan, Status(GuiFocus.StationServices));
        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                HighlightAlmostCoveredFleetCarrierLoads = true,
            });

        var rows = viewModel.Groups.SelectMany(group => group.Rows).ToArray();
        var steel = rows.Single(row => row.Commodity == "steel");
        var water = rows.Single(row => row.Commodity == "water");
        Assert.True(steel.IsFleetCarrierLoadHighlighted);
        Assert.Equal("FC READY", steel.MarketBadgeText);
        Assert.Equal("-20", steel.OnFleetCarriersText);
        Assert.Equal(1, steel.RowOpacity);
        Assert.False(water.HasMarketBadge);
        Assert.Equal(0.48, water.RowOpacity);
    }

    [Fact]
    public void PendingCarrierSyncReplacesCountsWithProgressMarkers()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.Apply(Plan(), Status(GuiFocus.StationServices));

        viewModel.ApplyPendingFleetCarrierCargo(["steel"]);

        var pending = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.True(viewModel.HasPendingCargo);
        Assert.True(pending.IsPending);
        Assert.Equal("...", pending.NeededText);
        Assert.Equal("...", pending.OnFleetCarriersText);

        viewModel.ApplyPendingFleetCarrierCargo(null);

        var complete = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.False(viewModel.HasPendingCargo);
        Assert.False(complete.IsPending);
        Assert.Equal("100", complete.NeededText);
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
