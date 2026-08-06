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

        var layout = new LegLayout(left, right, y, width, totalDistance);
        var brushes = new LegBrushes(behind, ahead, target, boost);
        var x = left;
        for (var index = 0; index < legs.Count; index++)
        {
            x = DrawLeg(context, legs, index, x, layout, brushes);
        }
    }

    private double DrawLeg(
        DrawingContext context,
        IReadOnlyList<JumpInfoRouteLeg> legs,
        int index,
        double x,
        LegLayout layout,
        LegBrushes brushes)
    {
        var leg = legs[index];
        var nextX = index == legs.Count - 1
            ? layout.Right
            : x + layout.Width * (leg.DistanceLy / layout.TotalDistance);
        var brush = ResolveLegBrush(index, brushes);
        DrawLegSegments(context, leg, index, x, nextX, layout, brushes, brush);
        DrawLegMarker(context, leg, index, nextX, layout, brushes, brush);
        return nextX;
    }

    private IBrush ResolveLegBrush(int index, LegBrushes brushes)
    {
        if (index == TargetLegIndex)
        {
            return brushes.Target;
        }

        return index < TargetLegIndex ? brushes.Behind : brushes.Ahead;
    }

    private void DrawLegSegments(
        DrawingContext context,
        JumpInfoRouteLeg leg,
        int index,
        double x,
        double nextX,
        LegLayout layout,
        LegBrushes brushes,
        IBrush brush)
    {
        if (leg.RequiresBoost)
        {
            context.DrawLine(
                new Pen(brushes.Boost, index == TargetLegIndex ? 7 : 5),
                new Point(x, layout.Y),
                new Point(nextX, layout.Y));
        }

        context.DrawLine(
            new Pen(brush, index == TargetLegIndex ? 4 : 2.5),
            new Point(x, layout.Y),
            new Point(nextX, layout.Y));
    }

    private void DrawLegMarker(
        DrawingContext context,
        JumpInfoRouteLeg leg,
        int index,
        double nextX,
        LegLayout layout,
        LegBrushes brushes,
        IBrush brush)
    {
        var radius = index == TargetLegIndex ? 5d : 3.5;
        context.DrawEllipse(
            brush,
            null,
            new Point(nextX, layout.Y),
            radius,
            radius);
        if (leg.IsScoopable)
        {
            context.DrawEllipse(
                null,
                new Pen(brushes.Target, 1.5),
                new Point(nextX, layout.Y - 9),
                4,
                2.5);
        }
    }

    private readonly record struct LegLayout(
        double Left,
        double Right,
        double Y,
        double Width,
        double TotalDistance);

    private readonly record struct LegBrushes(
        IBrush Behind,
        IBrush Ahead,
        IBrush Target,
        IBrush Boost);
}
