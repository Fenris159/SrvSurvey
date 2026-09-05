using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Controls;

public sealed class SurfaceSurveyRadarControl : Control
{
    private const double MetersPerPixel = 4;

    public static readonly StyledProperty<
        IReadOnlyList<SurfaceRadarMarkerViewModel>?> MarkersProperty =
        AvaloniaProperty.Register<
            SurfaceSurveyRadarControl,
            IReadOnlyList<SurfaceRadarMarkerViewModel>?>(nameof(Markers));
    public static readonly StyledProperty<IBrush?> BackgroundBrushProperty =
        AvaloniaProperty.Register<SurfaceSurveyRadarControl, IBrush?>(
            nameof(BackgroundBrush));
    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<SurfaceSurveyRadarControl, IBrush?>(
            nameof(GridBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<SurfaceSurveyRadarControl, IBrush?>(
            nameof(AccentBrush));
    public static readonly StyledProperty<IBrush?> MutedBrushProperty =
        AvaloniaProperty.Register<SurfaceSurveyRadarControl, IBrush?>(
            nameof(MutedBrush));
    public static readonly StyledProperty<IBrush?> SuccessBrushProperty =
        AvaloniaProperty.Register<SurfaceSurveyRadarControl, IBrush?>(
            nameof(SuccessBrush));
    public static readonly StyledProperty<IBrush?> WarningBrushProperty =
        AvaloniaProperty.Register<SurfaceSurveyRadarControl, IBrush?>(
            nameof(WarningBrush));
    public static readonly StyledProperty<IBrush?> DangerBrushProperty =
        AvaloniaProperty.Register<SurfaceSurveyRadarControl, IBrush?>(
            nameof(DangerBrush));
    public static readonly StyledProperty<double> ScaleMultiplierProperty =
        AvaloniaProperty.Register<SurfaceSurveyRadarControl, double>(
            nameof(ScaleMultiplier),
            1);

    static SurfaceSurveyRadarControl()
    {
        AffectsRender<SurfaceSurveyRadarControl>(
            MarkersProperty,
            BackgroundBrushProperty,
            GridBrushProperty,
            AccentBrushProperty,
            MutedBrushProperty,
            SuccessBrushProperty,
            WarningBrushProperty,
            DangerBrushProperty,
            ScaleMultiplierProperty);
    }

    public SurfaceSurveyRadarControl()
    {
        ClipToBounds = true;
    }

    public IReadOnlyList<SurfaceRadarMarkerViewModel>? Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public IBrush? BackgroundBrush
    {
        get => GetValue(BackgroundBrushProperty);
        set => SetValue(BackgroundBrushProperty, value);
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

    public IBrush? SuccessBrush
    {
        get => GetValue(SuccessBrushProperty);
        set => SetValue(SuccessBrushProperty, value);
    }

    public IBrush? WarningBrush
    {
        get => GetValue(WarningBrushProperty);
        set => SetValue(WarningBrushProperty, value);
    }

    public IBrush? DangerBrush
    {
        get => GetValue(DangerBrushProperty);
        set => SetValue(DangerBrushProperty, value);
    }

    public double ScaleMultiplier
    {
        get => GetValue(ScaleMultiplierProperty);
        set => SetValue(ScaleMultiplierProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        var background = BackgroundBrush ?? Brushes.Transparent;
        var grid = GridBrush ?? Brushes.DimGray;
        var accent = AccentBrush ?? Brushes.Cyan;
        context.DrawRectangle(
            background,
            new Pen(grid, 1),
            bounds,
            8,
            8);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var center = bounds.Center;
        // Keep rings/markers inside the radar frame so out-of-range contacts
        // disappear at the border instead of painting over neighbouring UI.
        using (context.PushClip(bounds))
        {
            DrawGrid(context, bounds, center, grid);
            foreach (var marker in Markers ?? [])
            {
                DrawMarker(context, bounds, center, marker);
            }

            DrawCommander(context, center, accent);
        }
    }

    private static void DrawGrid(
        DrawingContext context,
        Rect bounds,
        Point center,
        IBrush brush)
    {
        var pen = new Pen(brush, 1);
        context.DrawLine(
            pen,
            new Point(center.X, bounds.Top + 8),
            new Point(center.X, bounds.Bottom - 8));
        context.DrawLine(
            pen,
            new Point(bounds.Left + 8, center.Y),
            new Point(bounds.Right - 8, center.Y));
        foreach (var radius in new[] { 50d, 100d })
        {
            context.DrawEllipse(null, pen, center, radius, radius);
        }

        context.DrawLine(
            new Pen(brush, 2),
            new Point(center.X, bounds.Top + 7),
            new Point(center.X, bounds.Top + 16));
    }

    private void DrawMarker(
        DrawingContext context,
        Rect bounds,
        Point center,
        SurfaceRadarMarkerViewModel marker)
    {
        var radians = marker.RelativeBearingDegrees * Math.PI / 180d;
        var scale = double.IsFinite(ScaleMultiplier)
            ? Math.Clamp(ScaleMultiplier, 0.25, 10)
            : 1;
        var point = new Point(
            center.X + Math.Sin(radians)
                * marker.DistanceMeters / MetersPerPixel * scale,
            center.Y - Math.Cos(radians)
                * marker.DistanceMeters / MetersPerPixel * scale);
        var radius = marker.RadiusMeters / MetersPerPixel * scale;
        if (radius > 0
            && point.X + radius >= bounds.Left
            && point.X - radius <= bounds.Right
            && point.Y + radius >= bounds.Top
            && point.Y - radius <= bounds.Bottom)
        {
            var circleBrush = GetCircleBrush(marker);
            context.DrawEllipse(
                null,
                new Pen(circleBrush, marker.IsInsideRadius ? 2.5 : 1.25),
                point,
                radius,
                radius);
        }

        if (!bounds.Inflate(8).Contains(point))
        {
            return;
        }

        var markerBrush = GetMarkerBrush(marker);
        if (marker.Kind is SurfaceRadarMarkerKind.Ship
            or SurfaceRadarMarkerKind.FormerShip)
        {
            DrawTriangle(context, point, markerBrush, 7);
        }
        else if (marker.Kind == SurfaceRadarMarkerKind.Srv)
        {
            context.DrawRectangle(
                markerBrush,
                new Pen(markerBrush, 1),
                new Rect(point.X - 5, point.Y - 4, 10, 8),
                2,
                2);
        }
        else
        {
            context.DrawEllipse(markerBrush, null, point, 3.5, 3.5);
        }
    }

    private IBrush GetCircleBrush(SurfaceRadarMarkerViewModel marker)
    {
        var muted = MutedBrush ?? Brushes.Gray;
        var success = SuccessBrush ?? Brushes.LimeGreen;
        var warning = WarningBrush ?? Brushes.Gold;
        var danger = DangerBrush ?? Brushes.Red;
        var accent = AccentBrush ?? Brushes.Cyan;
        if (string.Equals(marker.Status, "Died", StringComparison.OrdinalIgnoreCase))
        {
            return danger;
        }

        return marker.Kind switch
        {
            SurfaceRadarMarkerKind.MiningRig when marker.Status == "COLLECT" => accent,
            SurfaceRadarMarkerKind.MiningRig => marker.IsInsideRadius ? danger : warning,
            SurfaceRadarMarkerKind.HistoricalScan =>
                marker.IsInsideRadius ? danger : muted,
            // Bookmarks and Canonn prior rings: muted when inactive, green when
            // inside the drawn radius (genus sample distance for priors), else cyan.
            SurfaceRadarMarkerKind.Bookmark or SurfaceRadarMarkerKind.CanonnPrior =>
                GetActiveInsideRadiusCircleBrush(marker, muted, success, accent),
            SurfaceRadarMarkerKind.ActiveSample =>
                marker.IsInsideRadius ? warning : success,
            _ => accent,
        };
    }

    private static IBrush GetActiveInsideRadiusCircleBrush(
        SurfaceRadarMarkerViewModel marker,
        IBrush muted,
        IBrush success,
        IBrush accent)
    {
        if (!marker.IsActive)
        {
            return muted;
        }

        return marker.IsInsideRadius ? success : accent;
    }

    private IBrush GetMarkerBrush(SurfaceRadarMarkerViewModel marker)
    {
        return marker.Kind switch
        {
            SurfaceRadarMarkerKind.Ship => WarningBrush ?? Brushes.Gold,
            SurfaceRadarMarkerKind.FormerShip => MutedBrush ?? Brushes.Gray,
            SurfaceRadarMarkerKind.Srv => SuccessBrush ?? Brushes.LimeGreen,
            _ => GetCircleBrush(marker),
        };
    }

    private static void DrawCommander(
        DrawingContext context,
        Point center,
        IBrush brush)
    {
        DrawTriangle(context, center, brush, 9);
        context.DrawEllipse(null, new Pen(brush, 1), center, 13, 13);
    }

    private static void DrawTriangle(
        DrawingContext context,
        Point center,
        IBrush brush,
        double radius)
    {
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(
                new Point(center.X, center.Y - radius),
                isFilled: true);
            geometryContext.LineTo(new Point(
                center.X + radius * 0.7,
                center.Y + radius * 0.75));
            geometryContext.LineTo(new Point(
                center.X,
                center.Y + radius * 0.45));
            geometryContext.LineTo(new Point(
                center.X - radius * 0.7,
                center.Y + radius * 0.75));
            geometryContext.EndFigure(isClosed: true);
        }

        context.DrawGeometry(brush, new Pen(brush, 1), geometry);
    }
}
