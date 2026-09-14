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
        GuardianSiteTemplate template = Template(
            new GuardianPointOfInterest("A01", GuardianPoiType.Obelisk, 180, 10, 0)
        );
        EliteStatus status = StatusAt(Bearing.North, 10, inSrv: true);
        var obelisk = new GuardianObelisk("A01", "H1", false, ["ca"]);

        GuardianSiteProximitySnapshot? result = new GuardianSiteProximityEvaluator().Evaluate(
            new GuardianSiteProximityEvaluateRequest
            {
                Status = status,
                SiteLocation = SiteLocation,
                SiteHeading = 0,
                Template = template,
                ActiveObelisks = [obelisk],
                ObeliskGroups = new HashSet<char> { 'A' },
            }
        );

        GuardianSiteProximitySnapshot proximity = Assert.IsType<GuardianSiteProximitySnapshot>(result);
        GuardianNearbyPoint nearby = Assert.IsType<GuardianNearbyPoint>(proximity.NearestPoint);
        Assert.Equal(0, nearby.Distance, precision: 5);
        Assert.Equal(0, proximity.MapX, precision: 5);
        Assert.Equal(-10, proximity.MapY, precision: 5);
        Assert.Same(obelisk, proximity.CurrentObelisk);
    }

    [Fact]
    public void AppliesSiteHeadingToObeliskPosition()
    {
        GuardianSiteTemplate template = Template(
            new GuardianPointOfInterest("A01", GuardianPoiType.Obelisk, 180, 10, 0)
        );
        EliteStatus status = StatusAt(Bearing.East, 10, inSrv: true);
        var obelisk = new GuardianObelisk("A01", "H1", false, ["ca"]);

        GuardianSiteProximitySnapshot? result = new GuardianSiteProximityEvaluator().Evaluate(
            new GuardianSiteProximityEvaluateRequest
            {
                Status = status,
                SiteLocation = SiteLocation,
                SiteHeading = 90,
                Template = template,
                ActiveObelisks = [obelisk],
            }
        );

        GuardianSiteProximitySnapshot proximity = Assert.IsType<GuardianSiteProximitySnapshot>(result);
        GuardianNearbyPoint nearby = Assert.IsType<GuardianNearbyPoint>(proximity.NearestPoint);
        Assert.Equal(0, nearby.Distance, precision: 5);
        Assert.Equal(0, proximity.MapX, precision: 5);
        Assert.Equal(-10, proximity.MapY, precision: 5);
        Assert.Same(obelisk, proximity.CurrentObelisk);
    }

    [Fact]
    public void AppliesMapMarkerOffsetToNearbyPointTargeting()
    {
        GuardianSiteTemplate template = Template(new GuardianPointOfInterest("p1", GuardianPoiType.Orb, 180, 10, 0));

        GuardianSiteProximitySnapshot? result = new GuardianSiteProximityEvaluator().Evaluate(
            new GuardianSiteProximityEvaluateRequest
            {
                Status = StatusAt(Bearing.North, 0, inSrv: true),
                SiteLocation = SiteLocation,
                SiteHeading = 90,
                Template = template,
                MarkerOffset = new GuardianMapPoint(0, 10),
            }
        );

        GuardianNearbyPoint nearby = Assert.IsType<GuardianNearbyPoint>(result?.NearestPoint);
        Assert.Equal(0, nearby.Distance, precision: 5);
        Assert.Equal(0, nearby.X, precision: 5);
        Assert.Equal(0, nearby.Y, precision: 5);
    }

    [Fact]
    public void ClosestSelectablePointMustBeObeliskAndWithinThreshold()
    {
        GuardianSiteTemplate template = Template(
            new GuardianPointOfInterest("A01", GuardianPoiType.Obelisk, 180, 20, 0),
            new GuardianPointOfInterest("p1", GuardianPoiType.Orb, 180, 10, 0)
        );
        EliteStatus status = StatusAt(Bearing.North, 10, inSrv: true);
        var obelisk = new GuardianObelisk("A01", "H1", false, ["ca"]);

        GuardianSiteProximitySnapshot? nearArtifact = new GuardianSiteProximityEvaluator().Evaluate(
            new GuardianSiteProximityEvaluateRequest
            {
                Status = status,
                SiteLocation = SiteLocation,
                SiteHeading = 0,
                Template = template,
                ActiveObelisks = [obelisk],
            }
        );
        GuardianSiteProximitySnapshot? outsideObeliskRange = new GuardianSiteProximityEvaluator().Evaluate(
            new GuardianSiteProximityEvaluateRequest
            {
                Status = StatusAt(Bearing.South, 10, inSrv: true),
                SiteLocation = SiteLocation,
                SiteHeading = 0,
                Template = Template(new GuardianPointOfInterest("A01", GuardianPoiType.Obelisk, 180, 20, 0)),
                ActiveObelisks = [obelisk],
            }
        );

        Assert.Equal("p1", nearArtifact?.NearestPoint?.Point.Name);
        Assert.Null(nearArtifact?.CurrentObelisk);
        Assert.True(outsideObeliskRange?.NearestPoint?.Distance > 25);
        Assert.Null(outsideObeliskRange?.CurrentObelisk);
    }

    [Fact]
    public void DoesNotExposeMappedPointsBeyondLegacySeventyFiveMeterRange()
    {
        GuardianSiteProximitySnapshot? result = new GuardianSiteProximityEvaluator().Evaluate(
            new GuardianSiteProximityEvaluateRequest
            {
                Status = StatusAt(Bearing.South, 100, inSrv: true),
                SiteLocation = SiteLocation,
                SiteHeading = 0,
                Template = Template(new GuardianPointOfInterest("p1", GuardianPoiType.Orb, 180, 10, 0)),
            }
        );

        Assert.NotNull(result);
        Assert.Null(result.NearestPoint);
        Assert.Null(result.CurrentObelisk);
    }

    [Fact]
    public void IgnoresInactiveFilteredAndVehicleIncompatibleObelisks()
    {
        GuardianSiteTemplate template = Template(
            new GuardianPointOfInterest("A01", GuardianPoiType.Obelisk, 180, 10, 0),
            new GuardianPointOfInterest("B01", GuardianPoiType.Obelisk, 180, 11, 0)
        );
        var active = new GuardianObelisk("B01", "H1", false, ["ca"]);
        var evaluator = new GuardianSiteProximityEvaluator();

        GuardianSiteProximitySnapshot? filtered = evaluator.Evaluate(
            new GuardianSiteProximityEvaluateRequest
            {
                Status = StatusAt(Bearing.North, 11, inSrv: true),
                SiteLocation = SiteLocation,
                SiteHeading = 0,
                Template = template,
                ActiveObelisks = [active],
                ObeliskGroups = new HashSet<char> { 'A' },
            }
        );
        GuardianSiteProximitySnapshot? inShip = evaluator.Evaluate(
            new GuardianSiteProximityEvaluateRequest
            {
                Status = StatusAt(Bearing.North, 11, inSrv: false),
                SiteLocation = SiteLocation,
                SiteHeading = 0,
                Template = template,
                ActiveObelisks = [active],
            }
        );

        Assert.Null(filtered?.NearestPoint);
        Assert.Null(inShip?.NearestPoint);
    }

    [Fact]
    public void GeneticSamplerSelectsOnlyRelicTowers()
    {
        GuardianSiteTemplate template = Template(
            new GuardianPointOfInterest("A01", GuardianPoiType.Obelisk, 180, 10, 0),
            new GuardianPointOfInterest("t1", GuardianPoiType.Relic, 180, 12, 0)
        );
        EliteStatus status = StatusAt(Bearing.North, 10, inSrv: true) with
        {
            SelectedWeapon = "$humanoid_companalyser_name;",
        };

        GuardianSiteProximitySnapshot? result = new GuardianSiteProximityEvaluator().Evaluate(
            new GuardianSiteProximityEvaluateRequest
            {
                Status = status,
                SiteLocation = SiteLocation,
                SiteHeading = 0,
                Template = template,
                ActiveObelisks = [new GuardianObelisk("A01", "H1", false, ["ca"])],
            }
        );

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
            [new GuardianPointOfInterest("d1", GuardianPoiType.DestructiblePanel, 180, 10, 0)],
            new Dictionary<string, GuardianMapPoint>()
        );
        EliteStatus status = StatusAt(Bearing.North, 10, inSrv: true);
        var evaluator = new GuardianSiteProximityEvaluator();

        GuardianSiteProximitySnapshot? standard = evaluator.Evaluate(
            new GuardianSiteProximityEvaluateRequest
            {
                Status = status,
                SiteLocation = SiteLocation,
                SiteHeading = 0,
                Template = template,
            }
        );
        GuardianSiteProximitySnapshot? componentMode = evaluator.Evaluate(
            new GuardianSiteProximityEvaluateRequest
            {
                Status = status,
                SiteLocation = SiteLocation,
                SiteHeading = 0,
                Template = template,
                IncludeComponentMaterials = true,
            }
        );

        Assert.Null(standard?.NearestPoint);
        GuardianNearbyPoint nearby = Assert.IsType<GuardianNearbyPoint>(componentMode?.NearestPoint);
        Assert.Equal("d1", nearby.Point.Name);
        Assert.Equal(0, nearby.Distance, precision: 5);
    }

    [Fact]
    public void ReturnsUnavailableWithoutSurfaceGeometryOrKnownHeading()
    {
        var evaluator = new GuardianSiteProximityEvaluator();
        EliteStatus status = StatusAt(Bearing.North, 10, inSrv: true);
        GuardianSiteTemplate template = Template(
            new GuardianPointOfInterest("A01", GuardianPoiType.Obelisk, 180, 10, 0)
        );

        Assert.Null(
            evaluator.Evaluate(
                new GuardianSiteProximityEvaluateRequest
                {
                    Status = status with { Flags = StatusFlags.InSrv },
                    SiteLocation = SiteLocation,
                    SiteHeading = 0,
                    Template = template,
                }
            )
        );
        Assert.Null(
            evaluator.Evaluate(
                new GuardianSiteProximityEvaluateRequest
                {
                    Status = status with { PlanetRadius = 0 },
                    SiteLocation = SiteLocation,
                    SiteHeading = 0,
                    Template = template,
                }
            )
        );
        Assert.Null(
            evaluator.Evaluate(
                new GuardianSiteProximityEvaluateRequest
                {
                    Status = status,
                    SiteLocation = SiteLocation,
                    SiteHeading = -1,
                    Template = template,
                }
            )
        );
    }

    private static GuardianSiteTemplate Template(params GuardianPointOfInterest[] points)
    {
        return new GuardianSiteTemplate(
            "Test",
            "Test",
            string.Empty,
            new GuardianMapPoint(0, 0),
            1,
            points,
            [],
            new Dictionary<string, GuardianMapPoint>()
        );
    }

    private static EliteStatus StatusAt(Bearing bearing, double distance, bool inSrv)
    {
        double angularDistance = distance / Radius;
        double latitude = bearing switch
        {
            Bearing.North => angularDistance * 180 / Math.PI,
            Bearing.South => -angularDistance * 180 / Math.PI,
            _ => 0,
        };
        double longitude = bearing switch
        {
            Bearing.East => angularDistance * 180 / Math.PI,
            Bearing.West => -angularDistance * 180 / Math.PI,
            _ => 0,
        };
        return new EliteStatus
        {
            Flags = StatusFlags.HasLatLong | (inSrv ? StatusFlags.InSrv : StatusFlags.InMainShip),
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
