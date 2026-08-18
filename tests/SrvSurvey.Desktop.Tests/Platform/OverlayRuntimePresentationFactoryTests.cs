using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayRuntimePresentationFactoryTests
{
    [Fact]
    public void StatefulPlottersExposeNamedEditorPreviewStates()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["PlotBioSystem"] =
                ["System overview", "Body predictions", "Body identified"],
            ["PlotBioStatus"] =
                ["Active sample", "Signal summary", "DSS required", "Stale sample"],
            ["PlotGuardianStatus"] =
            [
                "Obelisk target",
                "Site type choice",
                "Heading choice",
                "Site origin",
                "On-foot relic",
                "POI choice",
                "No nearby point",
                "Glide approach",
            ],
            ["PlotFleetCarrierRoute"] =
                ["Jump cooldown", "Jump scheduled", "Route only"],
            ["PlotPulse"] =
                ["SCO cooling", "SCO active", "SCO ready", "Journal pulse"],
        };

        foreach (var (plotterName, stateNames) in expected)
        {
            Assert.Equal(
                stateNames,
                OverlayRuntimePresentationFactory
                    .GetEditorPreviewStates(plotterName)
                    .Select(state => state.DisplayName));
        }

        Assert.Equal(
            ["Default"],
            OverlayRuntimePresentationFactory
                .GetEditorPreviewStates("PlotFSSInfo")
                .Select(state => state.DisplayName));
    }

    [Fact]
    public void EveryRegisteredEditorPreviewStateBuildsSharedData()
    {
        foreach (var definition in OverlayLayoutCatalog.Supported)
        {
            var states = OverlayRuntimePresentationFactory
                .GetEditorPreviewStates(definition.Name);
            for (var index = 0; index < states.Count; index++)
            {
                var dataContext = OverlayRuntimePresentationFactory
                    .CreateEditorDataContextOnly(definition.Name, index);
                Assert.NotNull(dataContext);
                (dataContext as IDisposable)?.Dispose();
            }

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                    definition.Name,
                    states.Count));
        }
    }

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

    [Fact]
    public void BiologyBodyStatesKeepSignalCountsConsistentWithTheirRows()
    {
        var predictions = CreateSystemSurveyState("PlotBioSystem", 1)
            .Survey.BiologySurveyDisplay;
        Assert.Equal("BODY PREDICTIONS", predictions.Title);
        Assert.Equal("4 biological signals", predictions.ProgressText);
        Assert.Equal(4, predictions.OrganismGroups.Count);
        Assert.All(predictions.Organisms, organism =>
            Assert.True(organism.IsPrediction));

        var identified = CreateSystemSurveyState("PlotBioSystem", 2)
            .Survey.BiologySurveyDisplay;
        Assert.Equal("IDENTIFIED BIO", identified.Title);
        Assert.Equal("3 biological signals", identified.ProgressText);
        Assert.Equal(3, identified.OrganismGroups.Count);
        Assert.DoesNotContain(identified.Organisms, organism =>
            organism.IsPrediction);
    }

    [Fact]
    public void BiologyStatusStatesRepresentDistinctGameConditions()
    {
        var active = CreateSystemSurveyState("PlotBioStatus", 0);
        Assert.True(active.Survey.BiologyStatus!.HasActiveSample);
        Assert.Equal(
            active.Survey.BiologyStatus.CompletionPercent,
            active.Survey.BiologyStatus.TrackedCompletionPercent);
        Assert.Equal(100d / 3d, active.Survey.BiologyStatus.CompletionPercent, 6);

        var summary = CreateSystemSurveyState("PlotBioStatus", 1);
        Assert.True(summary.Survey.BiologyStatus!.ShowSignalSummary);

        var dss = CreateSystemSurveyState("PlotBioStatus", 2);
        Assert.True(dss.Survey.BiologyStatus!.RequiresDss);
        Assert.Equal(0, dss.Survey.BiologyStatus.CompletionPercent);
        Assert.Equal(0, dss.Survey.BiologyStatus.TrackedCompletionPercent);

        var stale = CreateSystemSurveyState("PlotBioStatus", 3);
        Assert.True(stale.Survey.BiologyStatus!.IsStaleActiveSample);
        Assert.False(stale.Survey.BiologyStatus.HasActiveSample);
    }

    [Fact]
    public void FleetCarrierAndPulseStatesRepresentDistinctTimingConditions()
    {
        using var cooldown = Assert.IsType<FleetCarrierRouteOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotFleetCarrierRoute",
                0));
        Assert.True(cooldown.HasCountdown);
        Assert.Equal("JUMP COOLDOWN", cooldown.CountdownTitle);

        using var scheduled = Assert.IsType<FleetCarrierRouteOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotFleetCarrierRoute",
                1));
        Assert.True(scheduled.HasCountdown);
        Assert.Equal("JUMP DEPARTURE", scheduled.CountdownTitle);

        using var routeOnly = Assert.IsType<FleetCarrierRouteOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotFleetCarrierRoute",
                2));
        Assert.False(routeOnly.HasCountdown);

        var cooling = CreatePulseState(0);
        Assert.True(cooling.IsScoCoolingDown);
        var active = CreatePulseState(1);
        Assert.True(active.IsScoActive);
        var ready = CreatePulseState(2);
        Assert.True(ready.IsScoReady);
        ready.Refresh();
        Assert.True(ready.IsScoReady);
        var journal = CreatePulseState(3);
        Assert.False(journal.IsScoActive);
        Assert.False(journal.IsScoCoolingDown);
        Assert.False(journal.IsScoReady);
        Assert.True(journal.ShouldShow);
    }

    [Fact]
    public void GuardianStatusStatesExposeEveryConditionalBranch()
    {
        var visibleBranches = new Func<IGuardianOverlayPresentationState, bool>[]
        {
            state => state.IsGuardianObeliskVisible,
            state => state.IsGuardianSiteTypeChoiceVisible,
            state => state.IsGuardianHeadingChoiceVisible,
            state => state.IsGuardianOriginVisible,
            state => state.IsGuardianOnFootRelicVisible,
            state => state.IsGuardianPoiChoiceVisible,
            state => state.IsGuardianNoPointVisible,
            state => state.IsGlideApproach,
        };

        GuardianSiteMapProjection? sharedProjection = null;
        for (var index = 0; index < visibleBranches.Length; index++)
        {
            var viewModel = Assert.IsType<GuardianOverlayViewModel>(
                OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                    "PlotGuardianStatus",
                    index));
            Assert.True(visibleBranches[index](viewModel.Guardian));
            sharedProjection ??= viewModel.Guardian.ActiveMapProjection;
            Assert.Same(
                sharedProjection,
                viewModel.Guardian.ActiveMapProjection);
            Assert.Equal(
                1,
                visibleBranches.Count(branch => branch(viewModel.Guardian)));
        }
    }

    [Fact]
    public void GuardianChoiceAndOriginPreviewsDescribeTheirActualControls()
    {
        var siteType = Assert.IsType<GuardianOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotGuardianStatus",
                1));
        Assert.Contains(
            "Cycle firegroup to choose",
            siteType.Guardian.GuardianChoiceGestureText);
        Assert.Contains(
            "toggle cockpit mode 2x",
            siteType.Guardian.GuardianChoiceGestureText);

        var heading = Assert.IsType<GuardianOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotGuardianStatus",
                2));
        Assert.DoesNotContain(
            "firegroup",
            heading.Guardian.BlinkGestureText,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "toggle cockpit mode 2x",
            heading.Guardian.BlinkGestureText,
            StringComparison.OrdinalIgnoreCase);

        var origin = Assert.IsType<GuardianOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotGuardianStatus",
                3));
        Assert.Contains("aerial guide", origin.Guardian.GuardianOriginFooter);
        Assert.Contains(".map", origin.Guardian.GuardianOriginFooter);
        Assert.DoesNotContain("Blink", origin.Guardian.GuardianOriginFooter);
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

    private static SystemSurveyOverlayViewModel CreateSystemSurveyState(
        string plotterName,
        int stateIndex) => Assert.IsType<SystemSurveyOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                plotterName,
                stateIndex));

    private static PulseOverlayViewModel CreatePulseState(int stateIndex) =>
        Assert.IsType<PulseOverlayViewModel>(
            OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
                "PlotPulse",
                stateIndex));
}
