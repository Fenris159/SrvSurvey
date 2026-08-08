using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SrvSurvey.Desktop.Controls;

public sealed class GroundTargetGuidanceControl : Control
{
    public static readonly StyledProperty<double> RelativeBearingDegreesProperty =
        AvaloniaProperty.Register<GroundTargetGuidanceControl, double>(
            nameof(RelativeBearingDegrees));
    public static readonly StyledProperty<double> AttackAngleDegreesProperty =
        AvaloniaProperty.Register<GroundTargetGuidanceControl, double>(
            nameof(AttackAngleDegrees));
    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<GroundTargetGuidanceControl, IBrush?>(
            nameof(GridBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<GroundTargetGuidanceControl, IBrush?>(
            nameof(AccentBrush));
    public static readonly StyledProperty<IBrush?> WarningBrushProperty =
        AvaloniaProperty.Register<GroundTargetGuidanceControl, IBrush?>(
            nameof(WarningBrush));
    public static readonly StyledProperty<IBrush?> DangerBrushProperty =
        AvaloniaProperty.Register<GroundTargetGuidanceControl, IBrush?>(
            nameof(DangerBrush));
    public static readonly StyledProperty<IBrush?> MutedBrushProperty =
        AvaloniaProperty.Register<GroundTargetGuidanceControl, IBrush?>(
            nameof(MutedBrush));

    static GroundTargetGuidanceControl()
    {
        AffectsRender<GroundTargetGuidanceControl>(
            RelativeBearingDegreesProperty,
            AttackAngleDegreesProperty,
            GridBrushProperty,
            AccentBrushProperty,
            WarningBrushProperty,
            DangerBrushProperty,
            MutedBrushProperty);
    }

    public double RelativeBearingDegrees
    {
        get => GetValue(RelativeBearingDegreesProperty);
        set => SetValue(RelativeBearingDegreesProperty, value);
    }

    public double AttackAngleDegrees
    {
        get => GetValue(AttackAngleDegreesProperty);
        set => SetValue(AttackAngleDegreesProperty, value);
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

    public IBrush? MutedBrush
    {
        get => GetValue(MutedBrushProperty);
        set => SetValue(MutedBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var grid = GridBrush ?? Brushes.DimGray;
        var accent = AccentBrush ?? Brushes.Cyan;
        var warning = WarningBrush ?? Brushes.Orange;
        var danger = DangerBrush ?? Brushes.Red;
        var muted = MutedBrush ?? Brushes.Gray;
        var center = new Point(Bounds.Width / 2, 45);
        var radius = Math.Max(10, Math.Min(Bounds.Width - 16, 82) / 2);

        context.DrawEllipse(null, new Pen(grid, 1), center, radius, radius);
        context.DrawLine(
            new Pen(grid, 1),
            new Point(center.X, center.Y - radius),
            new Point(center.X, center.Y - radius + 7));
        context.DrawLine(
            new Pen(grid, 1),
            new Point(center.X + radius - 7, center.Y),
            new Point(center.X + radius, center.Y));
        context.DrawLine(
            new Pen(grid, 1),
            new Point(center.X, center.Y + radius - 7),
            new Point(center.X, center.Y + radius));
        context.DrawLine(
            new Pen(grid, 1),
            new Point(center.X - radius, center.Y),
            new Point(center.X - radius + 7, center.Y));

        var bearingRadians = RelativeBearingDegrees * Math.PI / 180d;
        var target = new Point(
            center.X + Math.Sin(bearingRadians) * (radius - 9),
            center.Y - Math.Cos(bearingRadians) * (radius - 9));
        context.DrawLine(new Pen(accent, 2.2), center, target);
        context.DrawEllipse(accent, null, target, 4, 4);
        RingedPointerDrawing.Draw(
            context,
            center,
            20,
            bearingDegrees: 0,
            accent,
            strokeThickness: 1.5);

        var baselineY = Math.Max(94, Bounds.Height - 12);
        var origin = new Point(10, baselineY);
        var length = Math.Max(20, Math.Min(70, Bounds.Width - 22));
        context.DrawLine(
            new Pen(grid, 1),
            origin,
            new Point(origin.X + length, origin.Y));
        var attackAngle = Math.Clamp(AttackAngleDegrees, 0, 89);
        var attackRadians = attackAngle * Math.PI / 180d;
        var attackBrush = attackAngle switch
        {
            <= 5 => muted,
            <= 30 => warning,
            <= 50 => accent,
            _ => danger,
        };
        var attackEnd = new Point(
            origin.X + Math.Cos(attackRadians) * length,
            origin.Y - Math.Sin(attackRadians) * length);
        context.DrawLine(new Pen(attackBrush, 2.6), origin, attackEnd);
        context.DrawEllipse(attackBrush, null, attackEnd, 3, 3);
    }

}
