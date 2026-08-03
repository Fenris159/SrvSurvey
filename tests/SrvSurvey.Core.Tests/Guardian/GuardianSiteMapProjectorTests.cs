using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Core.Tests.Guardian;

public sealed class GuardianSiteMapProjectorTests
{
    [Fact]
    public void ProjectsLegacyPolarCoordinatesIntoSiteOrientedMap()
    {
        var template = CreateTemplate(
        [
            new GuardianPointOfInterest("north", GuardianPoiType.Relic, 180, 10, 0),
            new GuardianPointOfInterest("east", GuardianPoiType.Orb, 270, 20, 0),
            new GuardianPointOfInterest("south", GuardianPoiType.Tablet, 0, 30, 0),
            new GuardianPointOfInterest("west", GuardianPoiType.Totem, 90, 40, 0),
        ]);

        var projection = new GuardianSiteMapProjector().Project(template);

        AssertPoint(projection.Points[0], 0, -10);
        AssertPoint(projection.Points[1], 20, 0);
        AssertPoint(projection.Points[2], 0, 30);
        AssertPoint(projection.Points[3], -40, 0);
        Assert.Equal(40, projection.MaximumDistance);
    }

    [Fact]
    public void ProjectionCarriesLegacyMapArtworkPlacementMetadata()
    {
        var template = new GuardianSiteTemplate(
            "Beta",
            "Beta",
            "beta-background.png",
            new GuardianMapPoint(487, 556),
            1.88,
            [new GuardianPointOfInterest(
                "p1",
                GuardianPoiType.Orb,
                0,
                10,
                0)],
            [],
            new Dictionary<string, GuardianMapPoint>());

        var projection = new GuardianSiteMapProjector().Project(template);

        Assert.Equal("beta-background.png", projection.BackgroundImage);
        Assert.Equal(new GuardianMapPoint(487, 556), projection.ImageOffset);
        Assert.Equal(1.88, projection.ImageScaleFactor);
    }

    [Fact]
    public void CombinesSurveyStateRawPointsAndActiveObelisks()
    {
        var template = CreateTemplate(
        [
            new GuardianPointOfInterest("A01", GuardianPoiType.Obelisk, 0, 10, 20),
            new GuardianPointOfInterest("p1", GuardianPoiType.Orb, 90, 20, 0),
        ]);
        var survey = new GuardianSurveyData
        {
            PoiStatuses = new Dictionary<string, GuardianPoiStatus>
            {
                ["p1"] = GuardianPoiStatus.Present,
            },
            RawPointsOfInterest =
            [
                new GuardianPointOfInterest(
                    "x1",
                    GuardianPoiType.Relic,
                    180,
                    30,
                    45),
            ],
        };

        var projection = new GuardianSiteMapProjector().Project(
            template,
            survey,
            [new GuardianObelisk("A01", "H1", true, ["ca"])],
            new HashSet<char> { 'A' });

        Assert.Equal(3, projection.Points.Count);
        var puddle = projection.Points.Single(point => point.Name == "p1");
        Assert.Equal(GuardianPoiStatus.Present, puddle.Status);
        var obelisk = projection.Points.Single(point => point.Name == "A01");
        Assert.True(obelisk.IsActiveObelisk);
        Assert.True(obelisk.IsScannedObelisk);
        Assert.Equal("H1", obelisk.LogCode);
        var raw = Assert.Single(
            projection.Points,
            point => point.Name == "x1");
        Assert.Equal(GuardianPoiStatus.Present, raw.Status);
    }

    [Fact]
    public void SelectedGroupsFilterObelisksAndProjectLabels()
    {
        var template = new GuardianSiteTemplate(
            "Test",
            "Test",
            string.Empty,
            new GuardianMapPoint(0, 0),
            1,
            [
                new GuardianPointOfInterest(
                    "A01",
                    GuardianPoiType.Obelisk,
                    0,
                    10,
                    0),
                new GuardianPointOfInterest(
                    "B01",
                    GuardianPoiType.Obelisk,
                    90,
                    20,
                    0),
                new GuardianPointOfInterest(
                    "p1",
                    GuardianPoiType.Orb,
                    180,
                    30,
                    0),
            ],
            [],
            new Dictionary<string, GuardianMapPoint>
            {
                ["A"] = new GuardianMapPoint(0, 15),
                ["B"] = new GuardianMapPoint(90, 25),
            });

        var projection = new GuardianSiteMapProjector().Project(
            template,
            obeliskGroups: new HashSet<char> { 'A' });

        Assert.Contains(projection.Points, point => point.Name == "A01");
        Assert.DoesNotContain(projection.Points, point => point.Name == "B01");
        Assert.Contains(projection.Points, point => point.Name == "p1");
        var group = Assert.Single(projection.Groups);
        Assert.Equal("A", group.Name);
        AssertPoint(group.X, group.Y, 0, 15);
    }

    [Fact]
    public void ComponentModeProjectsTowerMaterialsAndDestructiblePanels()
    {
        var template = new GuardianSiteTemplate(
            "Test",
            "Test",
            string.Empty,
            new GuardianMapPoint(0, 0),
            1,
            [
                new GuardianPointOfInterest(
                    "c1",
                    GuardianPoiType.Component,
                    0,
                    10,
                    0),
            ],
            [
                new GuardianPointOfInterest(
                    "d1",
                    GuardianPoiType.DestructiblePanel,
                    90,
                    20,
                    0),
            ],
            new Dictionary<string, GuardianMapPoint>());
        var survey = new GuardianSurveyData
        {
            ComponentMaterials = new Dictionary<
                string,
                GuardianComponentLoadout>
            {
                ["c1"] = new GuardianComponentLoadout(
                    "c1",
                    [
                        GuardianComponentMaterial.Cell,
                        GuardianComponentMaterial.Conduit,
                        GuardianComponentMaterial.Tech,
                    ]),
                ["d1"] = new GuardianComponentLoadout(
                    "d1",
                    [GuardianComponentMaterial.Tech]),
            },
        };
        var projector = new GuardianSiteMapProjector();

        var standard = projector.Project(template, survey);
        var componentMode = projector.Project(
            template,
            survey,
            includeComponentMaterials: true);

        Assert.DoesNotContain(standard.Points, point => point.Name == "d1");
        Assert.Equal(
            [
                GuardianComponentMaterial.Cell,
                GuardianComponentMaterial.Conduit,
                GuardianComponentMaterial.Tech,
            ],
            componentMode.Points.Single(point => point.Name == "c1")
                .ComponentMaterials);
        var panel = componentMode.Points.Single(point => point.Name == "d1");
        Assert.Equal(GuardianPoiStatus.Present, panel.Status);
        Assert.Equal(
            GuardianComponentMaterial.Tech,
            Assert.Single(panel.ComponentMaterials));
    }

    [Fact]
    public void ProjectsLegacyHeadingAndRamTahRenderingState()
    {
        var template = new GuardianSiteTemplate(
            "Alpha",
            "Alpha",
            string.Empty,
            new GuardianMapPoint(0, 0),
            1,
            [
                new GuardianPointOfInterest(
                    "t1",
                    GuardianPoiType.Relic,
                    0,
                    10,
                    0),
                new GuardianPointOfInterest(
                    "t2",
                    GuardianPoiType.Relic,
                    90,
                    20,
                    0),
                new GuardianPointOfInterest(
                    "A01",
                    GuardianPoiType.Obelisk,
                    180,
                    30,
                    25),
                new GuardianPointOfInterest(
                    "e1",
                    GuardianPoiType.EmptyPuddle,
                    270,
                    40,
                    0),
            ],
            [],
            new Dictionary<string, GuardianMapPoint>());
        var survey = new GuardianSurveyData
        {
            SiteHeading = 120,
            RelicTowerHeading = 35,
            RelicHeadings = new Dictionary<string, int>
            {
                ["t1"] = 220,
            },
        };

        var projection = new GuardianSiteMapProjector().Project(
            template,
            survey,
            [new GuardianObelisk("A01", "H1", false, [])],
            neededRamTahLogCodes: new HashSet<string>(
                ["H1"],
                StringComparer.OrdinalIgnoreCase));

        Assert.True(projection.IsRuins);
        Assert.Equal(120, projection.SiteHeading);
        Assert.Equal(35, projection.RelicTowerHeading);
        var individual = projection.Points.Single(point => point.Name == "t1");
        Assert.Equal(220, individual.RelicHeading);
        Assert.True(individual.HasIndividualRelicHeading);
        var general = projection.Points.Single(point => point.Name == "t2");
        Assert.Equal(35, general.RelicHeading);
        Assert.False(general.HasIndividualRelicHeading);
        Assert.True(projection.Points.Single(point => point.Name == "A01")
            .IsRamTahNeededObelisk);
        Assert.Equal(
            GuardianPoiStatus.Empty,
            projection.Points.Single(point => point.Name == "e1").Status);
    }

    [Fact]
    public void StructuresDoNotApplyGeneralRelicHeadingFallback()
    {
        var template = new GuardianSiteTemplate(
            "Lacrosse",
            "Lacrosse",
            string.Empty,
            new GuardianMapPoint(0, 0),
            1,
            [
                new GuardianPointOfInterest(
                    "t1",
                    GuardianPoiType.Relic,
                    0,
                    10,
                    0),
            ],
            [],
            new Dictionary<string, GuardianMapPoint>());

        var projection = new GuardianSiteMapProjector().Project(
            template,
            new GuardianSurveyData
            {
                SiteHeading = 10,
                RelicTowerHeading = 90,
            });

        Assert.False(projection.IsRuins);
        Assert.Equal(-1, Assert.Single(projection.Points).RelicHeading);
    }

    [Fact]
    public void EmbeddedTemplatesAllProduceFiniteMapGeometry()
    {
        var templates = GuardianSiteTemplateCatalog.LoadEmbedded();
        var projector = new GuardianSiteMapProjector();

        Assert.Equal(13, templates.Templates.Count);
        foreach (var template in templates.Templates)
        {
            var projection = projector.Project(template);
            Assert.NotEmpty(projection.Points);
            Assert.True(double.IsFinite(projection.MaximumDistance));
            Assert.True(projection.MaximumDistance > 0);
            Assert.All(
                projection.Points,
                point =>
                {
                    Assert.True(double.IsFinite(point.X));
                    Assert.True(double.IsFinite(point.Y));
                });
        }
    }

    private static GuardianSiteTemplate CreateTemplate(
        IReadOnlyList<GuardianPointOfInterest> points)
    {
        return new GuardianSiteTemplate(
            "Test",
            "Test",
            string.Empty,
            new GuardianMapPoint(0, 0),
            1,
            points,
            [],
            new Dictionary<string, GuardianMapPoint>());
    }

    private static void AssertPoint(
        GuardianProjectedPoint point,
        double x,
        double y)
    {
        AssertPoint(point.X, point.Y, x, y);
    }

    private static void AssertPoint(
        double actualX,
        double actualY,
        double expectedX,
        double expectedY)
    {
        Assert.Equal(expectedX, actualX, precision: 9);
        Assert.Equal(expectedY, actualY, precision: 9);
    }
}
