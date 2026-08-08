using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayRuntimePresentationFactoryTests
{
    [Fact]
    public void EveryCatalogPlotterIsRegisteredForSharedPresentation()
    {
        foreach (var definition in OverlayLayoutCatalog.Supported)
        {
            Assert.True(
                OverlayRuntimePresentationFactory.IsSupported(definition.Name),
                $"{definition.Name} is missing a shared presentation template.");
        }
    }

    [Fact]
    public void EveryCatalogPlotterCanBuildEditorDataWithoutXaml()
    {
        foreach (var definition in OverlayLayoutCatalog.Supported)
        {
            var dataContext = OverlayRuntimePresentationFactory
                .CreateEditorDataContextOnly(definition.Name);
            Assert.NotNull(dataContext);
        }
    }

    [Fact]
    public void RemainingEditorsHaveNonEmptyRepresentativeContent()
    {
        var colonization = Assert.IsType<ColonizationCommodityOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotBuildCommodities"));
        Assert.True(colonization.HasRows);
        Assert.NotEmpty(colonization.Groups);

        var notification = Assert.IsType<NotificationViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotFloatie"));
        Assert.True(notification.HasMessages);

        var combat = Assert.IsType<CombatOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotFootCombat"));
        Assert.Equal(22, combat.Combat.FootCombatKills);
        Assert.True(combat.Combat.HasMassacreMissions);

        var galMap = Assert.IsType<GalaxyMapOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotGalMap"));
        Assert.True(galMap.HasPrimarySystem);
        Assert.True(galMap.HasRouteFooter);

        var surface = Assert.IsType<SurfaceSurveyOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotGrounded"));
        Assert.True(surface.SurfaceSurvey.HasTrackers);
        Assert.True(surface.SurfaceSurvey.ShouldShowRadar);

        var routeBio = Assert.IsType<RouteBioOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotRouteBio"));
        Assert.NotEmpty(routeBio.Targets);

        var jump = Assert.IsType<JumpInfoOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotJumpInfo"));
        Assert.True(jump.JumpInfo.HasDetailLines);
        Assert.False(string.IsNullOrWhiteSpace(jump.JumpInfo.TargetName));
        Assert.DoesNotContain("UNKNOWN", jump.JumpInfo.TargetName);

        var fc = Assert.IsType<FleetCarrierRouteOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotFleetCarrierRoute"));
        Assert.True(fc.HasCountdown);
        Assert.True(fc.HasRestockWarning);

        var prior = Assert.IsType<PriorScansOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotPriorScans"));
        Assert.True(prior.HasSpecies);

        var quest = Assert.IsType<QuestIndicatorViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotQuestMini"));
        Assert.NotEmpty(quest.Objectives);

        var station = Assert.IsType<StationInfoOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotStationInfo"));
        Assert.True(station.StationInfo.HasRelevantServices);

        var ground = Assert.IsType<GroundTargetOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotTrackTarget"));
        Assert.True(ground.GroundTarget.HasIdealApproach);

        var multi = Assert.IsType<CommanderInstancesViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotMultiGameCommander"));
        Assert.Contains("Raven", multi.MultiGameOverlayLabel);

        var spherical = Assert.IsType<SphericalSearchOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotSphericalSearch"));
        Assert.False(string.IsNullOrWhiteSpace(spherical.SphereCenterSystemName));
        Assert.False(string.IsNullOrWhiteSpace(spherical.BoxelNextSystem));

        var human = Assert.IsType<HumanSiteOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotHumanSite"));
        Assert.Contains("Mitchell", human.HumanSite.SiteName);

        var pulse = Assert.IsType<PulseOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotPulse"));
        Assert.True(pulse.PulseHeight > 0);
        Assert.True(pulse.IsScoCoolingDown || pulse.IsScoReady);
    }

    [AvaloniaFact]
    public void SystemBiologyPresentationHostsSharedTemplateWithData()
    {
        Assert.True(OverlayRuntimePresentationFactory.TryCreate(
            "PlotBioSystem",
            out var presentation,
            out var dataContext));
        Assert.NotNull(presentation);
        var overlay = Assert.IsType<SystemSurveyOverlayViewModel>(
            dataContext);
        Assert.True(overlay.Survey.HasBiologySurvey);
        Assert.True(overlay.Survey.BiologySurveyDisplay.IsSystemOverview);
        Assert.NotEmpty(overlay.Survey.BiologySurveyDisplay.Bodies);
        Assert.Contains(
            overlay.Survey.BiologySurveyDisplay.Bodies,
            body => body.RewardBands.Any(band => band.IsPrediction));
        Assert.Same(dataContext, presentation.DataContext);
    }

    [AvaloniaFact]
    public void SystemStatusPresentationIncludesDssBodies()
    {
        Assert.True(OverlayRuntimePresentationFactory.TryCreate(
            "PlotSysStatus",
            out var presentation,
            out var dataContext));
        Assert.NotNull(presentation);
        var overlay = Assert.IsType<SystemSurveyOverlayViewModel>(
            dataContext);
        Assert.True(overlay.Survey.HasDssBodies);
        Assert.True(overlay.Survey.HasBiologicalBodies);
        Assert.False(string.IsNullOrWhiteSpace(overlay.Survey.SystemStatusText));
    }
}
