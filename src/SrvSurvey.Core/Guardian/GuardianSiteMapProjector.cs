namespace SrvSurvey.Core.Guardian;

public sealed class GuardianSiteMapProjector
{
    public GuardianSiteMapProjection Project(
        GuardianSiteTemplate template,
        GuardianSurveyData? survey = null,
        IReadOnlyList<GuardianObelisk>? activeObelisks = null,
        IReadOnlySet<char>? obeliskGroups = null,
        bool includeComponentMaterials = false,
        IReadOnlySet<string>? neededRamTahLogCodes = null)
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
                survey?.RelicHeadings,
                survey?.RelicTowerHeading ?? -1,
                IsRuins(template.SiteType),
                activeObelisks,
                neededRamTahLogCodes))
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
            maximumDistance,
            IsRuins(template.SiteType),
            NormalizeHeading(survey?.SiteHeading ?? -1),
            NormalizeHeading(survey?.RelicTowerHeading ?? -1));
    }

    private static GuardianProjectedPoint ProjectPoint(
        GuardianPointOfInterest point,
        IReadOnlyDictionary<string, GuardianPoiStatus>? statuses,
        IReadOnlyList<GuardianPointOfInterest>? rawPoints,
        IReadOnlyDictionary<string, GuardianComponentLoadout>? components,
        IReadOnlyDictionary<string, int>? relicHeadings,
        int relicTowerHeading,
        bool isRuins,
        IReadOnlyList<GuardianObelisk>? activeObelisks,
        IReadOnlySet<string>? neededRamTahLogCodes)
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
                            : point.Type == GuardianPoiType.EmptyPuddle
                                ? GuardianPoiStatus.Empty
                                : GuardianPoiStatus.Unknown;
        var relicHeading = -1;
        var hasIndividualRelicHeading = point.Type == GuardianPoiType.Relic
            && relicHeadings?.TryGetValue(point.Name, out relicHeading) == true
            && NormalizeHeading(relicHeading) >= 0;
        var projectedRelicHeading = hasIndividualRelicHeading
            ? NormalizeHeading(relicHeading)
            : point.Type == GuardianPoiType.Relic
                && isRuins
                && NormalizeHeading(relicTowerHeading) >= 0
                    ? NormalizeHeading(relicTowerHeading)
                    : -1;
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
            componentLoadout?.Items ?? [],
            projectedRelicHeading,
            hasIndividualRelicHeading,
            active is not null
                && !string.IsNullOrWhiteSpace(active.LogCode)
                && neededRamTahLogCodes?.Contains(active.LogCode) == true);
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

    private static bool IsRuins(string siteType)
    {
        return siteType.Equals("Alpha", StringComparison.OrdinalIgnoreCase)
            || siteType.Equals("Beta", StringComparison.OrdinalIgnoreCase)
            || siteType.Equals("Gamma", StringComparison.OrdinalIgnoreCase);
    }

    private static int NormalizeHeading(int heading)
    {
        return heading is >= 0 and <= 359 ? heading : -1;
    }
}

public sealed record GuardianSiteMapProjection(
    string SiteType,
    IReadOnlyList<GuardianProjectedPoint> Points,
    IReadOnlyList<GuardianProjectedGroup> Groups,
    double MaximumDistance,
    bool IsRuins = false,
    int SiteHeading = -1,
    int RelicTowerHeading = -1)
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
    IReadOnlyList<GuardianComponentMaterial> ComponentMaterials,
    int RelicHeading = -1,
    bool HasIndividualRelicHeading = false,
    bool IsRamTahNeededObelisk = false);

public sealed record GuardianProjectedGroup(
    string Name,
    double X,
    double Y,
    double Angle,
    double Distance);
