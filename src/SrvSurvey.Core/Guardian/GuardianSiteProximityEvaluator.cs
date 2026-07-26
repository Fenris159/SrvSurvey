using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianSiteProximityEvaluator
{
    public const double CurrentObeliskDistance = 25;
    private const string GeneticSamplerWeapon = "$humanoid_companalyser_name;";

    public GuardianSiteProximitySnapshot? Evaluate(
        EliteStatus status,
        GuardianSurfaceLocation siteLocation,
        int siteHeading,
        GuardianSiteTemplate template,
        GuardianSurveyData? survey = null,
        IReadOnlyList<GuardianObelisk>? activeObelisks = null,
        IReadOnlySet<char>? obeliskGroups = null,
        bool includeComponentMaterials = false)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(template);
        var radius = (double)status.PlanetRadius;
        if (!status.HasLatitudeLongitude
            || !double.IsFinite(radius)
            || radius <= 0
            || siteHeading is < 0 or > 359
            || !IsValidLocation(siteLocation)
            || !IsValidLocation(status.Latitude, status.Longitude))
        {
            return null;
        }

        var commander = new SurfaceCoordinate(status.Latitude, status.Longitude);
        var site = new SurfaceCoordinate(
            siteLocation.Latitude,
            siteLocation.Longitude);
        var siteDistance = SurfaceNavigation.GetDistance(commander, site, radius);
        var siteBearing = SurfaceNavigation.GetBearing(commander, site);
        var siteBearingRadians = DegreesToRadians(siteBearing);
        var commanderX = Math.Sin(siteBearingRadians) * siteDistance;
        var commanderY = -Math.Cos(siteBearingRadians) * siteDistance;
        var mapBearing = SurfaceNavigation.GetBearing(site, commander);
        var mapAngleRadians = DegreesToRadians(mapBearing - siteHeading);
        var mapX = Math.Sin(mapAngleRadians) * siteDistance;
        var mapY = -Math.Cos(mapAngleRadians) * siteDistance;
        var activeByName = (activeObelisks ?? [])
            .GroupBy(obelisk => obelisk.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        GuardianNearbyPoint? nearest = null;
        foreach (var point in template.PointsOfInterest
                     .Concat(includeComponentMaterials
                         ? template.DestructiblePanels
                         : [])
                     .Concat(survey?.RawPointsOfInterest ?? []))
        {
            if (!IsSelectable(
                    point,
                    status,
                    activeByName,
                    obeliskGroups))
            {
                continue;
            }

            var pointAngle = 180 - siteHeading - point.Angle;
            var pointRadians = DegreesToRadians(pointAngle);
            var pointX = Math.Sin(pointRadians) * point.Distance;
            var pointY = Math.Cos(pointRadians) * point.Distance;
            var deltaX = pointX - commanderX;
            var deltaY = pointY - commanderY;
            var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            if (nearest is not null && distance >= nearest.Distance)
            {
                continue;
            }

            activeByName.TryGetValue(point.Name, out var activeObelisk);
            nearest = new GuardianNearbyPoint(
                point,
                distance,
                pointX,
                pointY,
                activeObelisk);
        }

        var currentObelisk = nearest is
        {
            Point.Type: GuardianPoiType.Obelisk,
            ActiveObelisk: not null,
            Distance: < CurrentObeliskDistance,
        }
            ? nearest.ActiveObelisk
            : null;
        return new GuardianSiteProximitySnapshot(
            siteDistance,
            commanderX,
            commanderY,
            mapX,
            mapY,
            nearest,
            currentObelisk);
    }

    private static bool IsSelectable(
        GuardianPointOfInterest point,
        EliteStatus status,
        IReadOnlyDictionary<string, GuardianObelisk> activeByName,
        IReadOnlySet<char>? obeliskGroups)
    {
        var isObelisk = point.Type is GuardianPoiType.Obelisk
            or GuardianPoiType.BrokenObelisk;
        if (isObelisk
            && obeliskGroups is { Count: > 0 }
            && !string.IsNullOrEmpty(point.Name)
            && !obeliskGroups.Contains(point.Name[0]))
        {
            return false;
        }

        if (point.Type == GuardianPoiType.BrokenObelisk)
        {
            return false;
        }

        if (string.Equals(
                status.SelectedWeapon,
                GeneticSamplerWeapon,
                StringComparison.Ordinal))
        {
            return point.Type == GuardianPoiType.Relic;
        }

        var isMobileOnSurface = status.InSrv || status.OnFoot;
        if (point.Type is GuardianPoiType.Obelisk or GuardianPoiType.Relic
            && !isMobileOnSurface)
        {
            return false;
        }

        return point.Type != GuardianPoiType.Obelisk
            || activeByName.ContainsKey(point.Name);
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

public sealed record GuardianSiteProximitySnapshot(
    double DistanceFromSite,
    double CommanderX,
    double CommanderY,
    double MapX,
    double MapY,
    GuardianNearbyPoint? NearestPoint,
    GuardianObelisk? CurrentObelisk);

public sealed record GuardianNearbyPoint(
    GuardianPointOfInterest Point,
    double Distance,
    double X,
    double Y,
    GuardianObelisk? ActiveObelisk);
