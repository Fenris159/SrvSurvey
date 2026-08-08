using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SrvSurvey.Desktop.Controls;

public sealed class DirectionalChevronControl : Control
{
    public static readonly StyledProperty<double> BearingDegreesProperty =
        AvaloniaProperty.Register<DirectionalChevronControl, double>(
            nameof(BearingDegrees));

    public static readonly StyledProperty<bool> IsFarProperty =
        AvaloniaProperty.Register<DirectionalChevronControl, bool>(
            nameof(IsFar));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<DirectionalChevronControl, IBrush?>(
            nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<DirectionalChevronControl, double>(
            nameof(StrokeThickness),
            1.75);

    static DirectionalChevronControl()
    {
        AffectsRender<DirectionalChevronControl>(
            BearingDegreesProperty,
            IsFarProperty,
            StrokeProperty,
            StrokeThicknessProperty);
    }

    public double BearingDegrees
    {
        get => GetValue(BearingDegreesProperty);
        set => SetValue(BearingDegreesProperty, value);
    }

    public bool IsFar
    {
        get => GetValue(IsFarProperty);
        set => SetValue(IsFarProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        DirectionalChevronDrawing.Draw(
            context,
            new Point(Bounds.Width / 2, Bounds.Height / 2),
            Math.Min(Bounds.Width, Bounds.Height),
            BearingDegrees,
            IsFar,
            Stroke ?? Brushes.Orange,
            StrokeThickness);
    }
}

internal static class DirectionalChevronDrawing
{
    public static void Draw(
        DrawingContext context,
        Point center,
        double size,
        double bearingDegrees,
        bool isFar,
        IBrush stroke,
        double strokeThickness)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(stroke);
        if (!double.IsFinite(size) || size <= 0)
        {
            return;
        }

        var maximumThickness = Math.Max(0.5, size / 3);
        var thickness = double.IsFinite(strokeThickness)
            ? Math.Clamp(strokeThickness, 0.5, maximumThickness)
            : 1.75;
        var usableSize = Math.Max(1, size - thickness);
        var halfWidth = usableSize * 0.32;
        var angle = double.IsFinite(bearingDegrees) ? bearingDegrees : 0;
        var radians = angle * Math.PI / 180d;
        var pen = new Pen(
            stroke,
            thickness,
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);

        if (isFar)
        {
            DrawChevron(
                context,
                pen,
                center,
                halfWidth,
                -usableSize * 0.39,
                -usableSize * 0.02,
                radians);
            DrawChevron(
                context,
                pen,
                center,
                halfWidth,
                -usableSize * 0.02,
                usableSize * 0.35,
                radians);
            return;
        }

        DrawChevron(
            context,
            pen,
            center,
            halfWidth,
            -usableSize * 0.29,
            usableSize * 0.24,
            radians);
    }

    private static void DrawChevron(
        DrawingContext context,
        Pen pen,
        Point center,
        double halfWidth,
        double tipY,
        double legY,
        double radians)
    {
        var tip = Rotate(center, 0, tipY, radians);
        context.DrawLine(
            pen,
            Rotate(center, -halfWidth, legY, radians),
            tip);
        context.DrawLine(
            pen,
            tip,
            Rotate(center, halfWidth, legY, radians));
    }

    private static Point Rotate(
        Point center,
        double x,
        double y,
        double radians)
    {
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return new Point(
            center.X + x * cosine - y * sine,
            center.Y + x * sine + y * cosine);
    }
}
