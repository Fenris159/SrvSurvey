using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Desktop.Controls;

public sealed class JumpRouteProgressControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<JumpInfoRouteLeg>?>
        LegsProperty = AvaloniaProperty.Register<
            JumpRouteProgressControl,
            IReadOnlyList<JumpInfoRouteLeg>?>(nameof(Legs));
    public static readonly StyledProperty<int> TargetLegIndexProperty =
        AvaloniaProperty.Register<JumpRouteProgressControl, int>(
            nameof(TargetLegIndex),
            -1);
    public static readonly StyledProperty<IBrush?> AheadBrushProperty =
        AvaloniaProperty.Register<JumpRouteProgressControl, IBrush?>(
            nameof(AheadBrush));
    public static readonly StyledProperty<IBrush?> BehindBrushProperty =
        AvaloniaProperty.Register<JumpRouteProgressControl, IBrush?>(
            nameof(BehindBrush));
    public static readonly StyledProperty<IBrush?> TargetBrushProperty =
        AvaloniaProperty.Register<JumpRouteProgressControl, IBrush?>(
            nameof(TargetBrush));
    public static readonly StyledProperty<IBrush?> BoostBrushProperty =
        AvaloniaProperty.Register<JumpRouteProgressControl, IBrush?>(
            nameof(BoostBrush));
    public static readonly StyledProperty<IBrush?> BackgroundLineBrushProperty =
        AvaloniaProperty.Register<JumpRouteProgressControl, IBrush?>(
            nameof(BackgroundLineBrush));

    static JumpRouteProgressControl()
    {
        AffectsRender<JumpRouteProgressControl>(
            LegsProperty,
            TargetLegIndexProperty,
            AheadBrushProperty,
            BehindBrushProperty,
            TargetBrushProperty,
            BoostBrushProperty,
            BackgroundLineBrushProperty);
    }

    public IReadOnlyList<JumpInfoRouteLeg>? Legs
    {
        get => GetValue(LegsProperty);
        set => SetValue(LegsProperty, value);
    }

    public int TargetLegIndex
    {
        get => GetValue(TargetLegIndexProperty);
        set => SetValue(TargetLegIndexProperty, value);
    }

    public IBrush? AheadBrush
    {
        get => GetValue(AheadBrushProperty);
        set => SetValue(AheadBrushProperty, value);
    }

    public IBrush? BehindBrush
    {
        get => GetValue(BehindBrushProperty);
        set => SetValue(BehindBrushProperty, value);
    }

    public IBrush? TargetBrush
    {
        get => GetValue(TargetBrushProperty);
        set => SetValue(TargetBrushProperty, value);
    }

    public IBrush? BoostBrush
    {
        get => GetValue(BoostBrushProperty);
        set => SetValue(BoostBrushProperty, value);
    }

    public IBrush? BackgroundLineBrush
    {
        get => GetValue(BackgroundLineBrushProperty);
        set => SetValue(BackgroundLineBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Legs is not { Count: > 0 } legs || Bounds.Width <= 24)
        {
            return;
        }

        var left = 8d;
        var right = Bounds.Width - 8;
        var y = Bounds.Height / 2;
        var width = right - left;
        var totalDistance = legs.Sum(leg => leg.DistanceLy);
        if (totalDistance <= 0)
        {
            return;
        }

        var background = BackgroundLineBrush ?? Brushes.DimGray;
        var behind = BehindBrush ?? Brushes.Gray;
        var ahead = AheadBrush ?? Brushes.Orange;
        var target = TargetBrush ?? Brushes.Cyan;
        var boost = BoostBrush ?? Brushes.Gold;
        context.DrawLine(
            new Pen(background, 2),
            new Point(left, y),
            new Point(right, y));
        context.DrawEllipse(behind, null, new Point(left, y), 3, 3);

        var x = left;
        for (var index = 0; index < legs.Count; index++)
        {
            var leg = legs[index];
            var nextX = index == legs.Count - 1
                ? right
                : x + width * (leg.DistanceLy / totalDistance);
            var brush = index == TargetLegIndex
                ? target
                : index < TargetLegIndex
                    ? behind
                    : ahead;
            if (leg.RequiresBoost)
            {
                context.DrawLine(
                    new Pen(boost, index == TargetLegIndex ? 7 : 5),
                    new Point(x, y),
                    new Point(nextX, y));
            }

            context.DrawLine(
                new Pen(brush, index == TargetLegIndex ? 4 : 2.5),
                new Point(x, y),
                new Point(nextX, y));
            var radius = index == TargetLegIndex ? 5d : 3.5;
            context.DrawEllipse(
                brush,
                null,
                new Point(nextX, y),
                radius,
                radius);
            if (leg.IsScoopable)
            {
                context.DrawEllipse(
                    null,
                    new Pen(target, 1.5),
                    new Point(nextX, y - 9),
                    4,
                    2.5);
            }

            x = nextX;
        }
    }
}
