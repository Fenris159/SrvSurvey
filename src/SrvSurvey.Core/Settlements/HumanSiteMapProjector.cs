using System.Diagnostics.CodeAnalysis;

namespace SrvSurvey.Core.Settlements;

public sealed class HumanSiteMapProjector
{
    private const byte PathTypeMask = 0x07;
    private const byte StartPoint = 0;
    private const byte LinePoint = 1;
    private const byte BezierPoint = 3;
    private const byte CloseSubpath = 0x80;

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The projector is consumed through an instance service contract.")]
    [SuppressMessage(
        "Maintainability",
        "S2325:Make methods and properties static",
        Justification = "The projector is consumed through an instance service contract.")]
    public HumanSiteMapProjection Project(
        HumanSiteTemplate template,
        HumanSiteMapDisplayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        options ??= HumanSiteMapDisplayOptions.Default;

        var skipped = 0;
        var buildings = template.Buildings.Select(ProjectBuilding).ToArray();
        var pads = template.LandingPads
            .Select((pad, index) => new HumanSiteProjectedPoint(
                (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                HumanSiteMapPointKind.LandingPad,
                pad.Offset,
                pad.Rotation,
                pad.SecurityLevel,
                pad.Floor,
                pad.Size))
            .ToArray();
        var doors = ProjectPoints(
            template.SecureDoors,
            HumanSiteMapPointKind.SecureDoor,
            ref skipped);
        var namedPoints = template.NamedPoints
            .Where(point => options.ShowMedkits
                || !string.Equals(
                    point.Name,
                    "Medkit",
                    StringComparison.OrdinalIgnoreCase))
            .Where(point => options.ShowBatteries
                || !string.Equals(
                    point.Name,
                    "Battery",
                    StringComparison.OrdinalIgnoreCase))
            .Select(point => ProjectNamedPoint(point, ref skipped))
            .Where(point => point is not null)
            .Select(point => point!)
            .ToArray();
        var terminals = options.ShowDataTerminals
            ? ProjectPoints(
                template.DataTerminals,
                HumanSiteMapPointKind.DataTerminal,
                ref skipped)
            : [];
        var conflictZonePoints = options.ShowConflictZonePoints
            ? ProjectPoints(
                template.ConflictZonePoints,
                HumanSiteMapPointKind.ConflictZone,
                ref skipped)
            : [];

        var maximumDistance = buildings
            .SelectMany(building => building.Paths)
            .SelectMany(path => path.Segments)
            .SelectMany(GetSegmentPoints)
            .Concat(pads.Select(point => point.Offset))
            .Concat(doors.Select(point => point.Offset))
            .Concat(namedPoints.Select(point => point.Offset))
            .Concat(terminals.Select(point => point.Offset))
            .Concat(conflictZonePoints.Select(point => point.Offset))
            .Where(point => point.IsPlausibleMapOffset())
            .Select(point => Math.Sqrt(
                (point.X * point.X) + (point.Y * point.Y)))
            .DefaultIfEmpty(1)
            .Max();

        return new HumanSiteMapProjection(
            template.Economy,
            template.SubType,
            template.Name,
            buildings,
            pads,
            doors,
            namedPoints,
            terminals,
            conflictZonePoints,
            Math.Max(1, maximumDistance),
            skipped);
    }

    private static HumanSiteProjectedBuilding ProjectBuilding(
        HumanSiteBuilding building)
    {
        return new HumanSiteProjectedBuilding(
            building.Name,
            building.Paths.Select(ProjectPath).ToArray());
    }

    private static HumanSiteProjectedPath ProjectPath(
        HumanSiteBuildingPath path)
    {
        var segments = new List<HumanSitePathSegment>();
        var index = 0;
        while (index < path.Points.Count)
        {
            var type = path.PointTypes[index];
            var baseType = (byte)(type & PathTypeMask);
            if (baseType == StartPoint)
            {
                segments.Add(new HumanSitePathSegment(
                    HumanSitePathSegmentKind.Move,
                    path.Points[index],
                    default,
                    default));
            }
            else if (baseType == LinePoint)
            {
                segments.Add(new HumanSitePathSegment(
                    HumanSitePathSegmentKind.Line,
                    path.Points[index],
                    default,
                    default));
            }
            else if (baseType == BezierPoint)
            {
                if (index + 2 >= path.Points.Count
                    || (path.PointTypes[index + 1] & PathTypeMask) != BezierPoint
                    || (path.PointTypes[index + 2] & PathTypeMask) != BezierPoint)
                {
                    throw new InvalidDataException(
                        "A human settlement building has an incomplete Bézier segment.");
                }

                segments.Add(new HumanSitePathSegment(
                    HumanSitePathSegmentKind.CubicBezier,
                    path.Points[index],
                    path.Points[index + 1],
                    path.Points[index + 2]));
                index += 2;
                type = path.PointTypes[index];
            }
            else
            {
                throw new InvalidDataException(
                    $"Unknown human settlement path-point type '{baseType}'.");
            }

            if ((type & CloseSubpath) != 0)
            {
                segments.Add(new HumanSitePathSegment(
                    HumanSitePathSegmentKind.Close,
                    default,
                    default,
                    default));
            }

            index++;
        }

        return new HumanSiteProjectedPath(
            path.FillMode == 1
                ? HumanSitePathFillRule.NonZero
                : HumanSitePathFillRule.EvenOdd,
            segments);
    }

    private static HumanSiteProjectedPoint[] ProjectPoints(
        IEnumerable<HumanSitePointOfInterest> source,
        HumanSiteMapPointKind kind,
        ref int skipped)
    {
        var result = new List<HumanSiteProjectedPoint>();
        foreach (var point in source)
        {
            if (!point.Offset.IsPlausibleMapOffset())
            {
                skipped++;
                continue;
            }

            result.Add(new HumanSiteProjectedPoint(
                string.Empty,
                kind,
                point.Offset,
                point.Rotation,
                point.SecurityLevel,
                point.Floor,
                HumanSiteLandingPadSize.Unknown));
        }

        return result.ToArray();
    }

    private static HumanSiteProjectedPoint? ProjectNamedPoint(
        HumanSiteNamedPointOfInterest point,
        ref int skipped)
    {
        if (!point.Offset.IsPlausibleMapOffset())
        {
            skipped++;
            return null;
        }

        return new HumanSiteProjectedPoint(
            point.Name,
            HumanSiteMapPointKind.NamedPoint,
            point.Offset,
            point.Rotation,
            point.SecurityLevel,
            point.Floor,
            HumanSiteLandingPadSize.Unknown);
    }

    private static IEnumerable<HumanSiteMapPoint> GetSegmentPoints(
        HumanSitePathSegment segment)
    {
        return segment.Kind switch
        {
            HumanSitePathSegmentKind.Move or HumanSitePathSegmentKind.Line =>
                [segment.First],
            HumanSitePathSegmentKind.CubicBezier =>
                [segment.First, segment.Second, segment.Third],
            _ => [],
        };
    }
}

public sealed record HumanSiteMapDisplayOptions(
    bool ShowMedkits,
    bool ShowBatteries,
    bool ShowDataTerminals,
    bool ShowConflictZonePoints)
{
    public static HumanSiteMapDisplayOptions Default { get; } =
        new(true, true, true, false);
}

public sealed record HumanSiteMapProjection(
    HumanSiteEconomy Economy,
    int SubType,
    string Name,
    IReadOnlyList<HumanSiteProjectedBuilding> Buildings,
    IReadOnlyList<HumanSiteProjectedPoint> LandingPads,
    IReadOnlyList<HumanSiteProjectedPoint> SecureDoors,
    IReadOnlyList<HumanSiteProjectedPoint> NamedPoints,
    IReadOnlyList<HumanSiteProjectedPoint> DataTerminals,
    IReadOnlyList<HumanSiteProjectedPoint> ConflictZonePoints,
    double MaximumDistance,
    int SkippedImplausiblePoints);

public sealed record HumanSiteProjectedBuilding(
    string Name,
    IReadOnlyList<HumanSiteProjectedPath> Paths);

public sealed record HumanSiteProjectedPath(
    HumanSitePathFillRule FillRule,
    IReadOnlyList<HumanSitePathSegment> Segments);

public readonly record struct HumanSitePathSegment(
    HumanSitePathSegmentKind Kind,
    HumanSiteMapPoint First,
    HumanSiteMapPoint Second,
    HumanSiteMapPoint Third);

public sealed record HumanSiteProjectedPoint(
    string Name,
    HumanSiteMapPointKind Kind,
    HumanSiteMapPoint Offset,
    double Rotation,
    int SecurityLevel,
    int Floor,
    HumanSiteLandingPadSize LandingPadSize);

public enum HumanSitePathSegmentKind
{
    Move,
    Line,
    CubicBezier,
    Close,
}

public enum HumanSitePathFillRule
{
    EvenOdd,
    NonZero,
}

public enum HumanSiteMapPointKind
{
    LandingPad,
    SecureDoor,
    NamedPoint,
    DataTerminal,
    ConflictZone,
}
