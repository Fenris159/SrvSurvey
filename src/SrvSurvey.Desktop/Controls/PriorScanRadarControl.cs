using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Controls;

public sealed class PriorScanRadarControl : Control
{
    private const double MetersPerPixel = 4;

    public static readonly StyledProperty<
        IReadOnlyList<PriorScanRadarTargetViewModel>?> TargetsProperty =
        AvaloniaProperty.Register<
            PriorScanRadarControl,
            IReadOnlyList<PriorScanRadarTargetViewModel>?>(nameof(Targets));
    public static readonly StyledProperty<bool> UseSmallCirclesProperty =
        AvaloniaProperty.Register<PriorScanRadarControl, bool>(
            nameof(UseSmallCircles),
            true);
    public static readonly StyledProperty<IBrush?> BackgroundBrushProperty =
        AvaloniaProperty.Register<PriorScanRadarControl, IBrush?>(
            nameof(BackgroundBrush));
    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<PriorScanRadarControl, IBrush?>(
            nameof(GridBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<PriorScanRadarControl, IBrush?>(
            nameof(AccentBrush));
    public static readonly StyledProperty<IBrush?> MutedBrushProperty =
        AvaloniaProperty.Register<PriorScanRadarControl, IBrush?>(
            nameof(MutedBrush));
    public static readonly StyledProperty<IBrush?> CloseBrushProperty =
        AvaloniaProperty.Register<PriorScanRadarControl, IBrush?>(
            nameof(CloseBrush));

    static PriorScanRadarControl()
    {
        AffectsRender<PriorScanRadarControl>(
            TargetsProperty,
            UseSmallCirclesProperty,
            BackgroundBrushProperty,
            GridBrushProperty,
            AccentBrushProperty,
            MutedBrushProperty,
            CloseBrushProperty);
    }

    public PriorScanRadarControl()
    {
        ClipToBounds = true;
    }

    public IReadOnlyList<PriorScanRadarTargetViewModel>? Targets
    {
        get => GetValue(TargetsProperty);
        set => SetValue(TargetsProperty, value);
    }

    public bool UseSmallCircles
    {
        get => GetValue(UseSmallCirclesProperty);
        set => SetValue(UseSmallCirclesProperty, value);
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

    public IBrush? CloseBrush
    {
        get => GetValue(CloseBrushProperty);
        set => SetValue(CloseBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        var grid = GridBrush ?? Brushes.DimGray;
        var background = BackgroundBrush ?? Brushes.Transparent;
        var accent = AccentBrush ?? Brushes.Cyan;
        var muted = MutedBrush ?? Brushes.Gray;
        var close = CloseBrush ?? Brushes.LimeGreen;
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
        using (context.PushClip(bounds))
        {
            context.DrawLine(
                new Pen(grid, 1),
                new Point(center.X, bounds.Top + 8),
                new Point(center.X, bounds.Bottom - 8));
            context.DrawLine(
                new Pen(grid, 1),
                new Point(bounds.Left + 8, center.Y),
                new Point(bounds.Right - 8, center.Y));
            context.DrawEllipse(
                null,
                new Pen(grid, 1),
                center,
                50,
                50);
            context.DrawEllipse(
                null,
                new Pen(grid, 1),
                center,
                100,
                100);

            foreach (var target in Targets ?? [])
            {
                DrawTarget(
                    context,
                    target,
                    center,
                    bounds,
                    accent,
                    muted,
                    close);
            }

            DrawCommander(context, center, accent);
        }
    }

    private void DrawTarget(
        DrawingContext context,
        PriorScanRadarTargetViewModel target,
        Point center,
        Rect bounds,
        IBrush accent,
        IBrush muted,
        IBrush close)
    {
        var point = ResolveTargetPoint(target, center);
        var signalRadius = ResolveSignalRadius(target);
        if (!IsTargetVisible(point, signalRadius, bounds))
        {
            return;
        }

        var brush = ResolveTargetBrush(target, accent, muted, close);
        context.DrawEllipse(
            null,
            new Pen(brush, target.IsClose ? 2.5 : 1.5),
            point,
            signalRadius,
            signalRadius);
        context.DrawEllipse(brush, null, point, 3, 3);
        if (target.IsClose)
        {
            context.DrawEllipse(
                null,
                new Pen(brush, 1),
                point,
                12.5,
                12.5);
        }
    }

    private static Point ResolveTargetPoint(
        PriorScanRadarTargetViewModel target,
        Point center)
    {
        var radians = target.RelativeBearingDegrees * Math.PI / 180d;
        return new Point(
            center.X + Math.Sin(radians)
                * target.DistanceMeters / MetersPerPixel,
            center.Y - Math.Cos(radians)
                * target.DistanceMeters / MetersPerPixel);
    }

    private double ResolveSignalRadius(PriorScanRadarTargetViewModel target)
    {
        var signalRadiusMeters = UseSmallCircles
            ? 100
            : target.SampleRadiusMeters;
        return Math.Clamp(
            signalRadiusMeters / MetersPerPixel,
            5,
            100);
    }

    private static bool IsTargetVisible(Point point, double signalRadius, Rect bounds)
    {
        return point.X + signalRadius >= bounds.Left
            && point.X - signalRadius <= bounds.Right
            && point.Y + signalRadius >= bounds.Top
            && point.Y - signalRadius <= bounds.Bottom;
    }

    private static IBrush ResolveTargetBrush(
        PriorScanRadarTargetViewModel target,
        IBrush accent,
        IBrush muted,
        IBrush close)
    {
        if (target.IsClose)
        {
            return close;
        }

        return target.IsActive ? accent : muted;
    }

    private static void DrawCommander(
        DrawingContext context,
        Point center,
        IBrush brush)
    {
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(
                new Point(center.X, center.Y - 9),
                isFilled: true);
            geometryContext.LineTo(new Point(center.X + 6, center.Y + 7));
            geometryContext.LineTo(new Point(center.X, center.Y + 4));
            geometryContext.LineTo(new Point(center.X - 6, center.Y + 7));
            geometryContext.EndFigure(isClosed: true);
        }

        context.DrawGeometry(brush, new Pen(brush, 1), geometry);
    }
}
