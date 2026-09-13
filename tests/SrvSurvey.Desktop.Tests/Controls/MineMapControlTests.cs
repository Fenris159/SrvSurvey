using Avalonia;
using Avalonia.Media;
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
    [InlineData(100, 120, 0, 100, 20)]
    [InlineData(80, 100, 90, 180, 100)]
    [InlineData(100, 80, 180, 100, 180)]
    [InlineData(120, 100, 270, 20, 100)]
    public void PlayerSightLineEndsAtTheOutermostMapRing(
        double playerX,
        double playerY,
        double heading,
        double expectedX,
        double expectedY
    )
    {
        var mapCenter = new Point(100, 100);
        var player = new Point(playerX, playerY);
        Point start = MineMapControl.GetCommanderHeadingEnd(player, radius: 6, heading);

        Point end = Assert.IsType<Point>(MineMapControl.GetSightLineEnd(start, mapCenter, 80, heading));

        Assert.Equal(expectedX, end.X, precision: 6);
        Assert.Equal(expectedY, end.Y, precision: 6);
    }

    [Fact]
    public void PlayerSightLineIsOmittedWhenTheFacingTipPointsAwayFromOutsideTheMapRing()
    {
        Point? end = MineMapControl.GetSightLineEnd(
            new Point(190, 100),
            new Point(100, 100),
            outerRingRadius: 80,
            heading: 90
        );

        Assert.Null(end);
    }

    [Fact]
    public void PlayerSightLineUsesVisibleRoundDots()
    {
        Pen pen = MineMapControl.CreateSightLinePen(Brushes.White, markerScale: 2);

        Assert.Equal(DashStyle.Dot, pen.DashStyle);
        Assert.Equal(PenLineCap.Round, pen.LineCap);
        Assert.Equal(2.5, pen.Thickness);
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

    [Theory]
    [InlineData(1, 250)]
    [InlineData(4, 1000)]
    [InlineData(15, 3750)]
    public void BearingSpokesReachTheOutermostDistanceRingAtEveryZoom(double zoom, double expectedRadius)
    {
        Assert.Equal(expectedRadius, MineMapControl.GetBearingSpokeRadius(250, zoom));
        Assert.Equal(
            MineMapControl.CreateDistanceRings(250, zoom, mapRadiusKilometers: 4)[^1].RadiusPixels,
            MineMapControl.GetBearingSpokeRadius(250, zoom)
        );
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

    [Theory]
    [InlineData(true, null, "Ruby")]
    [InlineData(true, 4, "Ruby [4]")]
    [InlineData(false, 4, "[4]")]
    [InlineData(false, null, "")]
    public void RigCountRemainsInMarkerLabelWhenMaterialNamesAreHidden(
        bool showMaterial,
        int? rigCount,
        string expected
    )
    {
        var marker = new MineMapMarker { Material = "Ruby", RigCount = rigCount };

        Assert.Equal(expected, MineMapControl.BuildMarkerLabel(marker, showMaterial));
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
