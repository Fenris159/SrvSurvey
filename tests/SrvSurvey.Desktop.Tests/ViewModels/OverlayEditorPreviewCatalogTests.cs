using Avalonia.Media;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayEditorPreviewCatalogTests
{
    /// <summary>Prevents editor-only jump guidance rows that the live overlay never produces.</summary>
    [Fact]
    public void JumpInfoPreviewUsesLiveDetailRows()
    {
        using JumpInfoOverlayViewModel preview = Assert.IsType<JumpInfoOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotJumpInfo")
        );
        using JumpInfoViewModel jump = preview.JumpInfo;

        Assert.Contains(jump.DetailLines, line => line.Label == "Followed route");
        Assert.DoesNotContain(jump.DetailLines, line => line.Label is "Next hop" or "Neutron" or "Scoopable");
        Assert.Empty(jump.DataStatus);
    }

    /// <summary>Uses the live star class and followed-hop flags for each editor scenario.</summary>
    [Theory]
    [InlineData(0, "K", true, false, true)]
    [InlineData(1, "N", false, true, true)]
    [InlineData(2, "K", false, false, true)]
    [InlineData(3, "K", false, false, false)]
    public void JumpInfoPreviewGuidanceMatchesItsRoute(
        int state,
        string starClass,
        bool refuel,
        bool neutron,
        bool hasRoute
    )
    {
        using JumpInfoOverlayViewModel preview = Assert.IsType<JumpInfoOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotJumpInfo", state)
        );
        using JumpInfoViewModel jump = preview.JumpInfo;

        Assert.True(jump.PendingSummaryLoad.IsCompletedSuccessfully);
        Assert.True(jump.HasDiscoveryText);
        Assert.Contains("Discovered by CMDR Raven", jump.DiscoveryText);
        Assert.Equal("STAR CLASS " + starClass, jump.StarClass);
        Assert.Equal(starClass == "K", jump.IsScoopableStarClass);
        Assert.Equal(refuel, jump.HasRefuelGuidance);
        Assert.Equal(neutron, jump.HasNeutronGuidance);
        Assert.Equal(hasRoute, jump.HasRouteLegs);
        Assert.Equal(refuel || neutron, jump.DetailLines.Any(line => line.Label == "Followed route"));
        string expectedProgress = state switch
        {
            0 or 1 => "HOP 2 / 3",
            2 => "JUMP 2 OF 3",
            _ => "DIRECT TARGET",
        };
        Assert.Equal(expectedProgress, jump.JumpProgress);
        Assert.Equal(hasRoute ? "120.0 LY" : "40.0 LY", jump.TotalDistance);
        Assert.Empty(jump.DataStatus);
    }

    /// <summary>
    /// Checks that carrier preview shows jump based estimated time remaining.
    /// </summary>
    [Fact]
    public void CarrierPreviewShowsJumpBasedEstimatedTimeRemaining()
    {
        using FleetCarrierRouteOverlayViewModel preview = Assert.IsType<FleetCarrierRouteOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotFleetCarrierRoute", 0)
        );
        Assert.Equal("44 JUMPS LEFT • ETR: 14h 40min", preview.JumpsLeft);
    }

    [Fact]
    public async Task SphericalPreviewRejectsSessionMutationsWithoutFaulting()
    {
        using SphericalSearchOverlayViewModel preview = Assert.IsType<SphericalSearchOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotSphericalSearch", 0)
        );
        EventHandler<BoxelSearchSessionChangedEventArgs> handler = (_, _) =>
            throw new InvalidOperationException("Preview sessions never publish changes.");
        preview.Boxel.Session.Changed += handler;
        try
        {
            BoxelSearchOutcome outcome = await preview.Boxel.Session.ExecuteAsync(StopBoxelSearch.Instance);
            BoxelSearchOutcome cleared = await preview.Boxel.Session.ClearProfileAsync(
                BoxelSearchMessageCode.ProfileUnavailable
            );
            BoxelSearchOutcome switched = await preview.Boxel.Session.SwitchProfileAsync(
                new BoxelSearchProfile("F123", "Preview", true, BoxelSearchSnapshot.Empty)
            );
            BoxelSearchOutcome applied = await preview.Boxel.Session.ApplyAsync(new BoxelSearchUpdate());
            BoxelSearchLibrarySnapshot library = await preview.Boxel.Session.GetLibraryAsync();

            Assert.Equal(BoxelSearchOutcomeKind.Rejected, outcome.Kind);
            Assert.Equal(BoxelSearchMessageCode.SearchNotConfigured, outcome.Code);
            Assert.Equal(preview.Boxel.Session.Current.Version, outcome.SessionVersion);
            Assert.Equal(BoxelSearchOutcomeKind.Rejected, cleared.Kind);
            Assert.Equal(BoxelSearchMessageCode.ProfileUnavailable, cleared.Code);
            Assert.Equal(BoxelSearchOutcomeKind.Rejected, switched.Kind);
            Assert.Equal(BoxelSearchOutcomeKind.Rejected, applied.Kind);
            Assert.Empty(library.Entries);
        }
        finally
        {
            preview.Boxel.Session.Changed -= handler;
        }
    }

    [Fact]
    public void FlightWarningPreviewStatesUseDifficultyNames()
    {
        IReadOnlyList<OverlayEditorPreviewStateDefinition> states = OverlayEditorPreviewCatalog.GetStates(
            "PlotFlightWarning"
        );

        Assert.Equal(
            ["Noticeable", "Challenging", "High risk", "Expert only"],
            states.Select(state => state.DisplayName)
        );
    }

    [Theory]
    [InlineData(0, 255, 215, 0, false)]
    [InlineData(1, 255, 165, 0, false)]
    [InlineData(2, 255, 69, 0, false)]
    [InlineData(3, 220, 20, 60, true)]
    public void FlightWarningPreviewCyclesSeverityStates(
        int stateIndex,
        byte red,
        byte green,
        byte blue,
        bool expectedExtreme
    )
    {
        SystemSurveyOverlayViewModel overlay = Assert.IsType<SystemSurveyOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotFlightWarning", stateIndex)
        );

        ISolidColorBrush brush = Assert.IsType<Avalonia.Media.ISolidColorBrush>(
            overlay.Survey.FlightWarningBrush,
            exactMatch: false
        );
        Assert.Equal(Avalonia.Media.Color.FromRgb(red, green, blue), brush.Color);
        Assert.Equal(expectedExtreme, overlay.Survey.IsExtremeFlightWarning);
    }

    [Fact]
    public void SystemBiologyOverviewDemonstratesAlternativePredictionPips()
    {
        SystemSurveyOverlayViewModel overlay = Assert.IsType<SystemSurveyOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotBioSystem", 0)
        );

        Assert.Contains(
            overlay.Survey.BiologySurveyDisplay.Bodies,
            body => body.RewardBands.Count > body.SignalCount && body.HasAlternativeRewardBands
        );
    }

    [Fact]
    public void SystemBiologyOverviewUsesCompactLeftColumnRewardText()
    {
        SystemSurveyOverlayViewModel overlay = Assert.IsType<SystemSurveyOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotBioSystem", 0)
        );
        BiologySurveyViewModel biology = overlay.Survey.BiologySurveyDisplay;

        Assert.Equal("10.89–\n34.34 M", biology.Bodies.Single(body => body.Name == "A4").RewardText);
        Assert.Equal("20.70 M", biology.Bodies.Single(body => body.Name == "BC3").RewardText);
        Assert.Equal("Estimated reward:\n42.75 M – 106 M", biology.RewardSummary);
    }

    [Fact]
    public void BodyPredictionsUsesCompactHeadingAndRewardSummary()
    {
        SystemSurveyOverlayViewModel overlay = Assert.IsType<SystemSurveyOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotBioSystem", 1)
        );
        BiologySurveyViewModel biology = overlay.Survey.BiologySurveyDisplay;

        Assert.Equal("BODY PREDICTIONS", biology.Title);
        Assert.Equal(OverlayPreviewSimulationState.Default.CurrentBody, biology.Heading);
        Assert.DoesNotContain("biology", biology.Heading, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Estimated reward:\n11.13 M – 33.24 M", biology.RewardSummary);
    }

    [Fact]
    public void IdentifiedBioUsesCompactHeadingStatusAndRewardRows()
    {
        SystemSurveyOverlayViewModel overlay = Assert.IsType<SystemSurveyOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotBioSystem", 2)
        );
        BiologySurveyViewModel biology = overlay.Survey.BiologySurveyDisplay;

        Assert.Equal("IDENTIFIED BIO", biology.Title);
        Assert.Equal(OverlayPreviewSimulationState.Default.CurrentBody, biology.Heading);
        Assert.DoesNotContain("biology", biology.Heading, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("DSS Scan Complete\nExact Organisms Identified", biology.PredictionStatus);
        Assert.Equal("Known reward:\n121.82 M", biology.RewardSummary);
        Assert.Equal("First-footfall total:\n609.10 M", biology.FirstFootfallRewardSummary);
    }

    [Theory]
    [InlineData(false, 0, 3, false)]
    [InlineData(false, 1, 3, true)]
    [InlineData(false, 3, 3, false)]
    [InlineData(true, 0, 3, true)]
    public void RewardBandGroupHighlightMatchesLegacyBodyState(
        bool isDestination,
        int analyzedSignalCount,
        int signalCount,
        bool expected
    )
    {
        var body = new BiologyBodyRowViewModel
        {
            IsDestination = isDestination,
            AnalyzedSignalCount = analyzedSignalCount,
            SignalCount = signalCount,
        };

        Assert.Equal(expected, body.IsRewardBandGroupHighlighted);
    }

    /// <summary>Checks that preview totals, trips, and badges agree with the sample carrier and ship cargo.</summary>
    [Fact]
    public void ColonizationPreviewSummariesMatchItsRepresentativeCargo()
    {
        ColonizationCommodityOverlayViewModel preview = Assert.IsType<ColonizationCommodityOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotBuildCommodities", 0)
        );

        Assert.Equal(16, preview.Plan.Rows.Count);
        Assert.Equal(preview.Plan.Rows.Sum(row => (long)row.Needed), preview.Plan.TotalRemaining);
        Assert.Equal(
            preview.Plan.Rows.Sum(row => Math.Max(0L, (long)row.Needed - row.OnFleetCarriers)),
            preview.Plan.FleetCarrierDeficit
        );
        Assert.Equal((long)Math.Ceiling(preview.Plan.TotalRemaining / 512d), preview.Plan.TripsInCurrentShip);
        Assert.Equal(
            (long)Math.Ceiling(preview.Plan.FleetCarrierDeficit / 512d),
            preview.Plan.FleetCarrierDeficitTrips
        );
        Assert.False(preview.Plan.IsAtConstructionSite);
        Assert.Contains(preview.Groups, group => group.IsCollapsed);
        Assert.Contains(preview.Plan.Rows, row => row.IsUnavailableAtCurrentMarket);
        Assert.Contains(preview.Plan.Rows, row => row.CanCompleteFleetCarrierLoad);
        Assert.All(
            preview.Plan.Rows,
            row => Assert.Equal(row.OnFleetCarriers, preview.Plan.FleetCarriers[0].Cargo[row.Commodity])
        );
    }
}
