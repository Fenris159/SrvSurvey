using System.Diagnostics.CodeAnalysis;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianSiteProximityEvaluator
{
    public const double CurrentObeliskDistance = 25;
    public const double NearbyPointDistance = 75;
    private const string GeneticSamplerWeapon = "$humanoid_companalyser_name;";

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The evaluator is consumed through an instance service contract."
    )]
    public GuardianSiteProximitySnapshot? Evaluate(GuardianSiteProximityEvaluateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Status);
        ArgumentNullException.ThrowIfNull(request.Template);
        EliteStatus status = request.Status;
        GuardianSurfaceLocation siteLocation = request.SiteLocation;
        int siteHeading = request.SiteHeading;
        GuardianSiteTemplate template = request.Template;
        GuardianSurveyData? survey = request.Survey;
        IReadOnlyList<GuardianObelisk>? activeObelisks = request.ActiveObelisks;
        IReadOnlySet<char>? obeliskGroups = request.ObeliskGroups;
        bool includeComponentMaterials = request.IncludeComponentMaterials;
        double radius = (double)status.PlanetRadius;
        if (
            !status.HasLatitudeLongitude
            || !double.IsFinite(radius)
            || radius <= 0
            || siteHeading is < 0 or > 359
            || !IsValidLocation(siteLocation)
            || !IsValidLocation(status.Latitude, status.Longitude)
        )
        {
            return null;
        }

        GuardianMapPoint surfaceMarkerOffset = GuardianMapMarkerOffsetCalculator.ToSurfaceCoordinates(
            request.MarkerOffset,
            siteHeading
        );

        var commander = new SurfaceCoordinate(status.Latitude, status.Longitude);
        var site = new SurfaceCoordinate(siteLocation.Latitude, siteLocation.Longitude);
        double siteDistance = SurfaceNavigation.GetDistance(commander, site, radius);
        double siteBearing = SurfaceNavigation.GetBearing(commander, site);
        double siteBearingRadians = DegreesToRadians(siteBearing);
        double commanderX = Math.Sin(siteBearingRadians) * siteDistance;
        double commanderY = -Math.Cos(siteBearingRadians) * siteDistance;
        double mapBearing = SurfaceNavigation.GetBearing(site, commander);
        double mapAngleRadians = DegreesToRadians(mapBearing - siteHeading);
        double mapX = Math.Sin(mapAngleRadians) * siteDistance;
        double mapY = -Math.Cos(mapAngleRadians) * siteDistance;
        var activeByName = (activeObelisks ?? [])
            .GroupBy(obelisk => obelisk.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        GuardianNearbyPoint? nearest = null;
        foreach (
            GuardianPointOfInterest? point in template
                .PointsOfInterest.Concat(includeComponentMaterials ? template.DestructiblePanels : [])
                .Concat(survey?.RawPointsOfInterest ?? [])
        )
        {
            if (!IsSelectable(point, status, activeByName, obeliskGroups))
            {
                continue;
            }

            double pointAngle = 180 - siteHeading - point.Angle;
            double pointRadians = DegreesToRadians(pointAngle);
            double pointX = (Math.Sin(pointRadians) * point.Distance) + surfaceMarkerOffset.X;
            double pointY = (Math.Cos(pointRadians) * point.Distance) + surfaceMarkerOffset.Y;
            double deltaX = pointX - commanderX;
            double deltaY = pointY - commanderY;
            double distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            if (nearest is not null && distance >= nearest.Distance)
            {
                continue;
            }

            activeByName.TryGetValue(point.Name, out GuardianObelisk? activeObelisk);
            nearest = new GuardianNearbyPoint(point, distance, pointX, pointY, activeObelisk);
        }

        if (nearest?.Distance > NearbyPointDistance)
        {
            nearest = null;
        }

        GuardianObelisk? currentObelisk = nearest
            is { Point.Type: GuardianPoiType.Obelisk, ActiveObelisk: not null, Distance: < CurrentObeliskDistance }
            ? nearest.ActiveObelisk
            : null;
        return new GuardianSiteProximitySnapshot(
            siteDistance,
            commanderX,
            commanderY,
            mapX,
            mapY,
            nearest,
            currentObelisk
        );
    }

    private static bool IsSelectable(
        GuardianPointOfInterest point,
        EliteStatus status,
        Dictionary<string, GuardianObelisk> activeByName,
        IReadOnlySet<char>? obeliskGroups
    )
    {
        bool isObelisk = point.Type is GuardianPoiType.Obelisk or GuardianPoiType.BrokenObelisk;
        if (
            isObelisk
            && obeliskGroups is { Count: > 0 }
            && !string.IsNullOrEmpty(point.Name)
            && !obeliskGroups.Contains(point.Name[0])
        )
        {
            return false;
        }

        if (point.Type == GuardianPoiType.BrokenObelisk)
        {
            return false;
        }

        if (string.Equals(status.SelectedWeapon, GeneticSamplerWeapon, StringComparison.Ordinal))
        {
            return point.Type == GuardianPoiType.Relic;
        }

        bool isMobileOnSurface = status.InSrv || status.OnFoot;
        if (point.Type is GuardianPoiType.Obelisk or GuardianPoiType.Relic && !isMobileOnSurface)
        {
            return false;
        }

        return point.Type != GuardianPoiType.Obelisk || activeByName.ContainsKey(point.Name);
    }

    private static bool IsValidLocation(GuardianSurfaceLocation location)
    {
        return IsValidLocation(location.Latitude, location.Longitude);
    }

    private static bool IsValidLocation(double latitude, double longitude)
    {
        return double.IsFinite(latitude)
            && latitude is >= -90 and <= 90
            && double.IsFinite(longitude)
            && longitude is >= -180 and <= 180;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180;
    }
}

public sealed class GuardianSiteProximityEvaluateRequest
{
    public required EliteStatus Status { get; init; }

    public required GuardianSurfaceLocation SiteLocation { get; init; }

    public int SiteHeading { get; init; }

    public required GuardianSiteTemplate Template { get; init; }

    public GuardianSurveyData? Survey { get; init; }

    public IReadOnlyList<GuardianObelisk>? ActiveObelisks { get; init; }

    public IReadOnlySet<char>? ObeliskGroups { get; init; }

    public bool IncludeComponentMaterials { get; init; }

    public GuardianMapPoint MarkerOffset { get; init; }
}

public sealed record GuardianSiteProximitySnapshot(
    double DistanceFromSite,
    double CommanderX,
    double CommanderY,
    double MapX,
    double MapY,
    GuardianNearbyPoint? NearestPoint,
    GuardianObelisk? CurrentObelisk
);

public sealed record GuardianNearbyPoint(
    GuardianPointOfInterest Point,
    double Distance,
    double X,
    double Y,
    GuardianObelisk? ActiveObelisk
);
