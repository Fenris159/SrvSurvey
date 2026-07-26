namespace SrvSurvey.Core.Guardian;

public sealed class GuardianSiteMapProjector
{
    public GuardianSiteMapProjection Project(
        GuardianSiteTemplate template,
        GuardianSurveyData? survey = null,
        IReadOnlyList<GuardianObelisk>? activeObelisks = null,
        IReadOnlySet<char>? obeliskGroups = null,
        bool includeComponentMaterials = false)
    {
        ArgumentNullException.ThrowIfNull(template);
        var points = template.PointsOfInterest
            .Concat(includeComponentMaterials
                ? template.DestructiblePanels
                : [])
            .Concat(survey?.RawPointsOfInterest ?? [])
            .Where(point => IsVisible(point, obeliskGroups))
            .Select(point => ProjectPoint(
                point,
                survey?.PoiStatuses,
                survey?.RawPointsOfInterest,
                survey?.ComponentMaterials,
                activeObelisks))
            .ToArray();
        var groups = template.ObeliskGroupNameLocations
            .Where(group => obeliskGroups?.Contains(group.Key[0]) == true)
            .Select(group => ProjectGroup(group.Key, group.Value))
            .ToArray();
        var maximumDistance = points
            .Select(point => point.Distance)
            .Concat(groups.Select(group => group.Distance))
            .DefaultIfEmpty(1)
            .Max();
        if (!double.IsFinite(maximumDistance) || maximumDistance <= 0)
        {
            maximumDistance = 1;
        }

        return new GuardianSiteMapProjection(
            template.SiteType,
            points,
            groups,
            maximumDistance);
    }

    private static GuardianProjectedPoint ProjectPoint(
        GuardianPointOfInterest point,
        IReadOnlyDictionary<string, GuardianPoiStatus>? statuses,
        IReadOnlyList<GuardianPointOfInterest>? rawPoints,
        IReadOnlyDictionary<string, GuardianComponentLoadout>? components,
        IReadOnlyList<GuardianObelisk>? activeObelisks)
    {
        var active = activeObelisks?.FirstOrDefault(obelisk => string.Equals(
            obelisk.Name,
            point.Name,
            StringComparison.OrdinalIgnoreCase));
        GuardianComponentLoadout? componentLoadout = null;
        components?.TryGetValue(point.Name, out componentLoadout);
        var status = statuses?.TryGetValue(point.Name, out var explicitStatus)
            == true
                ? explicitStatus
                : rawPoints?.Any(raw => ReferenceEquals(raw, point)
                    || string.Equals(
                        raw.Name,
                        point.Name,
                        StringComparison.Ordinal)) == true
                    ? GuardianPoiStatus.Present
                    : point.Type == GuardianPoiType.DestructiblePanel
                        && componentLoadout is not null
                        && componentLoadout.GetItem(0)
                            != GuardianComponentMaterial.Unknown
                            ? GuardianPoiStatus.Present
                            : GuardianPoiStatus.Unknown;
        var location = ProjectPolar(point.Angle, point.Distance);
        return new GuardianProjectedPoint(
            point.Name,
            point.Type,
            location.X,
            location.Y,
            point.Angle,
            point.Distance,
            point.Rotation,
            status,
            active is not null,
            active?.Scanned == true,
            active?.LogCode ?? string.Empty,
            componentLoadout?.Items ?? []);
    }

    private static GuardianProjectedGroup ProjectGroup(
        string name,
        GuardianMapPoint point)
    {
        var location = ProjectPolar(point.X, point.Y);
        return new GuardianProjectedGroup(
            name,
            location.X,
            location.Y,
            point.X,
            point.Y);
    }

    private static GuardianMapPoint ProjectPolar(double angle, double distance)
    {
        var radians = angle * Math.PI / 180;
        return new GuardianMapPoint(
            -Math.Sin(radians) * distance,
            Math.Cos(radians) * distance);
    }

    private static bool IsVisible(
        GuardianPointOfInterest point,
        IReadOnlySet<char>? obeliskGroups)
    {
        if (obeliskGroups is not { Count: > 0 }
            || point.Type is not GuardianPoiType.Obelisk
                and not GuardianPoiType.BrokenObelisk
            || string.IsNullOrEmpty(point.Name))
        {
            return true;
        }

        return obeliskGroups.Contains(point.Name[0]);
    }
}

public sealed record GuardianSiteMapProjection(
    string SiteType,
    IReadOnlyList<GuardianProjectedPoint> Points,
    IReadOnlyList<GuardianProjectedGroup> Groups,
    double MaximumDistance)
{
    public int SurveyablePointCount => Points.Count(point =>
        point.Type is not GuardianPoiType.Obelisk
            and not GuardianPoiType.BrokenObelisk
            and not GuardianPoiType.DestructiblePanel);

    public int ConfirmedPointCount => Points.Count(point =>
        point.Type is not GuardianPoiType.Obelisk
            and not GuardianPoiType.BrokenObelisk
            and not GuardianPoiType.DestructiblePanel
            && point.Status != GuardianPoiStatus.Unknown);
}

public sealed record GuardianProjectedPoint(
    string Name,
    GuardianPoiType Type,
    double X,
    double Y,
    double Angle,
    double Distance,
    double Rotation,
    GuardianPoiStatus Status,
    bool IsActiveObelisk,
    bool IsScannedObelisk,
    string LogCode,
    IReadOnlyList<GuardianComponentMaterial> ComponentMaterials);

public sealed record GuardianProjectedGroup(
    string Name,
    double X,
    double Y,
    double Angle,
    double Distance);
