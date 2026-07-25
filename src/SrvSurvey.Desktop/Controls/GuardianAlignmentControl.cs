using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Controls;

public sealed class GuardianAlignmentControl : Control
{
    public static readonly StyledProperty<GuardianAlignmentMode?> ModeProperty =
        AvaloniaProperty.Register<
            GuardianAlignmentControl,
            GuardianAlignmentMode?>(nameof(Mode));
    public static readonly StyledProperty<IBrush?> GuideBrushProperty =
        AvaloniaProperty.Register<GuardianAlignmentControl, IBrush?>(
            nameof(GuideBrush));
    public static readonly StyledProperty<IBrush?> ShadowBrushProperty =
        AvaloniaProperty.Register<GuardianAlignmentControl, IBrush?>(
            nameof(ShadowBrush));

    static GuardianAlignmentControl()
    {
        AffectsRender<GuardianAlignmentControl>(
            ModeProperty,
            GuideBrushProperty,
            ShadowBrushProperty);
    }

    public GuardianAlignmentMode? Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public IBrush? GuideBrush
    {
        get => GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
    }

    public IBrush? ShadowBrush
    {
        get => GetValue(ShadowBrushProperty);
        set => SetValue(ShadowBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Mode is not { } mode || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var bounds = new Rect(Bounds.Size);
        var center = bounds.Center;
        var guide = GuideBrush ?? Brushes.Gold;
        var shadow = ShadowBrush ?? Brushes.Black;
        var guidePen = new Pen(guide, 3);
        var shadowPen = new Pen(shadow, 6, DashStyle.Dash);
        DrawMode(context, mode, bounds, center, shadowPen);
        DrawMode(context, mode, bounds, center, guidePen);
    }

    private static void DrawMode(
        DrawingContext context,
        GuardianAlignmentMode mode,
        Rect bounds,
        Point center,
        Pen pen)
    {
        switch (mode)
        {
            case GuardianAlignmentMode.Buttress:
                DrawButtress(context, bounds, center, pen);
                break;

            case GuardianAlignmentMode.RelicTower:
                DrawRelicTower(context, bounds, center, pen);
                break;

            case GuardianAlignmentMode.Alpha:
                DrawAlpha(context, bounds, center, pen);
                break;

            case GuardianAlignmentMode.Beta:
                DrawBeta(context, bounds, center, pen);
                break;

            case GuardianAlignmentMode.Gamma:
                DrawGamma(context, bounds, center, pen);
                break;

            default:
                DrawStructure(context, mode, bounds, center, pen);
                break;
        }
    }

    private static void DrawButtress(
        DrawingContext context,
        Rect bounds,
        Point center,
        Pen pen)
    {
        context.DrawLine(
            pen,
            center,
            new Point(center.X, bounds.Bottom - 8));
        context.DrawLine(
            pen,
            new Point(center.X - 40, center.Y + 35),
            new Point(center.X - 40, bounds.Bottom - 45));
        context.DrawLine(
            pen,
            new Point(center.X + 40, center.Y + 35),
            new Point(center.X + 40, bounds.Bottom - 45));
    }

    private static void DrawRelicTower(
        DrawingContext context,
        Rect bounds,
        Point center,
        Pen pen)
    {
        var spacing = Math.Min(42, bounds.Width * 0.08);
        context.DrawLine(
            pen,
            new Point(center.X - spacing, center.Y - 80),
            new Point(center.X - spacing, bounds.Bottom - 20));
        context.DrawLine(
            pen,
            new Point(center.X + spacing, center.Y - 80),
            new Point(center.X + spacing, bounds.Bottom - 20));
        context.DrawLine(
            pen,
            new Point(center.X - spacing - 25, center.Y - 80),
            new Point(center.X - spacing, center.Y - 80));
        context.DrawLine(
            pen,
            new Point(center.X + spacing, center.Y - 80),
            new Point(center.X + spacing + 25, center.Y - 80));
    }

    private static void DrawAlpha(
        DrawingContext context,
        Rect bounds,
        Point center,
        Pen pen)
    {
        var y = bounds.Bottom - Math.Max(70, bounds.Height * 0.18);
        var target = new Point(center.X, y);
        foreach (var radius in new[] { 18d, 42d, 68d })
        {
            context.DrawEllipse(null, pen, target, radius, radius);
        }

        context.DrawLine(
            pen,
            new Point(center.X + 68, y + 35),
            new Point(bounds.Right - 10, y + 35));
    }

    private static void DrawBeta(
        DrawingContext context,
        Rect bounds,
        Point center,
        Pen pen)
    {
        var target = new Point(center.X, bounds.Top + bounds.Height * 0.32);
        context.DrawEllipse(null, pen, target, 48, 48);
        context.DrawLine(
            pen,
            new Point(bounds.Left + 10, target.Y + 25),
            new Point(center.X - 105, target.Y + 25));
        context.DrawLine(
            pen,
            new Point(center.X + 105, target.Y + 25),
            new Point(bounds.Right - 10, target.Y + 25));
        context.DrawLine(
            pen,
            new Point(center.X, target.Y + 48),
            new Point(center.X, bounds.Bottom - 20));
    }

    private static void DrawGamma(
        DrawingContext context,
        Rect bounds,
        Point center,
        Pen pen)
    {
        var target = new Point(
            bounds.Left + bounds.Width * 0.7,
            bounds.Top + bounds.Height * 0.52);
        context.DrawEllipse(null, pen, target, 30, 30);
        context.DrawLine(
            pen,
            new Point(center.X, target.Y),
            new Point(target.X - 30, target.Y));
        context.DrawLine(
            pen,
            new Point(target.X, target.Y + 30),
            new Point(target.X, target.Y + 80));
    }

    private static void DrawStructure(
        DrawingContext context,
        GuardianAlignmentMode mode,
        Rect bounds,
        Point center,
        Pen pen)
    {
        var spread = mode switch
        {
            GuardianAlignmentMode.Fistbump => 80,
            GuardianAlignmentMode.Robolobster => 180,
            GuardianAlignmentMode.Squid => 145,
            GuardianAlignmentMode.Stickyhand => 120,
            _ => 105,
        };
        var height = mode is GuardianAlignmentMode.Bowl
            or GuardianAlignmentMode.Turtle
                ? 85
                : 130;
        context.DrawLine(
            pen,
            new Point(center.X - spread, center.Y),
            new Point(center.X + spread, center.Y));
        context.DrawLine(
            pen,
            new Point(center.X, center.Y - height),
            new Point(center.X, center.Y + height));
        context.DrawEllipse(
            null,
            pen,
            center,
            Math.Min(spread, bounds.Width / 3),
            Math.Min(height, bounds.Height / 3));
    }
}
