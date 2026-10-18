using System.Globalization;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationCommodityOverlayViewModelTests
{
    /// <summary>Hides every status and assignment badge immediately while preserving cargo values, highlights, and completion.</summary>
    [Theory]
    [InlineData(80, 20, true, "LOAD")]
    [InlineData(100, 0, false, "READY")]
    [InlineData(40, 0, false, "MARKET")]
    public void HideRowBadgesPreservesCommodityState(int carrierCargo, int shipCargo, bool canLoad, string badge)
    {
        ColonizationCommodityPlan plan = Plan();
        var viewModel = new ColonizationCommodityOverlayViewModel();
        ColonizationOverlayPreferences preferences = ColonizationOverlayPreferences.Default with
        {
            CollapseCoveredGroups = false,
            HighlightAlmostCoveredFleetCarrierLoads = true,
        };
        viewModel.ApplyPreferences(preferences);
        viewModel.Apply(
            plan with
            {
                Rows =
                [
                    plan.Rows[0] with
                    {
                        OnFleetCarriers = carrierCargo,
                        InShip = shipCargo,
                        CanCompleteFleetCarrierLoad = canLoad,
                        IsAvailableAtCurrentMarket = true,
                        IsAssignedToCommander = true,
                    },
                ],
            },
            null
        );
        ColonizationCommodityOverlayRowViewModel shown = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.Equal(badge, shown.MarketBadgeText);
        Assert.True(shown.HasMarketBadge);
        Assert.True(shown.HasAssignment);

        viewModel.ApplyPreferences(preferences with { HideRowBadges = true });
        ColonizationCommodityOverlayRowViewModel hidden = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.False(hidden.HasMarketBadge);
        Assert.False(hidden.HasAssignment);
        Assert.Equal(shown.NeededText, hidden.NeededText);
        Assert.Equal(shown.OnFleetCarriersText, hidden.OnFleetCarriersText);
        Assert.Equal(shown.InShipText, hidden.InShipText);
        Assert.Equal(shown.IsSatisfied, hidden.IsSatisfied);
        Assert.Equal(shown.IsFleetCarrierLoadHighlighted, hidden.IsFleetCarrierLoadHighlighted);

        viewModel.ApplyPreferences(preferences);
        ColonizationCommodityOverlayRowViewModel restored = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.True(restored.HasMarketBadge);
        Assert.True(restored.HasAssignment);
        Assert.Equal(badge, restored.MarketBadgeText);
    }

    /// <summary>Separates system and project text at the first spaced hyphen while retaining other hyphens and raw names.</summary>
    [Theory]
    [InlineData("Bleia Eohn SX-F c12 - Refinery Hub (fc_loading)", "BLEIA EOHN SX-F C12 -\nREFINERY HUB (FC_LOADING)")]
    [InlineData("System - Refinery - West (build)", "SYSTEM -\nREFINERY - WEST (BUILD)")]
    [InlineData("SX-F c12 (build)", "SX-F C12 (BUILD)")]
    [InlineData("", "")]
    public void ProjectHeaderBreaksAfterFirstSpacedHyphen(string title, string expected)
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.Apply(Plan() with { Title = title }, null);
        Assert.Equal(expected, viewModel.HeaderTitle);
        Assert.Equal(title, viewModel.Title);
    }

    [Fact]
    public void AutoShowsForSupportedEliteContextsAndHidesForGalaxyMap()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        ColonizationCommodityPlan plan = Plan();

        viewModel.Apply(plan, Status(GuiFocus.StationServices), updatedHasMarketSinceDocking: true);
        Assert.True(viewModel.ShouldAutoShow);
        Assert.Equal("TEST BUILD (NO_TRUSS)", viewModel.HeaderTitle);
        Assert.Same(viewModel.ProjectNames, viewModel.ProjectNames);

        viewModel.Apply(plan, Status(GuiFocus.InternalPanel));
        Assert.True(viewModel.ShouldAutoShow);

        viewModel.Apply(plan, Status(GuiFocus.GalaxyMap));
        Assert.False(viewModel.ShouldAutoShow);
        Assert.True(viewModel.CanShowManually);

        viewModel.Apply(
            plan,
            Status(GuiFocus.StationServices) with
            {
                Flags = StatusFlags.Docked | StatusFlags.FsdJump,
            }
        );
        Assert.False(viewModel.ShouldAutoShow);
    }

    [Fact]
    public void DockedSelectedProjectsDoNotRequireMarketOrConstructionSite()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();

        viewModel.Apply(Plan(), Status(GuiFocus.StationServices));

        Assert.True(viewModel.ShouldAutoShow);

        viewModel.Apply(Plan(), Status(GuiFocus.StationServices), updatedHasMarketSinceDocking: true);

        Assert.True(viewModel.ShouldAutoShow);
    }

    /// <summary>Docking visibility requires selected project content and still respects excluded game contexts.</summary>
    [Theory]
    [InlineData(StatusFlags.Docked, GuiFocus.NoFocus, true, true)]
    [InlineData(StatusFlags.Docked, GuiFocus.StationServices, true, true)]
    [InlineData(StatusFlags.Docked, GuiFocus.NoFocus, false, false)]
    [InlineData(StatusFlags.Docked, GuiFocus.StationServices, false, false)]
    [InlineData(StatusFlags.Docked, GuiFocus.GalaxyMap, true, false)]
    [InlineData(StatusFlags.Docked, GuiFocus.ExternalPanel, true, false)]
    [InlineData(StatusFlags.InMainShip, GuiFocus.NoFocus, true, false)]
    [InlineData(StatusFlags.Docked | StatusFlags.FsdJump, GuiFocus.NoFocus, true, false)]
    [InlineData(StatusFlags.Docked | StatusFlags.FsdJump, GuiFocus.StationServices, true, false)]
    public void AutomaticDockingRespectsProjectSelectionAndGameContext(
        StatusFlags flags,
        GuiFocus focus,
        bool selected,
        bool expected
    )
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        ColonizationCommodityPlan plan = Plan();
        viewModel.Apply(
            plan with
            {
                ProjectNames = selected ? plan.ProjectNames : [],
            },
            new EliteStatus { Flags = flags, GuiFocus = focus }
        );

        Assert.Equal(expected, viewModel.ShouldAutoShow);
    }

    [Fact]
    public void SquadronBankMusicShowsLinkedProjectCargo()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();

        viewModel.Apply(Plan(), Status(GuiFocus.NoFocus), updatedIsSquadronBankOpen: true);

        Assert.True(viewModel.ShouldAutoShow);
    }

    [Fact]
    public void DockedConstructionSiteShowsAtNormalCockpitFocus()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        ColonizationCommodityPlan plan = Plan() with { IsAtConstructionSite = true };

        viewModel.Apply(plan, Status(GuiFocus.NoFocus));

        Assert.True(viewModel.ShouldAutoShow);
    }

    [Fact]
    public void CollapsesFleetCarrierCoveredGroupsAndShortcutExpandsThem()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        ColonizationCommodityPlan covered = Plan() with
        {
            Rows =
            [
                new ColonizationCommodityPlanRow
                {
                    Commodity = "steel",
                    DisplayName = "Steel",
                    Category = "Metals",
                    Needed = 100,
                    InShip = 0,
                    OnFleetCarriers = 100,
                    IsAssignedToCommander = false,
                    IsAssignedToOther = false,
                },
            ],
            FleetCarriers = [new ColonizationFleetCarrier { MarketId = 1, Name = "ABC-123" }],
        };
        viewModel.Apply(covered, Status(GuiFocus.InternalPanel));

        ColonizationCommodityGroupViewModel collapsed = Assert.Single(viewModel.Groups);
        Assert.True(collapsed.IsCollapsed);
        Assert.Empty(collapsed.Rows);

        viewModel.ToggleSatisfiedGroups();

        ColonizationCommodityGroupViewModel expanded = Assert.Single(viewModel.Groups);
        Assert.False(expanded.IsCollapsed);
        Assert.Single(expanded.Rows);
    }

    /// <summary>Hides only collapsed categories and retains the original plan and overlay totals.</summary>
    [Theory]
    [InlineData(false, false, 1, "Covered Fleet Carrier groups are expanded.")]
    [InlineData(false, true, 1, "Covered Fleet Carrier groups are expanded.")]
    [InlineData(true, false, 1, "Covered Fleet Carrier groups collapse automatically.")]
    [InlineData(true, true, 0, "Covered Fleet Carrier groups are hidden automatically.")]
    public void HidingCoveredGroupsRequiresCollapse(bool collapse, bool hide, int expectedGroups, string expectedMode)
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        ColonizationCommodityPlan covered = CoveredPlan();
        viewModel.Apply(covered, Status(GuiFocus.InternalPanel));
        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                CollapseCoveredGroups = collapse,
                HideCollapsedGroups = hide,
            }
        );

        Assert.Equal(expectedGroups, viewModel.Groups.Count);
        Assert.Same(covered, viewModel.Plan);
        Assert.True(viewModel.HasRows);
        Assert.True(viewModel.ShouldAutoShow);
        Assert.Contains("100 remaining", viewModel.RemainingSummary);
        if (expectedGroups > 0)
        {
            ColonizationCommodityGroupViewModel group = Assert.Single(viewModel.Groups);
            Assert.Equal("Metals", group.Name);
            Assert.Equal(collapse, group.IsCollapsed);
        }
        Assert.Equal(expectedMode, viewModel.CollapseModeText);
    }

    /// <summary>Leaves categories expanded whenever the existing carrier coverage rules forbid collapse.</summary>
    [Theory]
    [InlineData(true, true, true, 0, 100)]
    [InlineData(false, false, true, 0, 100)]
    [InlineData(false, true, false, 0, 100)]
    [InlineData(false, true, true, 1, 100)]
    [InlineData(false, true, true, 0, 99)]
    public void HideOptionRespectsCoverageRules(
        bool construction,
        bool showCargo,
        bool linked,
        int inShip,
        int onCarrier
    )
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        ColonizationCommodityPlan covered = CoveredPlan();
        viewModel.Apply(
            covered with
            {
                IsAtConstructionSite = construction,
                FleetCarriers = linked ? covered.FleetCarriers : [],
                Rows = [covered.Rows[0] with { InShip = inShip, OnFleetCarriers = onCarrier }],
            },
            Status(GuiFocus.InternalPanel)
        );
        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                HideCollapsedGroups = true,
                ShowFleetCarrierCargo = showCargo,
            }
        );

        ColonizationCommodityGroupViewModel group = Assert.Single(viewModel.Groups);
        Assert.False(group.IsCollapsed);
        Assert.Single(group.Rows);
    }

    /// <summary>Preserves partially covered categories and allows the shortcut to reveal hidden categories.</summary>
    [Fact]
    public void HiddenCategoriesReturnOnShortcutAndCoverageChanges()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        ColonizationCommodityPlan covered = CoveredPlan();
        ColonizationCommodityPlanRow water = covered.Rows[0] with
        {
            Commodity = "water",
            DisplayName = "Water",
            Category = "Chemicals",
            OnFleetCarriers = 0,
        };
        ColonizationCommodityPlan mixed = covered with { Rows = [covered.Rows[0], water] };
        viewModel.Apply(mixed, Status(GuiFocus.InternalPanel));
        viewModel.ApplyPreferences(ColonizationOverlayPreferences.Default with { HideCollapsedGroups = true });
        Assert.Equal("Chemicals", Assert.Single(viewModel.Groups).Name);

        viewModel.ToggleSatisfiedGroups();
        Assert.Equal(2, viewModel.Groups.Count);
        Assert.All(viewModel.Groups, group => Assert.False(group.IsCollapsed));
        viewModel.ToggleSatisfiedGroups();
        Assert.Equal("Chemicals", Assert.Single(viewModel.Groups).Name);

        viewModel.Apply(
            mixed with
            {
                Rows = [covered.Rows[0], water with { Category = "Metals" }],
            },
            Status(GuiFocus.InternalPanel)
        );
        ColonizationCommodityGroupViewModel partial = Assert.Single(viewModel.Groups);
        Assert.False(partial.IsCollapsed);
        Assert.Equal(2, partial.Rows.Count);

        viewModel.Apply(covered, Status(GuiFocus.InternalPanel));
        Assert.Empty(viewModel.Groups);
        viewModel.ApplyPreferences(ColonizationOverlayPreferences.Default);
        Assert.True(Assert.Single(viewModel.Groups).IsCollapsed);
    }

    /// <summary>Creates a category entirely covered by a linked carrier with no cargo aboard the ship.</summary>
    private static ColonizationCommodityPlan CoveredPlan()
    {
        ColonizationCommodityPlan plan = Plan();
        return plan with { Rows = [plan.Rows[0] with { InShip = 0, OnFleetCarriers = 100 }] };
    }

    [Fact]
    public void PresentsAssignmentTotalsAndFleetCarrierDeficit()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.Apply(Plan(), Status(GuiFocus.InternalPanel));

        ColonizationCommodityOverlayRowViewModel row = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.Equal("PIN", row.AssignmentText);
        Assert.Equal("100", row.NeededText);
        Assert.Equal("20", row.InShipText);
        Assert.Contains("100 remaining", viewModel.RemainingSummary);
        Assert.Contains("80 deficit", viewModel.FleetCarrierSummary);
    }

    /// <summary>Disabling automatic hiding permits persistent display while preserving cargo column preferences.</summary>
    [Fact]
    public void PreferencesControlAutomaticHidingDeltaAndInlineColumns()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.Apply(Plan(), Status(GuiFocus.InternalPanel));
        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                AutoShow = false,
                ShowFleetCarrierDelta = true,
            }
        );

        Assert.True(viewModel.ShouldAutoShow);
        Assert.True(viewModel.CanShowManually);
        ColonizationCommodityOverlayRowViewModel deltaRow = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.Equal("-80", deltaRow.OnFleetCarriersText);
        Assert.Equal("FC Δ", viewModel.FleetCarrierColumnHeader);

        viewModel.ApplyPreferences(ColonizationOverlayPreferences.Default with { InlineFleetCarrierCargo = true });

        ColonizationCommodityOverlayRowViewModel inlineRow = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.Equal("40", inlineRow.OnFleetCarriersText);
        Assert.Empty(inlineRow.InShipText);
        Assert.Equal("HAVE", viewModel.FleetCarrierColumnHeader);
        Assert.Empty(viewModel.ShipColumnHeader);
    }

    /// <summary>Uses combined ship and carrier quantities for sums, deltas, and coverage, including empty cargo.</summary>
    [Theory]
    [InlineData(false, 80, 20, "100", true)]
    [InlineData(true, 80, 20, "0", true)]
    [InlineData(true, 80, 19, "-1", false)]
    [InlineData(true, 80, 21, "+1", true)]
    [InlineData(false, 0, 20, "20", false)]
    [InlineData(true, 0, 20, "-80", false)]
    [InlineData(false, 80, 0, "80", false)]
    [InlineData(true, 80, 0, "-20", false)]
    [InlineData(false, 0, 0, "0", false)]
    [InlineData(true, 0, 0, "-100", false)]
    [InlineData(true, 0, 100, "0", true)]
    [InlineData(false, 100, 0, "100", true)]
    public void CombinedCargoDisplaysSumOrDeltaAndMarksCoverage(
        bool delta,
        int carrier,
        int ship,
        string expected,
        bool covered
    )
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        ColonizationCommodityPlan plan = Plan();
        viewModel.Apply(
            plan with
            {
                Rows = [plan.Rows[0] with { OnFleetCarriers = carrier, InShip = ship }],
            },
            Status(GuiFocus.InternalPanel)
        );
        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                CollapseCoveredGroups = false,
                InlineFleetCarrierCargo = true,
                ShowFleetCarrierDelta = delta,
            }
        );

        ColonizationCommodityOverlayRowViewModel row = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.Equal(expected, row.OnFleetCarriersText);
        Assert.Equal(covered, row.IsSatisfied);
        Assert.Equal(delta && covered, row.IsActiveSurplus);
        Assert.Equal(delta && !covered, row.IsActiveDeficit);
        Assert.Empty(row.InShipText);
        Assert.Empty(viewModel.ShipColumnHeader);
        Assert.Equal(delta ? "HAVE Δ" : "HAVE", viewModel.FleetCarrierColumnHeader);
    }

    /// <summary>Keeps the combined value steady after a ship-to-carrier transfer and masks pending synchronization.</summary>
    [Theory]
    [InlineData(false, "100")]
    [InlineData(true, "0")]
    public void CombinedCargoRemainsCoveredAfterShipTransfer(bool delta, string expected)
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        ColonizationCommodityPlan plan = Plan();
        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                CollapseCoveredGroups = false,
                InlineFleetCarrierCargo = true,
                ShowFleetCarrierDelta = delta,
            }
        );
        viewModel.Apply(
            plan with
            {
                Rows = [plan.Rows[0] with { OnFleetCarriers = 80, InShip = 20 }],
            },
            Status(GuiFocus.InternalPanel)
        );
        Assert.Equal(expected, Assert.Single(Assert.Single(viewModel.Groups).Rows).OnFleetCarriersText);

        viewModel.ApplyPendingFleetCarrierCargo(["steel"]);
        viewModel.Apply(
            plan with
            {
                Rows = [plan.Rows[0] with { OnFleetCarriers = 100, InShip = 0 }],
            },
            Status(GuiFocus.InternalPanel)
        );
        Assert.Equal("...", Assert.Single(Assert.Single(viewModel.Groups).Rows).OnFleetCarriersText);
        viewModel.ApplyPendingFleetCarrierCargo(null);
        ColonizationCommodityOverlayRowViewModel row = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.Equal(expected, row.OnFleetCarriersText);
        Assert.True(row.IsSatisfied);
    }

    /// <summary>Retains separate ship counts when carrier display is disabled without clearing the saved combine option.</summary>
    [Fact]
    public void DisabledCarrierCountsRestoreSeparateShipDisplay()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        viewModel.Apply(Plan(), Status(GuiFocus.InternalPanel));
        ColonizationOverlayPreferences preferences = ColonizationOverlayPreferences.Default with
        {
            InlineFleetCarrierCargo = true,
            ShowFleetCarrierCargo = false,
        };
        viewModel.ApplyPreferences(preferences);
        ColonizationCommodityOverlayRowViewModel separate = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.Empty(separate.OnFleetCarriersText);
        Assert.Equal("20", separate.InShipText);
        Assert.Equal("SHIP", viewModel.ShipColumnHeader);
        Assert.Empty(viewModel.FleetCarrierColumnHeader);

        viewModel.ApplyPreferences(preferences with { ShowFleetCarrierCargo = true });
        ColonizationCommodityOverlayRowViewModel combined = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.Equal("40", combined.OnFleetCarriersText);
        Assert.Empty(combined.InShipText);
        Assert.Equal("HAVE", viewModel.FleetCarrierColumnHeader);
    }

    /// <summary>Preserves carrier-loading guidance while honoring the selected sum or delta for combined cargo.</summary>
    [Theory]
    [InlineData(false, "100")]
    [InlineData(true, "0")]
    public void CombinedLoadingGuidanceHonorsSumOrDelta(bool delta, string expected)
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        ColonizationCommodityPlan plan = Plan();
        viewModel.Apply(
            plan with
            {
                Rows =
                [
                    plan.Rows[0] with
                    {
                        OnFleetCarriers = 80,
                        InShip = 20,
                        CanCompleteFleetCarrierLoad = true,
                        IsAvailableAtCurrentMarket = true,
                    },
                ],
            },
            Status(GuiFocus.StationServices)
        );
        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                InlineFleetCarrierCargo = true,
                ShowFleetCarrierDelta = delta,
                HighlightAlmostCoveredFleetCarrierLoads = true,
            }
        );
        ColonizationCommodityOverlayRowViewModel row = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.True(row.IsFleetCarrierLoadHighlighted);
        Assert.Equal("LOAD", row.MarketBadgeText);
        Assert.Equal(expected, row.OnFleetCarriersText);
        Assert.True(row.IsSatisfied);
    }

    /// <summary>Keeps cargo awaiting transfer labeled LOAD, then marks the carrier ready only after it holds the full requirement.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FleetCarrierReadyWaitsForShipCargoToBeTransferred(bool combined, bool delta)
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        ColonizationCommodityPlan plan = Plan();
        ColonizationCommodityPlanRow cargo = plan.Rows[0] with
        {
            OnFleetCarriers = 80,
            InShip = 20,
            CanCompleteFleetCarrierLoad = true,
            IsAvailableAtCurrentMarket = true,
        };
        ColonizationOverlayPreferences preferences = ColonizationOverlayPreferences.Default with
        {
            CollapseCoveredGroups = false,
            InlineFleetCarrierCargo = combined,
            ShowFleetCarrierDelta = delta,
            HighlightAlmostCoveredFleetCarrierLoads = true,
        };
        viewModel.ApplyPreferences(preferences);
        viewModel.Apply(plan with { Rows = [cargo] }, Status(GuiFocus.StationServices));
        ColonizationCommodityOverlayRowViewModel row = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.Equal("LOAD", row.MarketBadgeText);
        Assert.True(row.HasMarketBadge);
        Assert.True(row.IsFleetCarrierLoadHighlighted);
        Assert.Equal(combined, row.IsSatisfied);

        cargo = cargo with { OnFleetCarriers = 100, InShip = 0, CanCompleteFleetCarrierLoad = false };
        viewModel.Apply(plan with { Rows = [cargo] }, Status(GuiFocus.StationServices));
        row = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.Equal("READY", row.MarketBadgeText);
        Assert.True(row.IsSatisfied);
        Assert.False(row.IsFleetCarrierLoadHighlighted);
        Assert.Equal(delta ? "0" : "100", row.OnFleetCarriersText);

        viewModel.ApplyPreferences(preferences with { HighlightAlmostCoveredFleetCarrierLoads = false });
        Assert.Equal("MARKET", Assert.Single(Assert.Single(viewModel.Groups).Rows).MarketBadgeText);
    }

    /// <summary>Prevents integer overflow when two valid cargo counts exceed an individual count's range.</summary>
    [Theory]
    [InlineData(false, 4294967294L)]
    [InlineData(true, 2147483647L)]
    public void CombinedCargoTotalsDoNotOverflow(bool delta, long expected)
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        ColonizationCommodityPlan plan = Plan();
        viewModel.Apply(
            plan with
            {
                Rows =
                [
                    plan.Rows[0] with
                    {
                        Needed = int.MaxValue,
                        OnFleetCarriers = int.MaxValue,
                        InShip = int.MaxValue,
                    },
                ],
            },
            Status(GuiFocus.InternalPanel)
        );
        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                InlineFleetCarrierCargo = true,
                ShowFleetCarrierDelta = delta,
            }
        );
        ColonizationCommodityOverlayRowViewModel row = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        string prefix = delta ? "+" : string.Empty;
        Assert.Equal(prefix + expected.ToString("N0", CultureInfo.CurrentCulture), row.OnFleetCarriersText);
        Assert.True(row.IsSatisfied);
    }

    [Fact]
    public void CompactScrollingListIsOptInAndUnconstrainedByDefault()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();

        Assert.False(viewModel.UseCompactScrollingCommoditiesList);
        Assert.Equal(double.PositiveInfinity, viewModel.CommodityListMaxHeight);

        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                UseCompactScrollingCommoditiesList = true,
            }
        );

        Assert.True(viewModel.UseCompactScrollingCommoditiesList);
        Assert.Equal(
            ColonizationCommodityOverlayViewModel.CompactCommodityListMaxHeight,
            viewModel.CommodityListMaxHeight
        );
    }

    /// <summary>Checks unavailable-market dimming and readiness badges while cargo display options change.</summary>
    [Fact]
    public void MarketGuidanceDimsUnavailableRowsAndHighlightsCarrierLoads()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        ColonizationCommodityPlan plan = Plan() with
        {
            Rows =
            [
                new ColonizationCommodityPlanRow
                {
                    Commodity = "steel",
                    DisplayName = "Steel",
                    Category = "Metals",
                    Needed = 100,
                    InShip = 20,
                    OnFleetCarriers = 80,
                    IsAssignedToCommander = false,
                    IsAssignedToOther = false,
                    IsAvailableAtCurrentMarket = true,
                    IsUnavailableAtCurrentMarket = false,
                    CanCompleteFleetCarrierLoad = true,
                },
                new ColonizationCommodityPlanRow
                {
                    Commodity = "water",
                    DisplayName = "Water",
                    Category = "Metals",
                    Needed = 50,
                    InShip = 0,
                    OnFleetCarriers = 0,
                    IsAssignedToCommander = false,
                    IsAssignedToOther = false,
                    IsAvailableAtCurrentMarket = false,
                    IsUnavailableAtCurrentMarket = true,
                    CanCompleteFleetCarrierLoad = false,
                },
            ],
        };
        viewModel.Apply(plan, Status(GuiFocus.StationServices));
        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                HighlightAlmostCoveredFleetCarrierLoads = true,
            }
        );

        ColonizationCommodityOverlayRowViewModel[] rows = viewModel.Groups.SelectMany(group => group.Rows).ToArray();
        ColonizationCommodityOverlayRowViewModel steel = rows.Single(row => row.Commodity == "steel");
        ColonizationCommodityOverlayRowViewModel water = rows.Single(row => row.Commodity == "water");
        Assert.True(steel.IsAlternateRow);
        Assert.False(water.IsAlternateRow);
        Assert.True(steel.IsFleetCarrierLoadHighlighted);
        Assert.Equal("LOAD", steel.MarketBadgeText);
        Assert.Equal("80", steel.OnFleetCarriersText);
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

        ColonizationCommodityOverlayRowViewModel pending = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.True(viewModel.HasPendingCargo);
        Assert.True(pending.IsPending);
        Assert.Equal("...", pending.NeededText);
        Assert.Equal("...", pending.OnFleetCarriersText);

        viewModel.ApplyPendingFleetCarrierCargo(null);

        ColonizationCommodityOverlayRowViewModel complete = Assert.Single(Assert.Single(viewModel.Groups).Rows);
        Assert.False(viewModel.HasPendingCargo);
        Assert.False(complete.IsPending);
        Assert.Equal("100", complete.NeededText);
    }

    [Fact]
    public void PresentsUntrackedFleetCarrierWarning()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();

        viewModel.Apply(
            Plan() with
            {
                IsDockedAtUntrackedFleetCarrier = true,
            },
            Status(GuiFocus.NoFocus),
            updatedIsSquadronBankOpen: true
        );

        Assert.True(viewModel.HasWarning);
        Assert.True(viewModel.ShouldAutoShow);
        Assert.Equal(
            "The current Fleet Carrier is not linked to this commander in Raven Colonial.",
            viewModel.WarningText
        );
    }

    private static ColonizationCommodityPlan Plan()
    {
        return new ColonizationCommodityPlan
        {
            Title = "Test build (no_truss)",
            ProjectNames = ["Test build (no_truss)"],
            Rows =
            [
                new ColonizationCommodityPlanRow
                {
                    Commodity = "steel",
                    DisplayName = "Steel",
                    Category = "Metals",
                    Needed = 100,
                    InShip = 20,
                    OnFleetCarriers = 20,
                    IsAssignedToCommander = true,
                    IsAssignedToOther = false,
                },
            ],
            FleetCarriers =
            [
                new ColonizationFleetCarrier
                {
                    MarketId = 1,
                    Name = "ABC-123",
                    DisplayName = "Supply carrier",
                },
            ],
            TotalRemaining = 100,
            TripsInCurrentShip = 2,
            FleetCarrierDeficit = 80,
            FleetCarrierDeficitTrips = 2,
            IsAtConstructionSite = false,
            IsLocalProjectUntracked = false,
            IsDockedAtUntrackedFleetCarrier = false,
            IsConstructionComplete = false,
            IsConstructionFailed = false,
        };
    }

    private static EliteStatus Status(GuiFocus focus)
    {
        return new EliteStatus { GuiFocus = focus, Flags = StatusFlags.Docked };
    }
}
