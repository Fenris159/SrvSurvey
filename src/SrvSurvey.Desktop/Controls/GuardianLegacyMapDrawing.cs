using Avalonia;
using Avalonia.Media;
using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Desktop.Controls;

internal static class GuardianLegacyMapDrawing
{
    internal static readonly Color Cyan = Color.FromRgb(84, 223, 237);
    internal static readonly Color DarkCyan = Color.FromRgb(0, 139, 139);
    internal static readonly Color RelicBlue = Color.FromRgb(66, 44, 255);
    internal static readonly Color MissingFill = Color.FromArgb(128, 64, 64, 64);
    internal static readonly Color MissingStroke = Color.FromArgb(128, 128, 128, 128);
    internal static readonly Color UnknownStroke = Color.FromArgb(128, 119, 136, 153);
    internal static readonly Color UnknownFill = Color.FromRgb(47, 79, 79);
    internal static readonly Color SiteHeading = Color.FromArgb(128, 139, 0, 0);
    internal static readonly Color TowerHeading = Color.FromArgb(128, 66, 44, 255);
    internal static readonly Color IndividualTowerHeading =
        Color.FromArgb(32, 66, 44, 255);
    internal static readonly Color Target = Colors.Lime;

    private static readonly Point[] ObeliskPoints =
    [
        new(-0.5, 2),
        new(-1.5, -1.5),
        new(2.5, -0.5),
        new(-0.5, 2),
    ];

    private static readonly Point[] BrokenObeliskPoints =
    [
        new(-0.5, 2.5),
        new(-1.5, -1.5),
        new(2.5, -0.5),
        new(-0.5, 2.5),
    ];

    private static readonly Point[] PylonPoints =
    [
        new(0, -3),
        new(6, 0),
        new(0, 3),
        new(-6, 0),
        new(0, -3),
    ];

    private static readonly Point[] ComponentPoints =
    [
        new(0, 2),
        new(-2, -1),
        new(2, -1),
        new(0, 2),
        new(0, 5),
        new(-5, -3),
        new(5, -3),
        new(0, 5),
    ];

    private static readonly Point[] RelicPoints =
    [
        new(-8, -8),
        new(8, -8),
        new(0, 8),
        new(-8, -8),
    ];

    internal static GuardianLegacyPointStyle GetPointStyle(
        GuardianPoiType type,
        GuardianPoiStatus status,
        bool isActiveObelisk = false)
    {
        return type switch
        {
            GuardianPoiType.Obelisk or GuardianPoiType.BrokenObelisk => new(
                Colors.Transparent,
                isActiveObelisk ? Cyan : DarkCyan,
                0.5,
                GuardianLegacyStrokePattern.Solid),
            GuardianPoiType.Pylon => StatusStrokeStyle(status, 2),
            GuardianPoiType.Component => GetComponentStyle(status),
            GuardianPoiType.Relic => GetRelicStyle(status),
            GuardianPoiType.DestructiblePanel => new(
                Colors.Transparent,
                Cyan,
                1,
                GuardianLegacyStrokePattern.Solid),
            _ => GetArtifactStyle(type, status),
        };
    }

    private static GuardianLegacyPointStyle GetArtifactStyle(
        GuardianPoiType type,
        GuardianPoiStatus status)
    {
        if (status == GuardianPoiStatus.Unknown)
        {
            return new GuardianLegacyPointStyle(
                Colors.Transparent,
                Cyan,
                3,
                GuardianLegacyStrokePattern.Dot);
        }

        if (status == GuardianPoiStatus.Absent)
        {
            return new GuardianLegacyPointStyle(
                MissingFill,
                MissingStroke,
                3,
                GuardianLegacyStrokePattern.Solid);
        }

        if (status == GuardianPoiStatus.Empty
            || type == GuardianPoiType.EmptyPuddle)
        {
            return new GuardianLegacyPointStyle(
                Colors.Gold,
                Colors.Yellow,
                3,
                GuardianLegacyStrokePattern.Solid);
        }

        return type switch
        {
            GuardianPoiType.Orb => new(
                Color.FromRgb(255, 127, 39),
                Color.FromRgb(147, 58, 0),
                3,
                GuardianLegacyStrokePattern.Solid),
            GuardianPoiType.Casket => new(
                Color.FromRgb(34, 177, 76),
                Color.FromRgb(17, 87, 38),
                3,
                GuardianLegacyStrokePattern.Solid),
            GuardianPoiType.Tablet => new(
                Color.FromRgb(153, 217, 234),
                Color.FromRgb(33, 135, 160),
                3,
                GuardianLegacyStrokePattern.Solid),
            GuardianPoiType.Totem => new(
                Color.FromRgb(63, 72, 204),
                Color.FromRgb(29, 34, 105),
                3,
                GuardianLegacyStrokePattern.Solid),
            GuardianPoiType.Urn => new(
                Color.FromRgb(163, 73, 164),
                Color.FromRgb(84, 37, 84),
                3,
                GuardianLegacyStrokePattern.Solid),
            _ => new GuardianLegacyPointStyle(
                Color.FromRgb(100, 0, 0),
                Colors.Red,
                3,
                GuardianLegacyStrokePattern.Solid),
        };
    }

    private static GuardianLegacyPointStyle GetComponentStyle(
        GuardianPoiStatus status)
    {
        var color = status switch
        {
            GuardianPoiStatus.Present => Colors.Lime,
            GuardianPoiStatus.Absent => MissingStroke,
            GuardianPoiStatus.Empty => Colors.Yellow,
            _ => Cyan,
        };
        return new GuardianLegacyPointStyle(
            Colors.Transparent,
            color,
            1,
            status == GuardianPoiStatus.Empty
                ? GuardianLegacyStrokePattern.Solid
                : GuardianLegacyStrokePattern.Dash);
    }

    private static GuardianLegacyPointStyle GetRelicStyle(
        GuardianPoiStatus status)
    {
        return status == GuardianPoiStatus.Present
            ? new GuardianLegacyPointStyle(
                RelicBlue,
                Cyan,
                2,
                GuardianLegacyStrokePattern.Solid)
            : new GuardianLegacyPointStyle(
                MissingFill,
                MissingStroke,
                1,
                GuardianLegacyStrokePattern.Solid);
    }

    internal static double GetGlyphRotation(
        GuardianProjectedPoint point,
        GuardianSiteMapProjection projection,
        double commanderHeading)
    {
        var relativeHeading = double.IsFinite(commanderHeading)
            ? commanderHeading
            : 0;
        return point.Type switch
        {
            GuardianPoiType.Obelisk or GuardianPoiType.BrokenObelisk =>
                NormalizeDegrees(point.Rotation - relativeHeading + 167.5),
            GuardianPoiType.Pylon =>
                NormalizeDegrees(point.Rotation - relativeHeading),
            GuardianPoiType.Component =>
                NormalizeDegrees(point.Rotation - relativeHeading - 45),
            GuardianPoiType.Relic when point.RelicHeading >= 0
                && projection.SiteHeading >= 0 => NormalizeDegrees(
                    point.RelicHeading
                    - projection.SiteHeading
                    - relativeHeading
                    - 180),
            GuardianPoiType.Relic => NormalizeDegrees(-relativeHeading),
            _ => 0,
        };
    }

    internal static IReadOnlyList<Point> CreateGlyphPoints(
        GuardianPoiType type,
        Point center,
        double rotation,
        double scale = 1)
    {
        var source = type switch
        {
            GuardianPoiType.Obelisk => ObeliskPoints,
            GuardianPoiType.BrokenObelisk => BrokenObeliskPoints,
            GuardianPoiType.Pylon => PylonPoints,
            GuardianPoiType.Component => ComponentPoints,
            GuardianPoiType.Relic => RelicPoints,
            _ => [],
        };
        return source
            .Select(point => center + RotateClockwise(
                new Point(point.X * scale, point.Y * scale),
                rotation))
            .ToArray();
    }

    internal static IReadOnlyList<Point> CreateComponentMaterialCenters(
        Point center,
        double scale = 1)
    {
        return new[] { -150d, 92d, -28d }
            .Select(angle => center + RotateClockwise(
                new Point(0, 8 * scale),
                angle))
            .ToArray();
    }

    internal static Color? GetComponentMaterialColor(
        GuardianComponentMaterial material)
    {
        return material switch
        {
            GuardianComponentMaterial.Cell => Colors.Lime,
            GuardianComponentMaterial.Conduit => Colors.Cyan,
            GuardianComponentMaterial.Tech => Colors.OrangeRed,
            _ => null,
        };
    }

    internal static Color GetActiveObeliskEffectColor(
        GuardianProjectedPoint point)
    {
        if (point.IsRamTahNeededObelisk)
        {
            return Cyan;
        }

        return point.IsScannedObelisk
            ? Color.FromRgb(255, 111, 0)
            : Colors.LightGray;
    }

    internal static double GetPuddleRadius(
        GuardianSiteMapProjection projection,
        GuardianProjectedPoint point)
    {
        if (point.Status == GuardianPoiStatus.Unknown)
        {
            return 5;
        }

        if (projection.IsRuins)
        {
            return 8;
        }

        return point.Status == GuardianPoiStatus.Absent ? 4 : 5;
    }

    internal static (Point Start, Point End) CreateHeadingLine(
        Point center,
        double length,
        double rotation)
    {
        var direction = RotateClockwise(new Point(0, length), rotation);
        return (center - direction, center + direction);
    }

    internal static IReadOnlyList<Point> CreateWedge(
        Point center,
        double radius,
        double rotation,
        int segments = 12)
    {
        var points = new List<Point>(segments + 2) { center };
        for (var index = 0; index <= segments; index++)
        {
            var angle = rotation + 240 + (90d * index / segments);
            var radians = angle * Math.PI / 180;
            points.Add(new Point(
                center.X + Math.Cos(radians) * radius,
                center.Y + Math.Sin(radians) * radius));
        }

        return points;
    }

    internal static Point RotateClockwise(Point point, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(
            point.X * Math.Cos(radians) - point.Y * Math.Sin(radians),
            point.X * Math.Sin(radians) + point.Y * Math.Cos(radians));
    }

    private static GuardianLegacyPointStyle StatusStrokeStyle(
        GuardianPoiStatus status,
        double width)
    {
        var color = status switch
        {
            GuardianPoiStatus.Present => Colors.DodgerBlue,
            GuardianPoiStatus.Absent => MissingStroke,
            GuardianPoiStatus.Empty => Colors.Yellow,
            _ => Cyan,
        };
        return new GuardianLegacyPointStyle(
            Colors.Transparent,
            color,
            width,
            GuardianLegacyStrokePattern.Solid);
    }

    private static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}

internal readonly record struct GuardianLegacyPointStyle(
    Color Fill,
    Color Stroke,
    double StrokeWidth,
    GuardianLegacyStrokePattern Pattern)
{
    internal bool HasFill => Fill.A > 0;
}

internal enum GuardianLegacyStrokePattern
{
    Solid,
    Dash,
    Dot,
}
