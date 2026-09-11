using Avalonia;
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
    [InlineData(1, 50, 200)]
    [InlineData(4, 200, 800)]
    [InlineData(15, 750, 3000)]
    public void DistanceRingsRetainFixedKilometerLabelsAndGrowWithZoom(
        double zoom,
        double expectedFirstRadius,
        double expectedLastRadius
    )
    {
        var rings = MineMapControl.CreateDistanceRings(250, zoom);

        Assert.Equal([1d, 2d, 3d, 4d], rings.Select(ring => ring.Kilometers));
        Assert.Equal(expectedFirstRadius, rings[0].RadiusPixels);
        Assert.Equal(expectedLastRadius, rings[^1].RadiusPixels);
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
}
