using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class OverlayWindowRegistryTests
{
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

    [Fact]
    public void CatalogExplicitlyLimitsGalaxyMapPanels()
    {
        Assert.Equal(
            ["PlotGalMap", "PlotJumpInfo", "PlotSphericalSearch"],
            OverlayLayoutCatalog.Supported
                .Where(definition => definition.ShowInGalaxyMap)
                .Select(definition => definition.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }
}
