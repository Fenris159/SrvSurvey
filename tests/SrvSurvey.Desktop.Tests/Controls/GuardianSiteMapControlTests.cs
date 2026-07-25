using Avalonia;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.Tests.Controls;

public sealed class GuardianSiteMapControlTests
{
    [Fact]
    public void CommanderRemainsAtViewportCenter()
    {
        var center = new Point(250, 300);
        var proximity = new GuardianSiteProximitySnapshot(
            50,
            40,
            -20,
            40,
            -20,
            null,
            null);
        const double scale = 3;

        var commander = GuardianSiteMapControl.TransformMapPoint(
            proximity.MapX,
            proximity.MapY,
            proximity,
            217,
            center,
            scale);

        Assert.Equal(center, commander);
    }

    [Fact]
    public void MapRotatesSoCommanderHeadingPointsUp()
    {
        var proximity = new GuardianSiteProximitySnapshot(
            0,
            0,
            0,
            0,
            0,
            null,
            null);
        var center = new Point(100, 100);

        var eastWhileFacingNorth = GuardianSiteMapControl.TransformMapPoint(
            10,
            0,
            proximity,
            0,
            center,
            2);
        var eastWhileFacingEast = GuardianSiteMapControl.TransformMapPoint(
            10,
            0,
            proximity,
            90,
            center,
            2);

        Assert.Equal(new Point(120, 100), eastWhileFacingNorth);
        Assert.Equal(100, eastWhileFacingEast.X, precision: 9);
        Assert.Equal(80, eastWhileFacingEast.Y, precision: 9);
    }

    [Fact]
    public void RejectsInvalidManualScale()
    {
        var proximity = new GuardianSiteProximitySnapshot(
            0,
            0,
            0,
            0,
            0,
            null,
            null);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GuardianSiteMapControl.TransformMapPoint(
                0,
                0,
                proximity,
                0,
                new Point(10, 10),
                double.NaN));
    }
}
