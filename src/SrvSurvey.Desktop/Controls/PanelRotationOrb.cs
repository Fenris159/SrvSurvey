using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Controls;

/// <summary>A draggable panel orientation preview with a separate rim gesture for roll.</summary>
public sealed class PanelRotationOrb : Control
{
    public static readonly StyledProperty<double> YawProperty = AvaloniaProperty.Register<PanelRotationOrb, double>(
        nameof(Yaw)
    );
    public static readonly StyledProperty<double> PitchProperty = AvaloniaProperty.Register<PanelRotationOrb, double>(
        nameof(Pitch)
    );
    public static readonly StyledProperty<double> RollProperty = AvaloniaProperty.Register<PanelRotationOrb, double>(
        nameof(Roll)
    );
    public static readonly StyledProperty<double> DepthProperty = AvaloniaProperty.Register<PanelRotationOrb, double>(
        nameof(Depth),
        1
    );
    public static readonly StyledProperty<IBrush?> AccentProperty = AvaloniaProperty.Register<
        PanelRotationOrb,
        IBrush?
    >(nameof(Accent));
    public static readonly StyledProperty<IBrush?> GuideBrushProperty = AvaloniaProperty.Register<
        PanelRotationOrb,
        IBrush?
    >(nameof(GuideBrush));
    public static readonly StyledProperty<IBrush?> PanelBrushProperty = AvaloniaProperty.Register<
        PanelRotationOrb,
        IBrush?
    >(nameof(PanelBrush));
    private Point previousPoint;
    private bool dragging;
    private bool rolling;

    static PanelRotationOrb()
    {
        AffectsRender<PanelRotationOrb>(
            YawProperty,
            PitchProperty,
            RollProperty,
            DepthProperty,
            IsFocusedProperty,
            AccentProperty,
            GuideBrushProperty,
            PanelBrushProperty
        );
        FocusableProperty.OverrideDefaultValue<PanelRotationOrb>(true);
    }

    public double Yaw
    {
        get => GetValue(YawProperty);
        set => SetValue(YawProperty, value);
    }
    public double Pitch
    {
        get => GetValue(PitchProperty);
        set => SetValue(PitchProperty, value);
    }
    public double Roll
    {
        get => GetValue(RollProperty);
        set => SetValue(RollProperty, value);
    }

    public double Depth
    {
        get => GetValue(DepthProperty);
        set => SetValue(DepthProperty, value);
    }
    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }
    public IBrush? GuideBrush
    {
        get => GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
    }
    public IBrush? PanelBrush
    {
        get => GetValue(PanelBrushProperty);
        set => SetValue(PanelBrushProperty, value);
    }

    /// <summary>Draws a theme-aware orientation sphere, rotation arrows and the panel's projective outline.</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double radius = Math.Max(1, Math.Min(Bounds.Width, Bounds.Height) / 2 - 9);
        var guide = new Pen(GuideBrush, 1);
        var accent = new Pen(Accent, IsFocused ? 2 : 1.5);
        context.DrawEllipse(Brushes.Transparent, accent, center, radius, radius);
        context.DrawEllipse(null, guide, center, radius, radius * 0.35);
        context.DrawEllipse(null, guide, center, radius * 0.35, radius);
        context.DrawLine(
            accent,
            new Point(center.X + radius - 4, center.Y - 8),
            new Point(center.X + radius, center.Y)
        );
        context.DrawLine(
            accent,
            new Point(center.X + radius, center.Y),
            new Point(center.X + radius + 4, center.Y - 8)
        );
        context.DrawLine(
            accent,
            new Point(center.X - radius + 4, center.Y + 8),
            new Point(center.X - radius, center.Y)
        );
        context.DrawLine(
            accent,
            new Point(center.X - radius, center.Y),
            new Point(center.X - radius - 4, center.Y + 8)
        );
        double width = radius * 1.25;
        double height = radius * 0.8;
        var rectangle = new Rect(center.X - width / 2, center.Y - height / 2, width, height);
        Matrix? projection = CockpitPanelProjection.Create(
            rectangle,
            Bounds.Size,
            new CockpitPanelPose(Yaw, Pitch, Roll, Depth),
            Matrix4x4.Identity
        );
        if (projection is not { } matrix)
        {
            return;
        }
        var shape = new StreamGeometry();
        using (StreamGeometryContext geometry = shape.Open())
        {
            geometry.BeginFigure(matrix.Transform(new Point(0, 0)), true);
            geometry.LineTo(matrix.Transform(new Point(width, 0)));
            geometry.LineTo(matrix.Transform(new Point(width, height)));
            geometry.LineTo(matrix.Transform(new Point(0, height)));
            geometry.EndFigure(true);
        }
        using (context.PushClip(new Rect(Bounds.Size)))
        {
            context.DrawGeometry(PanelBrush, accent, shape);
        }
        context.DrawEllipse(Accent, null, center, 2, 2);
    }

    /// <summary>Captures a central tilt drag or a rim roll drag without moving the overlay window.</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        previousPoint = e.GetPosition(this);
        Avalonia.Vector offset = previousPoint - new Point(Bounds.Width / 2, Bounds.Height / 2);
        rolling = offset.Length > Math.Min(Bounds.Width, Bounds.Height) * 0.36;
        dragging = true;
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    /// <summary>Applies bounded yaw and pitch, or continuous roll around the rim.</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!dragging)
        {
            return;
        }
        Point current = e.GetPosition(this);
        if (rolling)
        {
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
            double before = Math.Atan2(previousPoint.Y - center.Y, previousPoint.X - center.X);
            double after = Math.Atan2(current.Y - center.Y, current.X - center.X);
            SetCurrentValue(RollProperty, NormalizeRoll(Roll - (after - before) * 180 / Math.PI));
        }
        else
        {
            SetCurrentValue(YawProperty, Math.Clamp(Yaw + (current.X - previousPoint.X) * 0.6, -85, 85));
            SetCurrentValue(PitchProperty, Math.Clamp(Pitch - (current.Y - previousPoint.Y) * 0.6, -85, 85));
        }
        previousPoint = current;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (dragging)
        {
            dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        dragging = false;
        base.OnPointerCaptureLost(e);
    }

    /// <summary>Offers the same angle control with arrow keys; Control changes roll and Shift uses five-degree steps.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int direction = e.Key is Key.Left or Key.Down ? -1 : 1;
        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 5 : 1;
        if (e.Key is Key.Left or Key.Right)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                SetCurrentValue(RollProperty, NormalizeRoll(Roll - direction * step));
            }
            else
            {
                SetCurrentValue(YawProperty, Math.Clamp(Yaw + direction * step, -85, 85));
            }
        }
        else if (e.Key is Key.Up or Key.Down)
        {
            SetCurrentValue(PitchProperty, Math.Clamp(Pitch + direction * step, -85, 85));
        }
        else
        {
            return;
        }
        e.Handled = true;
    }

    private static double NormalizeRoll(double angle) => ((angle + 540) % 360) - 180;
}
