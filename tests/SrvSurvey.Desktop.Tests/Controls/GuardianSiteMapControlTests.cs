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

    [Fact]
    public void LegendRetainsLegacyLabelsAndAddsStructureMarkersWhenPresent()
    {
        var ruins = new GuardianSiteMapProjection("Alpha", [], [], 1);
        var structure = new GuardianSiteMapProjection(
            "Lacrosse",
            [
                Point("P1", GuardianPoiType.Pylon),
                Point("C1", GuardianPoiType.Component),
            ],
            [],
            1);

        var ruinsLabels = GuardianSiteMapControl.CreateLegendLabels(ruins);
        var structureLabels = GuardianSiteMapControl.CreateLegendLabels(structure);

        Assert.Contains("Relic tower", ruinsLabels);
        Assert.Contains("Empty puddle", ruinsLabels);
        Assert.Contains("Obelisk", ruinsLabels);
        Assert.Contains("Site heading", ruinsLabels);
        Assert.Contains("Tower heading", ruinsLabels);
        Assert.Contains("Survey needed", ruinsLabels);
        Assert.DoesNotContain("Energy pylon", ruinsLabels);
        Assert.DoesNotContain("Component tower", ruinsLabels);
        Assert.Contains("Energy pylon", structureLabels);
        Assert.Contains("Component tower", structureLabels);
    }

    private static GuardianProjectedPoint Point(
        string name,
        GuardianPoiType type)
    {
        return new GuardianProjectedPoint(
            name,
            type,
            0,
            0,
            0,
            0,
            0,
            GuardianPoiStatus.Present,
            false,
            false,
            string.Empty,
            []);
    }
}
