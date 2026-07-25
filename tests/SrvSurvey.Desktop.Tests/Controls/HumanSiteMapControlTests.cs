using Avalonia;
using SrvSurvey.Core.Settlements;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.Tests.Controls;

public sealed class HumanSiteMapControlTests
{
    [Fact]
    public void CommanderRemainsAtViewportCenter()
    {
        var commander = new HumanSiteMapPoint(125, -40);
        var center = new Point(250, 300);

        var projected = HumanSiteMapControl.TransformMapPoint(
            commander,
            commander,
            217,
            center,
            6);

        Assert.Equal(center, projected);
    }

    [Fact]
    public void MapRotatesSoCommanderHeadingPointsUp()
    {
        var center = new Point(100, 100);
        var commander = new HumanSiteMapPoint(0, 0);

        var eastWhileFacingNorth = HumanSiteMapControl.TransformMapPoint(
            new HumanSiteMapPoint(10, 0),
            commander,
            0,
            center,
            2);
        var eastWhileFacingEast = HumanSiteMapControl.TransformMapPoint(
            new HumanSiteMapPoint(10, 0),
            commander,
            90,
            center,
            2);

        Assert.Equal(new Point(120, 100), eastWhileFacingNorth);
        Assert.Equal(100, eastWhileFacingEast.X, precision: 9);
        Assert.Equal(80, eastWhileFacingEast.Y, precision: 9);
    }
}
