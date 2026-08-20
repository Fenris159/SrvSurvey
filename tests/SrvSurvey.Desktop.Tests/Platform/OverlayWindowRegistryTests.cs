using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayWindowRegistryTests
{
    [AvaloniaFact]
    public void RegisteredPresentationsFollowGalaxyMapVisibilityWithoutLosingIntent()
    {
        var registry = new OverlayWindowRegistry();
        var galaxyMapWindow = new Window();
        var biologyWindow = new Window();
        var galaxyMapContent = new Border();
        var biologyContent = new Border();
        var changes = 0;
        registry.Changed += (_, _) => changes++;

        registry.Register(galaxyMapWindow, "PlotGalMap");
        registry.Register(biologyWindow, "PlotBioSystem");
        registry.Register(galaxyMapWindow, "PlotGalMap");
        registry.SetPresentationVisual(galaxyMapWindow, galaxyMapContent);
        registry.SetPresentationVisual(biologyWindow, biologyContent);

        Assert.True(registry.TryGetPlotterName(
            galaxyMapWindow,
            out var plotterName));
        Assert.Equal("PlotGalMap", plotterName);
        Assert.Equal(4, changes);
        Assert.Equal(
            ["PlotGalMap", "PlotBioSystem"],
            registry.Snapshot().Select(entry => entry.PlotterName).ToArray());
        Assert.All(registry.Snapshot(), entry => Assert.True(entry.IsVisible));

        registry.SetGalaxyMapContextActive(active: true);

        Assert.True(registry.IsGalaxyMapContextActive);
        Assert.True(registry.Snapshot().Single(entry =>
            entry.PlotterName == "PlotGalMap").IsVisible);
        Assert.False(registry.Snapshot().Single(entry =>
            entry.PlotterName == "PlotBioSystem").IsVisible);
        Assert.Same(
            biologyContent,
            registry.Snapshot().Single(entry =>
                entry.PlotterName == "PlotBioSystem").RenderSource);

        registry.SetPresentationVisible(galaxyMapWindow, visible: false);
        registry.SetPresentationVisible(galaxyMapWindow, visible: false);
        registry.SetGalaxyMapContextActive(active: false);

        Assert.False(registry.IsGalaxyMapContextActive);
        Assert.False(registry.Snapshot().Single(entry =>
            entry.PlotterName == "PlotGalMap").IsVisible);
        Assert.True(registry.Snapshot().Single(entry =>
            entry.PlotterName == "PlotBioSystem").IsVisible);
    }

    [AvaloniaFact]
    public void RegistrationRejectsConflictingIdentityAndIgnoresUnknownWindows()
    {
        var registry = new OverlayWindowRegistry();
        var registered = new Window();
        var unknown = new Window();
        registry.Register(registered, "PlotJumpInfo");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register(registered, "PlotBioSystem"));
        Assert.Contains("PlotJumpInfo", exception.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            registry.Register(unknown, "PlotUnknown"));
        Assert.False(registry.TryGetPlotterName(unknown, out var plotterName));
        Assert.Empty(plotterName);

        registry.SetPresentationVisual(unknown, new Border());
        registry.SetPresentationVisible(unknown, visible: true);
        registry.SetPresentationVisual(registered, null);
        registry.SetPresentationVisible(registered, visible: true);
        registry.SetGalaxyMapContextActive(active: false);

        var snapshot = Assert.Single(registry.Snapshot());
        Assert.Same(registered, snapshot.Window);
        Assert.Same(registered, snapshot.RenderSource);
        Assert.False(snapshot.IsVisible);
    }

    [AvaloniaFact]
    public void SeparateRuntimeWindowsAreSuppressedAndRestoredAroundGalaxyMap()
    {
        var registry = new OverlayWindowRegistry();
        var mapWindow = new Window();
        var ordinaryWindow = new Window();
        registry.Register(mapWindow, "PlotGalMap");
        registry.Register(ordinaryWindow, "PlotBioSystem");
        mapWindow.Show();
        ordinaryWindow.Show();

        registry.SetGalaxyMapContextActive(active: true);

        Assert.True(mapWindow.IsVisible);
        Assert.False(ordinaryWindow.IsVisible);

        ordinaryWindow.Show();
        Assert.False(ordinaryWindow.IsVisible);

        registry.SetGalaxyMapContextActive(active: false);

        Assert.True(ordinaryWindow.IsVisible);
        mapWindow.Close();
        ordinaryWindow.Close();
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public void GalaxyMapContextKeepsOnlyMapGuidancePresentationsVisible()
    {
        Assert.True(OverlayWindowRegistry.ShouldPresentInContext(
            "PlotGalMap",
            galaxyMapActive: true));
        Assert.True(OverlayWindowRegistry.ShouldPresentInContext(
            "PlotSphericalSearch",
            galaxyMapActive: true));
        Assert.True(OverlayWindowRegistry.ShouldPresentInContext(
            "PlotJumpInfo",
            galaxyMapActive: true));
        Assert.False(OverlayWindowRegistry.ShouldPresentInContext(
            "PlotBioSystem",
            galaxyMapActive: true));
        Assert.False(OverlayWindowRegistry.ShouldPresentInContext(
            "PlotRouteBio",
            galaxyMapActive: true));
        Assert.False(OverlayWindowRegistry.ShouldPresentInContext(
            "PlotPulse",
            galaxyMapActive: true));
        Assert.True(OverlayWindowRegistry.ShouldPresentInContext(
            "PlotBioSystem",
            galaxyMapActive: false));
    }

    [Fact]
    public void GlobalSuppressionRemainsInForceAfterLeavingGalaxyMap()
    {
        Assert.False(OverlayWindowRegistry.ResolvePresentationVisibility(
            "PlotBioSystem",
            requestedVisibility: false,
            galaxyMapActive: false));
        Assert.False(OverlayWindowRegistry.ResolvePresentationVisibility(
            "PlotBioSystem",
            requestedVisibility: true,
            galaxyMapActive: true));
        Assert.True(OverlayWindowRegistry.ResolvePresentationVisibility(
            "PlotBioSystem",
            requestedVisibility: true,
            galaxyMapActive: false));
    }

    [AvaloniaFact]
    public void UserVisibilitySuppressesAndRestoresRegisteredPanels()
    {
        var registry = new OverlayWindowRegistry();
        var presentationWindow = new Window();
        var separateWindow = new Window();
        registry.Register(presentationWindow, "PlotBioSystem");
        registry.Register(separateWindow, "PlotRouteBio");
        registry.SetPresentationVisual(presentationWindow, new Border());
        separateWindow.Show();

        registry.SetUserVisibility("PlotBioSystem", visible: false);
        registry.SetUserVisibility("PlotRouteBio", visible: false);

        Assert.False(registry.ShouldPresent("PlotBioSystem"));
        Assert.False(registry.ShouldPresent("PlotRouteBio"));
        Assert.False(registry.Snapshot().Single(entry =>
            entry.PlotterName == "PlotBioSystem").IsVisible);
        Assert.False(separateWindow.IsVisible);

        registry.SetUserVisibility("PlotBioSystem", visible: true);
        registry.SetUserVisibility("PlotRouteBio", visible: true);

        Assert.True(registry.ShouldPresent("PlotBioSystem"));
        Assert.True(registry.Snapshot().Single(entry =>
            entry.PlotterName == "PlotBioSystem").IsVisible);
        Assert.True(separateWindow.IsVisible);
        presentationWindow.Close();
        separateWindow.Close();
    }

    [AvaloniaFact]
    public void EditorSuppressionPreservesRequestedIntentAndHostEligibility()
    {
        var registry = new OverlayWindowRegistry();
        var presentationWindow = new Window();
        var separateWindow = new Window();
        registry.Register(presentationWindow, "PlotBioSystem");
        registry.Register(separateWindow, "PlotRouteBio");
        registry.SetPresentationVisual(presentationWindow, new Border());
        separateWindow.Show();

        registry.SetEditorSuppressed(suppressed: true);

        Assert.All(
            new[] { presentationWindow, separateWindow },
            window =>
            {
                var decision = registry.GetDecision(window);
                Assert.True(decision.ShouldHost);
                Assert.False(decision.ShouldPresent);
                Assert.Equal(
                    OverlayVisibilityReason.EditorSuppressed,
                    decision.Reasons);
            });
        Assert.All(registry.Snapshot(), entry => Assert.False(entry.IsVisible));

        registry.SetEditorSuppressed(suppressed: false);

        Assert.All(registry.Snapshot(), entry => Assert.True(entry.IsVisible));
        presentationWindow.Close();
        separateWindow.Close();
    }

    [AvaloniaFact]
    public void GlobalSuppressionRecordsDistinctLifecycleReasons()
    {
        var registry = new OverlayWindowRegistry();
        var window = new Window();
        registry.Register(window, "PlotJumpInfo");
        window.Show();

        registry.SetGlobalSuppression(
            manualSuppressed: true,
            suitSuppressed: true,
            sessionSuppressed: true);

        var decision = registry.GetDecision(window);
        Assert.False(decision.Permitted);
        Assert.False(decision.ShouldHost);
        Assert.False(decision.ShouldPresent);
        Assert.Equal(
            OverlayVisibilityReason.ManualSuppressed
            | OverlayVisibilityReason.SuitSuppressed
            | OverlayVisibilityReason.SessionSuppressed,
            decision.Reasons);
        Assert.False(window.IsVisible);

        registry.SetGlobalSuppression(
            manualSuppressed: false,
            suitSuppressed: false,
            sessionSuppressed: false);

        Assert.True(window.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void RestoringPolicyDoesNotShowAWindowThatWasNeverRequested()
    {
        var registry = new OverlayWindowRegistry();
        var window = new Window();
        registry.Register(window, "PlotJumpInfo");

        registry.SetEditorSuppressed(suppressed: true);
        registry.SetEditorSuppressed(suppressed: false);

        Assert.False(window.IsVisible);
        Assert.Equal(
            OverlayVisibilityReason.DomainNotRequested,
            registry.GetDecision(window).Reasons);
        window.Close();
    }

    [AvaloniaFact]
    public void PresentationRequestRemainsOffAcrossEditorSuppression()
    {
        var registry = new OverlayWindowRegistry();
        var window = new Window();
        registry.Register(window, "PlotJumpInfo");
        registry.SetPresentationVisual(window, new Border());
        registry.SetPresentationVisible(window, visible: false);

        registry.SetEditorSuppressed(suppressed: true);
        registry.SetEditorSuppressed(suppressed: false);

        Assert.False(Assert.Single(registry.Snapshot()).IsVisible);
        Assert.Equal(
            OverlayVisibilityReason.DomainNotRequested,
            registry.GetDecision(window).Reasons);
        window.Close();
    }

    [AvaloniaFact]
    public void PriorityUsesPresentedStateAndRestoresWhenTheBlockerIsHidden()
    {
        var registry = new OverlayWindowRegistry();
        var guardianWindow = new Window();
        var biologyWindow = new Window();
        registry.Register(guardianWindow, "PlotGuardians");
        registry.Register(biologyWindow, "PlotBioSystem");
        biologyWindow.Show();
        guardianWindow.Show();

        Assert.True(guardianWindow.IsVisible);
        Assert.False(biologyWindow.IsVisible);
        Assert.Equal(
            OverlayVisibilityReason.PriorityObscured,
            registry.GetDecision(biologyWindow).Reasons);

        registry.SetUserVisibility("PlotGuardians", visible: false);

        Assert.False(guardianWindow.IsVisible);
        Assert.True(biologyWindow.IsVisible);
        Assert.Equal(
            OverlayVisibilityReason.None,
            registry.GetDecision(biologyWindow).Reasons);
        guardianWindow.Close();
        biologyWindow.Close();
    }

    [AvaloniaFact]
    public void PriorityAggregatesPresentedStateAcrossPlotterRegistrations()
    {
        var registry = new OverlayWindowRegistry();
        var firstGuardianWindow = new Window();
        var secondGuardianWindow = new Window();
        var surfaceWindow = new Window();
        registry.Register(firstGuardianWindow, "PlotGuardians");
        registry.Register(secondGuardianWindow, "PlotGuardians");
        registry.Register(surfaceWindow, "PlotGrounded");
        surfaceWindow.Show();
        firstGuardianWindow.Show();
        secondGuardianWindow.Show();

        firstGuardianWindow.Close();

        Assert.False(surfaceWindow.IsVisible);
        Assert.Equal(
            OverlayVisibilityReason.PriorityObscured,
            registry.GetDecision(surfaceWindow).Reasons);

        secondGuardianWindow.Close();

        Assert.True(surfaceWindow.IsVisible);
        surfaceWindow.Close();
    }

    [AvaloniaFact]
    public void ForcedSystemSurveyFactsOverrideTheGuardianSummaryPriority()
    {
        var registry = new OverlayWindowRegistry();
        var guardianSummaryWindow = new Window();
        var fssWindow = new Window();
        registry.Register(guardianSummaryWindow, "PlotGuardianSystem");
        registry.Register(fssWindow, "PlotFSSInfo");
        fssWindow.Show();
        guardianSummaryWindow.Show();

        Assert.False(fssWindow.IsVisible);
        Assert.True(guardianSummaryWindow.IsVisible);

        registry.SetPriorityFacts(OverlayPriorityFact.FssInfoForced);

        Assert.True(fssWindow.IsVisible);
        Assert.False(guardianSummaryWindow.IsVisible);
        Assert.Equal(
            OverlayVisibilityReason.PriorityObscured,
            registry.GetDecision(guardianSummaryWindow).Reasons);
        guardianSummaryWindow.Close();
        fssWindow.Close();
    }

    [Fact]
    public void PriorityRulesAreAcyclic()
    {
        OverlayPriorityRules.ValidateAcyclic();
    }

    [Fact]
    public void UserVisibilityParticipatesInPresentationResolution()
    {
        Assert.False(OverlayWindowRegistry.ResolvePresentationVisibility(
            "PlotGalMap",
            requestedVisibility: true,
            galaxyMapActive: false,
            userVisible: false));
    }

    [Fact]
    public void CatalogExplicitlyLimitsGalaxyMapPanels()
    {
        Assert.Equal(
            [
                "PlotBodyInfo",
                "PlotBuildCommodities",
                "PlotFSSInfo",
                "PlotFloatie",
                "PlotGalMap",
                "PlotJumpInfo",
                "PlotSphericalSearch",
                "PlotStationInfo",
            ],
            OverlayLayoutCatalog.Supported
                .Where(definition => definition.ShowInGalaxyMap)
                .Select(definition => definition.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }
}
