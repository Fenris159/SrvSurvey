using SrvSurvey.Core.Settlements;

namespace SrvSurvey.Core.Tests.Settlements;

public sealed class HumanSiteMapProjectorTests
{
    [Fact]
    public void EmbeddedTemplatesProjectCompleteFiniteMapGeometry()
    {
        var catalog = HumanSiteTemplateCatalog.LoadEmbedded();
        var projector = new HumanSiteMapProjector();

        var projections = catalog.Templates
            .Select(template => projector.Project(template))
            .ToArray();

        Assert.Equal(28, projections.Length);
        Assert.Equal(48, projections.Sum(
            projection => projection.LandingPads.Count));
        Assert.Equal(398, projections.Sum(
            projection => projection.SecureDoors.Count));
        Assert.Equal(592, projections.Sum(
            projection => projection.NamedPoints.Count));
        Assert.Equal(143, projections.Sum(
            projection => projection.DataTerminals.Count));
        Assert.Empty(projections.SelectMany(
            projection => projection.ConflictZonePoints));
        Assert.Equal(3, projections.Sum(
            projection => projection.SkippedImplausiblePoints));
        Assert.All(projections, projection =>
        {
            Assert.NotEmpty(projection.Buildings);
            Assert.True(double.IsFinite(projection.MaximumDistance));
            Assert.True(projection.MaximumDistance > 0);
            Assert.All(
                projection.LandingPads
                    .Concat(projection.SecureDoors)
                    .Concat(projection.NamedPoints)
                    .Concat(projection.DataTerminals),
                point => Assert.True(point.Offset.IsPlausibleMapOffset()));
        });
    }

    [Fact]
    public void DisplayOptionsMatchLegacyPoiTogglesAndWarState()
    {
        var template = HumanSiteTemplateCatalog.LoadEmbedded()
            .Find(HumanSiteEconomy.Agriculture, 1)!;
        var projector = new HumanSiteMapProjector();

        var hidden = projector.Project(
            template,
            new HumanSiteMapDisplayOptions(
                ShowMedkits: false,
                ShowBatteries: false,
                ShowDataTerminals: false,
                ShowConflictZonePoints: true));

        Assert.DoesNotContain(hidden.NamedPoints,
            point => point.Name is "Medkit" or "Battery");
        Assert.Empty(hidden.DataTerminals);
        Assert.Equal(template.ConflictZonePoints.Count,
            hidden.ConflictZonePoints.Count);
    }

    [Fact]
    public void ConvertsGdiLineAndCubicPathTypesWithoutSystemDrawing()
    {
        var template = CreateTemplate(new HumanSiteBuildingPath(
            [
                new HumanSiteMapPoint(0, 0),
                new HumanSiteMapPoint(10, 0),
                new HumanSiteMapPoint(15, 0),
                new HumanSiteMapPoint(15, 10),
                new HumanSiteMapPoint(10, 10),
            ],
            [0, 1, 3, 3, 0x83],
            1));

        var projection = new HumanSiteMapProjector().Project(template);
        var path = Assert.Single(Assert.Single(
            projection.Buildings).Paths);

        Assert.Equal(HumanSitePathFillRule.NonZero, path.FillRule);
        Assert.Equal(
            [
                HumanSitePathSegmentKind.Move,
                HumanSitePathSegmentKind.Line,
                HumanSitePathSegmentKind.CubicBezier,
                HumanSitePathSegmentKind.Close,
            ],
            path.Segments.Select(segment => segment.Kind));
        Assert.Equal(new HumanSiteMapPoint(15, 0),
            path.Segments[2].First);
        Assert.Equal(new HumanSiteMapPoint(10, 10),
            path.Segments[2].Third);
    }

    [Fact]
    public void RejectsIncompleteCubicPath()
    {
        var template = CreateTemplate(new HumanSiteBuildingPath(
            [new HumanSiteMapPoint(0, 0), new HumanSiteMapPoint(1, 1)],
            [0, 3],
            0));

        Assert.Throws<InvalidDataException>(
            () => new HumanSiteMapProjector().Project(template));
    }

    private static HumanSiteTemplate CreateTemplate(
        HumanSiteBuildingPath path)
    {
        return new HumanSiteTemplate(
            HumanSiteEconomy.Agriculture,
            1,
            "Test",
            [
                new HumanSiteLandingPad(
                    new HumanSiteMapPoint(0, 0),
                    0,
                    0,
                    0,
                    HumanSiteLandingPadSize.Small),
            ],
            [],
            [],
            [],
            [],
            [new HumanSiteBuilding("HAB", [path])]);
    }
}
