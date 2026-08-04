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

            case GuardianAlignmentMode.Bear:
                DrawBear(context, bounds, center, pen);
                break;

            case GuardianAlignmentMode.Bowl:
                DrawBowl(context, bounds, center, pen);
                break;

            case GuardianAlignmentMode.Fistbump:
                DrawFistbump(context, bounds, center, pen);
                break;

            case GuardianAlignmentMode.Hammerbot:
                DrawHammerbot(context, bounds, center, pen);
                break;

            case GuardianAlignmentMode.Robolobster:
                DrawRobolobster(context, bounds, center, pen);
                break;

            case GuardianAlignmentMode.Crossroads:
            case GuardianAlignmentMode.Lacrosse:
            case GuardianAlignmentMode.Squid:
            case GuardianAlignmentMode.Stickyhand:
            case GuardianAlignmentMode.Turtle:
                DrawVerticalTarget(context, bounds, center, pen);
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

    private static void DrawBear(
        DrawingContext context,
        Rect bounds,
        Point center,
        Pen pen)
    {
        var unit = Math.Min(bounds.Width, bounds.Height) / 24;
        var y = center.Y + (bounds.Height * 0.05);
        context.DrawLine(
            pen,
            new Point(bounds.Left + 30, y),
            new Point(bounds.Right - 30, y));
        context.DrawLine(
            pen,
            new Point(center.X - (unit * 2), center.Y - unit),
            new Point(center.X - (unit * 2), center.Y - (unit * 7)));
        context.DrawLine(
            pen,
            new Point(center.X + (unit * 2), center.Y - unit),
            new Point(center.X + (unit * 2), center.Y - (unit * 7)));
        context.DrawLine(
            pen,
            new Point(center.X, center.Y - (unit * 10)),
            new Point(center.X, center.Y - (unit * 16)));
    }

    private static void DrawBowl(
        DrawingContext context,
        Rect bounds,
        Point center,
        Pen pen)
    {
        var y = center.Y - (bounds.Height * 0.1);
        context.DrawLine(
            pen,
            new Point(bounds.Left + 30, y),
            new Point(bounds.Right - 30, y));
        context.DrawLine(
            pen,
            new Point(center.X, bounds.Top + 25),
            new Point(center.X, bounds.Bottom - 25));
        var radius = bounds.Height / 7;
        var circleCenter = new Point(center.X, center.Y + radius);
        context.DrawEllipse(
            null,
            pen,
            circleCenter,
            radius,
            radius);
        context.DrawEllipse(
            null,
            pen,
            circleCenter,
            radius * 1.3,
            radius * 1.3);
    }

    private static void DrawFistbump(
        DrawingContext context,
        Rect bounds,
        Point center,
        Pen pen)
    {
        var size = Math.Min(bounds.Width, bounds.Height) * 0.12;
        var crossCenter = new Point(center.X, center.Y - bounds.Height * 0.1);
        context.DrawLine(
            pen,
            new Point(crossCenter.X - size, crossCenter.Y - size),
            new Point(crossCenter.X + size, crossCenter.Y + size));
        context.DrawLine(
            pen,
            new Point(crossCenter.X + size, crossCenter.Y - size),
            new Point(crossCenter.X - size, crossCenter.Y + size));
        context.DrawLine(
            pen,
            new Point(center.X, crossCenter.Y - (size * 2)),
            new Point(center.X, crossCenter.Y - (size * 4)));
    }

    private static void DrawHammerbot(
        DrawingContext context,
        Rect bounds,
        Point center,
        Pen pen)
    {
        var xUnit = bounds.Width * 0.035;
        var yUnit = bounds.Height * 0.06;
        context.DrawLine(
            pen,
            new Point(center.X - xUnit, center.Y - 10),
            new Point(center.X + xUnit, center.Y - 10));
        context.DrawLine(
            pen,
            new Point(center.X - (xUnit * 6), center.Y + 10),
            new Point(center.X - (xUnit * 4), center.Y + 10));
        context.DrawLine(
            pen,
            new Point(center.X + (xUnit * 4), center.Y + 10),
            new Point(center.X + (xUnit * 6), center.Y + 10));
        context.DrawLine(
            pen,
            new Point(center.X, center.Y + yUnit),
            new Point(center.X, bounds.Bottom - 20));
        context.DrawLine(
            pen,
            new Point(center.X - (xUnit * 1.7), center.Y + yUnit),
            new Point(center.X - xUnit, center.Y + (yUnit * 2)));
        context.DrawLine(
            pen,
            new Point(center.X + (xUnit * 1.7), center.Y + yUnit),
            new Point(center.X + xUnit, center.Y + (yUnit * 2)));
    }

    private static void DrawRobolobster(
        DrawingContext context,
        Rect bounds,
        Point center,
        Pen pen)
    {
        var target = new Point(center.X, center.Y - (bounds.Height * 0.04));
        var radius = Math.Min(bounds.Width, bounds.Height) * 0.12;
        context.DrawEllipse(null, pen, target, radius, radius);
        context.DrawEllipse(null, pen, target, radius * 1.5, radius * 1.5);
        context.DrawLine(
            pen,
            new Point(center.X, bounds.Top + 25),
            new Point(center.X, target.Y + (radius * 2.5)));
    }

    private static void DrawVerticalTarget(
        DrawingContext context,
        Rect bounds,
        Point center,
        Pen pen)
    {
        var half = Math.Min(100, bounds.Height * 0.22);
        context.DrawLine(
            pen,
            new Point(center.X, center.Y - half),
            new Point(center.X, center.Y - (half * 2)));
    }
}
