using System.Diagnostics.CodeAnalysis;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianSiteMapProjector
{
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The projector is consumed through an instance service contract.")]
    [SuppressMessage(
        "Maintainability",
        "S2325:Make methods and properties static",
        Justification = "The projector is consumed through an instance service contract.")]
    public GuardianSiteMapProjection Project(
        GuardianSiteTemplate template,
        GuardianSurveyData? survey = null,
        IReadOnlyList<GuardianObelisk>? activeObelisks = null,
        IReadOnlySet<char>? obeliskGroups = null,
        bool includeComponentMaterials = false,
        IReadOnlySet<string>? neededRamTahLogCodes = null,
        GuardianMapPoint markerOffset = default)
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
                survey,
                IsRuins(template.SiteType),
                activeObelisks,
                neededRamTahLogCodes,
                markerOffset))
            .ToArray();
        var groups = template.ObeliskGroupNameLocations
            .Where(group => obeliskGroups?.Contains(group.Key[0]) == true)
            .Select(group => ProjectGroup(
                group.Key,
                group.Value,
                markerOffset))
            .ToArray();
        var maximumDistance = points
            .Select(point => Math.Sqrt(
                (point.X * point.X) + (point.Y * point.Y)))
            .Concat(groups.Select(group => Math.Sqrt(
                (group.X * group.X) + (group.Y * group.Y))))
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
            NormalizeHeading(survey?.RelicTowerHeading ?? -1),
            template.BackgroundImage,
            template.ImageOffset,
            template.ScaleFactor,
            markerOffset);
    }

    private static GuardianProjectedPoint ProjectPoint(
        GuardianPointOfInterest point,
        GuardianSurveyData? survey,
        bool isRuins,
        IReadOnlyList<GuardianObelisk>? activeObelisks,
        IReadOnlySet<string>? neededRamTahLogCodes,
        GuardianMapPoint markerOffset)
    {
        var active = activeObelisks?.FirstOrDefault(obelisk => string.Equals(
            obelisk.Name,
            point.Name,
            StringComparison.OrdinalIgnoreCase));
        GuardianComponentLoadout? componentLoadout = null;
        survey?.ComponentMaterials.TryGetValue(point.Name, out componentLoadout);
        var status = ResolveStatus(point, survey, componentLoadout);
        var (projectedRelicHeading, hasIndividualRelicHeading) =
            ResolveRelicHeading(point, survey, isRuins);
        var location = ProjectPolar(point.Angle, point.Distance);
        return new GuardianProjectedPoint(
            point.Name,
            point.Type,
            location.X + markerOffset.X,
            location.Y + markerOffset.Y,
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

    private static GuardianPoiStatus ResolveStatus(
        GuardianPointOfInterest point,
        GuardianSurveyData? survey,
        GuardianComponentLoadout? componentLoadout)
    {
        if (survey?.PoiStatuses.TryGetValue(point.Name, out var explicitStatus)
            == true)
        {
            return explicitStatus;
        }

        if (survey?.RawPointsOfInterest?.Any(raw => ReferenceEquals(raw, point)
            || string.Equals(
                raw.Name,
                point.Name,
                StringComparison.Ordinal)) == true)
        {
            return GuardianPoiStatus.Present;
        }

        if (point.Type == GuardianPoiType.DestructiblePanel
            && componentLoadout is not null
            && componentLoadout.GetItem(0) != GuardianComponentMaterial.Unknown)
        {
            return GuardianPoiStatus.Present;
        }

        return point.Type == GuardianPoiType.EmptyPuddle
            ? GuardianPoiStatus.Empty
            : GuardianPoiStatus.Unknown;
    }

    private static (int Heading, bool IsIndividual) ResolveRelicHeading(
        GuardianPointOfInterest point,
        GuardianSurveyData? survey,
        bool isRuins)
    {
        if (point.Type != GuardianPoiType.Relic)
        {
            return (-1, false);
        }

        if (survey?.RelicHeadings.TryGetValue(point.Name, out var relicHeading)
            == true)
        {
            var normalizedRelicHeading = NormalizeHeading(relicHeading);
            if (normalizedRelicHeading >= 0)
            {
                return (normalizedRelicHeading, true);
            }
        }

        var towerHeading = NormalizeHeading(survey?.RelicTowerHeading ?? -1);
        return isRuins && towerHeading >= 0
            ? (towerHeading, false)
            : (-1, false);
    }

    private static GuardianProjectedGroup ProjectGroup(
        string name,
        GuardianMapPoint point,
        GuardianMapPoint markerOffset)
    {
        var location = ProjectPolar(point.X, point.Y);
        return new GuardianProjectedGroup(
            name,
            location.X + markerOffset.X,
            location.Y + markerOffset.Y,
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
    int RelicTowerHeading = -1,
    string BackgroundImage = "",
    GuardianMapPoint ImageOffset = default,
    double ImageScaleFactor = 1,
    GuardianMapPoint MarkerOffset = default)
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
