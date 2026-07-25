using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Settlements;

namespace SrvSurvey.Core.Tests.Settlements;

public sealed class HumanSiteNavigationTests
{
    [Fact]
    public void SiteOffsetAndSurfaceLocationRoundTrip()
    {
        var site = new SurfaceCoordinate(32.5, -117.25);
        var expected = new HumanSiteMapPoint(149.25, -82.5);

        var location = HumanSiteNavigation.GetSurfaceLocation(
            site,
            expected,
            6_000_000,
            73);
        var actual = HumanSiteNavigation.GetSiteOffset(
            site,
            location,
            6_000_000,
            73);

        Assert.Equal(expected.X, actual.X, precision: 4);
        Assert.Equal(expected.Y, actual.Y, precision: 4);
    }

    [Fact]
    public void InfersTemplateAndHeadingFromKnownPad()
    {
        const double radius = 6_000_000;
        const double heading = 231;
        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();
        var template = catalog.Find(HumanSiteEconomy.Extraction, 5)!;
        var origin = new SurfaceCoordinate(-12.5, 44.25);
        var pad = Assert.Single(template.LandingPads);
        var observerHeading = SurfaceNavigation.NormalizeDegrees(
            heading + pad.Rotation);
        var location = HumanSiteNavigation.GetSurfaceLocation(
            origin,
            pad.Offset,
            radius,
            heading);
        var site = CreateSite(
            origin,
            HumanSiteEconomy.Extraction,
            HumanSiteLandingPads.From(template));

        var solution = new HumanSiteNavigation(catalog).InferGeometry(
            site,
            location,
            observerHeading,
            radius,
            vehicle: "foot",
            targetPad: 1);

        Assert.NotNull(solution);
        Assert.Equal(5, solution.SubType);
        Assert.Equal("Ourea", solution.Template.Name);
        Assert.Equal(heading, solution.Heading, precision: 5);
        Assert.Equal(1, solution.PadNumber);
        Assert.InRange(solution.DistanceFromPadCenter, 0, 0.01);
    }

    [Fact]
    public void VehicleCockpitOffsetIsRemovedBeforeInference()
    {
        const double radius = 6_000_000;
        const double heading = 125;
        const string vehicle = "sidewinder";
        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();
        var template = catalog.Find(HumanSiteEconomy.Extraction, 5)!;
        var origin = new SurfaceCoordinate(5, 6);
        var pad = Assert.Single(template.LandingPads);
        var observerHeading = SurfaceNavigation.NormalizeDegrees(
            heading + pad.Rotation);
        var center = HumanSiteNavigation.GetSurfaceLocation(
            origin,
            pad.Offset,
            radius,
            heading);
        var cockpitCorrection = HumanSiteVehicleOffsets.Find(vehicle);
        var observed = HumanSiteNavigation.GetSurfaceLocation(
            center,
            new HumanSiteMapPoint(
                -cockpitCorrection.X,
                -cockpitCorrection.Y),
            radius,
            observerHeading);
        var site = CreateSite(
            origin,
            HumanSiteEconomy.Extraction,
            HumanSiteLandingPads.From(template));

        var solution = new HumanSiteNavigation(catalog).InferGeometry(
            site,
            observed,
            observerHeading,
            radius,
            vehicle,
            targetPad: 1);

        Assert.NotNull(solution);
        Assert.Equal(heading, solution.Heading, precision: 4);
        Assert.InRange(solution.DistanceFromPadCenter, 0, 0.02);
    }

    [Fact]
    public void WrongPadConfigurationDoesNotInferGeometry()
    {
        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();
        var site = CreateSite(
            new SurfaceCoordinate(0, 0),
            HumanSiteEconomy.Extraction,
            new HumanSiteLandingPads(9, 9, 9));

        var solution = new HumanSiteNavigation(catalog).InferGeometry(
            site,
            new SurfaceCoordinate(0, 0),
            0,
            6_000_000);

        Assert.Null(solution);
    }

    private static HumanSiteLiveSnapshot CreateSite(
        SurfaceCoordinate location,
        HumanSiteEconomy economy,
        HumanSiteLandingPads pads)
    {
        return new HumanSiteLiveSnapshot(
            "Test",
            "Test",
            1,
            2,
            3,
            "Test 1",
            new HumanSiteSurfaceLocation(
                location.Latitude,
                location.Longitude),
            economy,
            string.Empty,
            string.Empty,
            string.Empty,
            null,
            string.Empty,
            string.Empty,
            [],
            "OnFootSettlement",
            pads,
            0,
            null,
            null,
            HumanSiteDockingStatus.None,
            0,
            null,
            false,
            default,
            default);
    }
}
