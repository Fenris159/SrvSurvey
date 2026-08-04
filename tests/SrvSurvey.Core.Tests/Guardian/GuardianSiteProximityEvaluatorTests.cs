using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Guardian;

public sealed class GuardianSiteProximityEvaluatorTests
{
    private const double Radius = 1_000_000;
    private static readonly GuardianSurfaceLocation SiteLocation = new(0, 0);

    [Fact]
    public void SelectsActiveObeliskWithinLegacyTwentyFiveMeterThreshold()
    {
        var template = Template(
            new GuardianPointOfInterest(
                "A01",
                GuardianPoiType.Obelisk,
                180,
                10,
                0));
        var status = StatusAt(Bearing.North, 10, inSrv: true);
        var obelisk = new GuardianObelisk("A01", "H1", false, ["ca"]);

        var result = new GuardianSiteProximityEvaluator().Evaluate(
            status,
            SiteLocation,
            0,
            template,
            activeObelisks: [obelisk],
            obeliskGroups: new HashSet<char> { 'A' });

        var proximity = Assert.IsType<GuardianSiteProximitySnapshot>(result);
        var nearby = Assert.IsType<GuardianNearbyPoint>(proximity.NearestPoint);
        Assert.Equal(0, nearby.Distance, precision: 5);
        Assert.Equal(0, proximity.MapX, precision: 5);
        Assert.Equal(-10, proximity.MapY, precision: 5);
        Assert.Same(obelisk, proximity.CurrentObelisk);
    }

    [Fact]
    public void AppliesSiteHeadingToObeliskPosition()
    {
        var template = Template(
            new GuardianPointOfInterest(
                "A01",
                GuardianPoiType.Obelisk,
                180,
                10,
                0));
        var status = StatusAt(Bearing.East, 10, inSrv: true);
        var obelisk = new GuardianObelisk("A01", "H1", false, ["ca"]);

        var result = new GuardianSiteProximityEvaluator().Evaluate(
            status,
            SiteLocation,
            90,
            template,
            activeObelisks: [obelisk]);

        var proximity = Assert.IsType<GuardianSiteProximitySnapshot>(result);
        var nearby = Assert.IsType<GuardianNearbyPoint>(proximity.NearestPoint);
        Assert.Equal(0, nearby.Distance, precision: 5);
        Assert.Equal(0, proximity.MapX, precision: 5);
        Assert.Equal(-10, proximity.MapY, precision: 5);
        Assert.Same(obelisk, proximity.CurrentObelisk);
    }

    [Fact]
    public void ClosestSelectablePointMustBeObeliskAndWithinThreshold()
    {
        var template = Template(
            new GuardianPointOfInterest(
                "A01",
                GuardianPoiType.Obelisk,
                180,
                20,
                0),
            new GuardianPointOfInterest(
                "p1",
                GuardianPoiType.Orb,
                180,
                10,
                0));
        var status = StatusAt(Bearing.North, 10, inSrv: true);
        var obelisk = new GuardianObelisk("A01", "H1", false, ["ca"]);

        var nearArtifact = new GuardianSiteProximityEvaluator().Evaluate(
            status,
            SiteLocation,
            0,
            template,
            activeObelisks: [obelisk]);
        var outsideObeliskRange = new GuardianSiteProximityEvaluator().Evaluate(
            StatusAt(Bearing.South, 10, inSrv: true),
            SiteLocation,
            0,
            Template(new GuardianPointOfInterest(
                "A01",
                GuardianPoiType.Obelisk,
                180,
                20,
                0)),
            activeObelisks: [obelisk]);

        Assert.Equal("p1", nearArtifact?.NearestPoint?.Point.Name);
        Assert.Null(nearArtifact?.CurrentObelisk);
        Assert.True(outsideObeliskRange?.NearestPoint?.Distance > 25);
        Assert.Null(outsideObeliskRange?.CurrentObelisk);
    }

    [Fact]
    public void DoesNotExposeMappedPointsBeyondLegacySeventyFiveMeterRange()
    {
        var result = new GuardianSiteProximityEvaluator().Evaluate(
            StatusAt(Bearing.South, 100, inSrv: true),
            SiteLocation,
            0,
            Template(new GuardianPointOfInterest(
                "p1",
                GuardianPoiType.Orb,
                180,
                10,
                0)));

        Assert.NotNull(result);
        Assert.Null(result.NearestPoint);
        Assert.Null(result.CurrentObelisk);
    }

    [Fact]
    public void IgnoresInactiveFilteredAndVehicleIncompatibleObelisks()
    {
        var template = Template(
            new GuardianPointOfInterest(
                "A01",
                GuardianPoiType.Obelisk,
                180,
                10,
                0),
            new GuardianPointOfInterest(
                "B01",
                GuardianPoiType.Obelisk,
                180,
                11,
                0));
        var active = new GuardianObelisk("B01", "H1", false, ["ca"]);
        var evaluator = new GuardianSiteProximityEvaluator();

        var filtered = evaluator.Evaluate(
            StatusAt(Bearing.North, 11, inSrv: true),
            SiteLocation,
            0,
            template,
            activeObelisks: [active],
            obeliskGroups: new HashSet<char> { 'A' });
        var inShip = evaluator.Evaluate(
            StatusAt(Bearing.North, 11, inSrv: false),
            SiteLocation,
            0,
            template,
            activeObelisks: [active]);

        Assert.Null(filtered?.NearestPoint);
        Assert.Null(inShip?.NearestPoint);
    }

    [Fact]
    public void GeneticSamplerSelectsOnlyRelicTowers()
    {
        var template = Template(
            new GuardianPointOfInterest(
                "A01",
                GuardianPoiType.Obelisk,
                180,
                10,
                0),
            new GuardianPointOfInterest(
                "t1",
                GuardianPoiType.Relic,
                180,
                12,
                0));
        var status = StatusAt(Bearing.North, 10, inSrv: true) with
        {
            SelectedWeapon = "$humanoid_companalyser_name;",
        };

        var result = new GuardianSiteProximityEvaluator().Evaluate(
            status,
            SiteLocation,
            0,
            template,
            activeObelisks: [new GuardianObelisk("A01", "H1", false, ["ca"])]);

        Assert.Equal("t1", result?.NearestPoint?.Point.Name);
        Assert.Null(result?.CurrentObelisk);
    }

    [Fact]
    public void ComponentModeMakesDestructiblePanelsSelectable()
    {
        var template = new GuardianSiteTemplate(
            "Test",
            "Test",
            string.Empty,
            new GuardianMapPoint(0, 0),
            1,
            [],
            [
                new GuardianPointOfInterest(
                    "d1",
                    GuardianPoiType.DestructiblePanel,
                    180,
                    10,
                    0),
            ],
            new Dictionary<string, GuardianMapPoint>());
        var status = StatusAt(Bearing.North, 10, inSrv: true);
        var evaluator = new GuardianSiteProximityEvaluator();

        var standard = evaluator.Evaluate(
            status,
            SiteLocation,
            0,
            template);
        var componentMode = evaluator.Evaluate(
            status,
            SiteLocation,
            0,
            template,
            includeComponentMaterials: true);

        Assert.Null(standard?.NearestPoint);
        var nearby = Assert.IsType<GuardianNearbyPoint>(
            componentMode?.NearestPoint);
        Assert.Equal("d1", nearby.Point.Name);
        Assert.Equal(0, nearby.Distance, precision: 5);
    }

    [Fact]
    public void ReturnsUnavailableWithoutSurfaceGeometryOrKnownHeading()
    {
        var evaluator = new GuardianSiteProximityEvaluator();
        var status = StatusAt(Bearing.North, 10, inSrv: true);
        var template = Template(new GuardianPointOfInterest(
            "A01",
            GuardianPoiType.Obelisk,
            180,
            10,
            0));

        Assert.Null(evaluator.Evaluate(
            status with { Flags = StatusFlags.InSrv },
            SiteLocation,
            0,
            template));
        Assert.Null(evaluator.Evaluate(
            status with { PlanetRadius = 0 },
            SiteLocation,
            0,
            template));
        Assert.Null(evaluator.Evaluate(
            status,
            SiteLocation,
            -1,
            template));
    }

    private static GuardianSiteTemplate Template(
        params GuardianPointOfInterest[] points)
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

    private static EliteStatus StatusAt(
        Bearing bearing,
        double distance,
        bool inSrv)
    {
        var angularDistance = distance / Radius;
        var latitude = bearing switch
        {
            Bearing.North => angularDistance * 180 / Math.PI,
            Bearing.South => -angularDistance * 180 / Math.PI,
            _ => 0,
        };
        var longitude = bearing switch
        {
            Bearing.East => angularDistance * 180 / Math.PI,
            Bearing.West => -angularDistance * 180 / Math.PI,
            _ => 0,
        };
        return new EliteStatus
        {
            Flags = StatusFlags.HasLatLong
                | (inSrv ? StatusFlags.InSrv : StatusFlags.InMainShip),
            Latitude = latitude,
            Longitude = longitude,
            PlanetRadius = (decimal)Radius,
        };
    }

    private enum Bearing
    {
        North,
        East,
        South,
        West,
    }
}
