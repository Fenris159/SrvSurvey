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
