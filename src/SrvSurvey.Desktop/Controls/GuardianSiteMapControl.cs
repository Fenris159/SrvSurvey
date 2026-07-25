using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Desktop.Controls;

public sealed class GuardianSiteMapControl : Control
{
    public static readonly StyledProperty<GuardianSiteMapProjection?> ProjectionProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, GuardianSiteMapProjection?>(
            nameof(Projection));
    public static readonly StyledProperty<GuardianSiteProximitySnapshot?>
        ProximityProperty = AvaloniaProperty.Register<
            GuardianSiteMapControl,
            GuardianSiteProximitySnapshot?>(nameof(Proximity));
    public static readonly StyledProperty<double> MapScaleProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, double>(
            nameof(MapScale),
            double.NaN);
    public static readonly StyledProperty<double> CommanderHeadingProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, double>(
            nameof(CommanderHeading));
    public static readonly StyledProperty<IBrush?> MapBackgroundProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, IBrush?>(
            nameof(MapBackground));
    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, IBrush?>(
            nameof(GridBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, IBrush?>(
            nameof(AccentBrush));
    public static readonly StyledProperty<IBrush?> MutedBrushProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, IBrush?>(
            nameof(MutedBrush));
    public static readonly StyledProperty<IBrush?> PresentBrushProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, IBrush?>(
            nameof(PresentBrush));
    public static readonly StyledProperty<IBrush?> AbsentBrushProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, IBrush?>(
            nameof(AbsentBrush));
    public static readonly StyledProperty<IBrush?> EmptyBrushProperty =
        AvaloniaProperty.Register<GuardianSiteMapControl, IBrush?>(
            nameof(EmptyBrush));

    static GuardianSiteMapControl()
    {
        AffectsRender<GuardianSiteMapControl>(
            ProjectionProperty,
            ProximityProperty,
            MapScaleProperty,
            CommanderHeadingProperty,
            MapBackgroundProperty,
            GridBrushProperty,
            AccentBrushProperty,
            MutedBrushProperty,
            PresentBrushProperty,
            AbsentBrushProperty,
            EmptyBrushProperty);
    }

    public GuardianSiteMapProjection? Projection
    {
        get => GetValue(ProjectionProperty);
        set => SetValue(ProjectionProperty, value);
    }

    public GuardianSiteProximitySnapshot? Proximity
    {
        get => GetValue(ProximityProperty);
        set => SetValue(ProximityProperty, value);
    }

    public double MapScale
    {
        get => GetValue(MapScaleProperty);
        set => SetValue(MapScaleProperty, value);
    }

    public double CommanderHeading
    {
        get => GetValue(CommanderHeadingProperty);
        set => SetValue(CommanderHeadingProperty, value);
    }

    public IBrush? MapBackground
    {
        get => GetValue(MapBackgroundProperty);
        set => SetValue(MapBackgroundProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? MutedBrush
    {
        get => GetValue(MutedBrushProperty);
        set => SetValue(MutedBrushProperty, value);
    }

    public IBrush? PresentBrush
    {
        get => GetValue(PresentBrushProperty);
        set => SetValue(PresentBrushProperty, value);
    }

    public IBrush? AbsentBrush
    {
        get => GetValue(AbsentBrushProperty);
        set => SetValue(AbsentBrushProperty, value);
    }

    public IBrush? EmptyBrush
    {
        get => GetValue(EmptyBrushProperty);
        set => SetValue(EmptyBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(
            MapBackground ?? Brushes.Transparent,
            new Pen(GridBrush ?? Brushes.Gray, 1),
            bounds,
            8,
            8);
        if (Projection is not { } projection
            || bounds.Width <= 0
            || bounds.Height <= 0)
        {
            return;
        }

        var grid = GridBrush ?? Brushes.Gray;
        var accent = AccentBrush ?? Brushes.Cyan;
        var viewportCenter = bounds.Center;
        var radius = Math.Max(
            1,
            Math.Min(bounds.Width, bounds.Height) / 2 - 30);
        var fittedScale = radius / projection.MaximumDistance;
        var scale = double.IsFinite(MapScale) && MapScale > 0
            ? Math.Clamp(MapScale, 0.1, 15)
            : fittedScale;
        var mapOrigin = TransformMapPoint(
            0,
            0,
            Proximity,
            CommanderHeading,
            viewportCenter,
            scale);
        var gridExtent = Math.Max(bounds.Width, bounds.Height) / scale * 2;
        var gridPen = new Pen(grid, 1, dashStyle: DashStyle.Dash);
        context.DrawLine(
            gridPen,
            TransformMapPoint(
                0,
                -gridExtent,
                Proximity,
                CommanderHeading,
                viewportCenter,
                scale),
            TransformMapPoint(
                0,
                gridExtent,
                Proximity,
                CommanderHeading,
                viewportCenter,
                scale));
        context.DrawLine(
            gridPen,
            TransformMapPoint(
                -gridExtent,
                0,
                Proximity,
                CommanderHeading,
                viewportCenter,
                scale),
            TransformMapPoint(
                gridExtent,
                0,
                Proximity,
                CommanderHeading,
                viewportCenter,
                scale));
        for (var ring = 1; ring <= 4; ring++)
        {
            var ringRadius = projection.MaximumDistance * scale * ring / 4;
            context.DrawEllipse(
                null,
                gridPen,
                mapOrigin,
                ringRadius,
                ringRadius);
        }

        context.DrawEllipse(accent, null, mapOrigin, 3, 3);
        foreach (var point in projection.Points)
        {
            DrawPoint(
                context,
                point,
                TransformMapPoint(
                    point.X,
                    point.Y,
                    Proximity,
                    CommanderHeading,
                    viewportCenter,
                    scale));
        }

        foreach (var group in projection.Groups)
        {
            DrawGroup(
                context,
                group,
                TransformMapPoint(
                    group.X,
                    group.Y,
                    Proximity,
                    CommanderHeading,
                    viewportCenter,
                    scale));
        }

        if (Proximity is not null)
        {
            DrawCommander(context, viewportCenter);
        }
    }

    public static Point TransformMapPoint(
        double x,
        double y,
        GuardianSiteProximitySnapshot? proximity,
        double commanderHeading,
        Point viewportCenter,
        double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        var relativeX = x - (proximity?.MapX ?? 0);
        var relativeY = y - (proximity?.MapY ?? 0);
        var heading = double.IsFinite(commanderHeading)
            ? commanderHeading
            : 0;
        var radians = heading * Math.PI / 180;
        var rotatedX = (relativeX * Math.Cos(radians))
            + (relativeY * Math.Sin(radians));
        var rotatedY = (-relativeX * Math.Sin(radians))
            + (relativeY * Math.Cos(radians));
        return new Point(
            viewportCenter.X + rotatedX * scale,
            viewportCenter.Y + rotatedY * scale);
    }

    private void DrawCommander(
        DrawingContext context,
        Point location)
    {
        var brush = PresentBrush ?? Brushes.LimeGreen;
        var pen = new Pen(brush, 2);
        context.DrawEllipse(MapBackground, pen, location, 7, 7);
        context.DrawEllipse(brush, null, location, 2.5, 2.5);
    }

    private void DrawPoint(
        DrawingContext context,
        GuardianProjectedPoint point,
        Point location)
    {
        var brush = GetPointBrush(point);
        var pen = new Pen(brush, point.IsActiveObelisk ? 2.5 : 1.5);
        if (point.IsActiveObelisk)
        {
            context.DrawEllipse(null, pen, location, 8, 8);
        }

        switch (point.Type)
        {
            case GuardianPoiType.Obelisk:
                DrawTriangle(context, location, 4.5, pen, point.IsScannedObelisk);
                context.DrawLine(
                    pen,
                    new Point(location.X, location.Y - 4),
                    new Point(location.X, location.Y + 5));
                break;

            case GuardianPoiType.BrokenObelisk:
                context.DrawLine(
                    pen,
                    new Point(location.X - 4, location.Y - 4),
                    new Point(location.X + 4, location.Y + 4));
                context.DrawLine(
                    pen,
                    new Point(location.X + 4, location.Y - 4),
                    new Point(location.X - 4, location.Y + 4));
                break;

            case GuardianPoiType.Pylon:
                DrawDiamond(context, location, 5, pen);
                break;

            case GuardianPoiType.Component:
            case GuardianPoiType.DestructiblePanel:
                context.DrawRectangle(
                    point.Status == GuardianPoiStatus.Present ? brush : null,
                    pen,
                    new Rect(location.X - 3.5, location.Y - 3.5, 7, 7));
                break;

            case GuardianPoiType.Relic:
                DrawTriangle(
                    context,
                    location,
                    6,
                    pen,
                    point.Status == GuardianPoiStatus.Present);
                break;

            default:
                context.DrawEllipse(
                    point.Status is GuardianPoiStatus.Present
                        or GuardianPoiStatus.Empty
                            ? brush
                            : null,
                    pen,
                    location,
                    4,
                    4);
                break;
        }
    }

    private void DrawGroup(
        DrawingContext context,
        GuardianProjectedGroup group,
        Point location)
    {
        var brush = AccentBrush ?? Brushes.Cyan;
        var text = new FormattedText(
            group.Name,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
            12,
            brush);
        context.DrawEllipse(
            MapBackground,
            new Pen(brush, 1),
            location,
            9,
            9);
        context.DrawText(
            text,
            new Point(
                location.X - text.Width / 2,
                location.Y - text.Height / 2));
    }

    private IBrush GetPointBrush(GuardianProjectedPoint point)
    {
        if (point.IsScannedObelisk)
        {
            return PresentBrush ?? Brushes.LimeGreen;
        }

        if (point.IsActiveObelisk)
        {
            return AccentBrush ?? Brushes.Cyan;
        }

        return point.Status switch
        {
            GuardianPoiStatus.Present => PresentBrush ?? Brushes.LimeGreen,
            GuardianPoiStatus.Absent => AbsentBrush ?? Brushes.OrangeRed,
            GuardianPoiStatus.Empty => EmptyBrush ?? Brushes.Goldenrod,
            _ => MutedBrush ?? Brushes.Gray,
        };
    }

    private static void DrawTriangle(
        DrawingContext context,
        Point center,
        double radius,
        Pen pen,
        bool fill)
    {
        var points = new[]
        {
            new Point(center.X, center.Y - radius),
            new Point(center.X + radius, center.Y + radius),
            new Point(center.X - radius, center.Y + radius),
        };
        var geometry = CreatePolygon(points);
        context.DrawGeometry(fill ? pen.Brush : null, pen, geometry);
    }

    private static void DrawDiamond(
        DrawingContext context,
        Point center,
        double radius,
        Pen pen)
    {
        var geometry = CreatePolygon(
        [
            new Point(center.X, center.Y - radius),
            new Point(center.X + radius, center.Y),
            new Point(center.X, center.Y + radius),
            new Point(center.X - radius, center.Y),
        ]);
        context.DrawGeometry(null, pen, geometry);
    }

    private static StreamGeometry CreatePolygon(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: true);
            for (var index = 1; index < points.Count; index++)
            {
                context.LineTo(points[index]);
            }

            context.EndFigure(isClosed: true);
        }

        return geometry;
    }
}
