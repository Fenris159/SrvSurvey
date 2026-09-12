using Avalonia;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.Tests.Controls;

public sealed class MineMapControlTests
{
    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(7.5, 7.5)]
    [InlineData(20, 15)]
    public void ViewportZoomUsesGuardianMapLimits(double requested, double expected)
    {
        Assert.Equal(expected, MineMapControl.NormalizeViewportZoom(requested));
    }

    [Fact]
    public void ViewportPanResetsAtFitAndClampsAtHigherZoom()
    {
        Assert.Equal(default, MineMapControl.ClampViewportOffset(new Vector(100, -100), new Size(400, 300), 1));
        Assert.Equal(
            new Vector(200, -150),
            MineMapControl.ClampViewportOffset(new Vector(500, -500), new Size(400, 300), 2)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(225)]
    public void CommanderMarkerUsesTheGuardianHeadingGeometry(double heading)
    {
        var location = new Point(100, 200);
        const double radius = 6;

        Assert.Equal(
            GuardianSiteMapControl.GetCommanderHeadingEnd(location, radius, heading),
            MineMapControl.GetCommanderHeadingEnd(location, radius, heading)
        );
    }

    [Theory]
    [InlineData(1, 62.5, 250)]
    [InlineData(4, 250, 1000)]
    [InlineData(15, 937.5, 3750)]
    public void DistanceRingsRetainFixedKilometerLabelsAndGrowWithZoom(
        double zoom,
        double expectedFirstRadius,
        double expectedLastRadius
    )
    {
        IReadOnlyList<MineMapControl.DistanceRing> rings = MineMapControl.CreateDistanceRings(250, zoom, 4);

        Assert.Equal([1d, 2d, 3d, 4d], rings.Select(ring => ring.Kilometers));
        Assert.Equal(expectedFirstRadius, rings[0].RadiusPixels);
        Assert.Equal(expectedLastRadius, rings[^1].RadiusPixels);
    }

    [Fact]
    public void MapRingsExtendToTheWholeKilometerThatEnclosesTheLocationBorder()
    {
        int mapRadius = MineMapControl.GetMapRadiusKilometers(6_340);
        IReadOnlyList<MineMapControl.DistanceRing> rings = MineMapControl.CreateDistanceRings(350, 1, mapRadius);

        Assert.Equal(7, mapRadius);
        Assert.Equal([1d, 2d, 3d, 4d, 5d, 6d, 7d], rings.Select(ring => ring.Kilometers));
        Assert.Equal(350, rings[^1].RadiusPixels);
    }

    [Fact]
    public void MarkerScaleGrowsWithZoomWithoutObscuringNearbyDeposits()
    {
        var fitScale = MineMapControl.GetMarkerScale(1);
        var mediumScale = MineMapControl.GetMarkerScale(4);
        var maximumScale = MineMapControl.GetMarkerScale(15);

        Assert.Equal(1, fitScale);
        Assert.True(mediumScale > fitScale);
        Assert.True(maximumScale > mediumScale);
        Assert.InRange(maximumScale, 2, 4);
    }

    [Fact]
    public void PlanningCircleUsesFixedRadiusAndConvertsMapPointToSurfaceCoordinate()
    {
        var survey = new MineMapSurvey
        {
            Center = new SurfaceCoordinate(14.2609, -79.3291),
            PlanetRadiusMeters = 855_573.1875,
        };

        var location = MineMapControl.ToSurfaceCoordinate(survey, new Point(300, 200), new Point(200, 200), 0.1);

        Assert.Equal(4_500, MineMapControl.PlanningCircleRadiusMeters);
        Assert.InRange(
            SurfaceNavigation.GetDistance(survey.Center, location, survey.PlanetRadiusMeters),
            999.9,
            1_000.1
        );
        Assert.InRange(SurfaceNavigation.GetBearing(survey.Center, location), 89.99, 90.01);
    }
}
