using System.Numerics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SrvSurvey.Desktop.Localization;
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
    private PixelPoint previousScreenPoint;
    private PixelPoint rollCenter;
    private double dragRenderScaling = 1;
    private IPointer? dragPointer;
    private IOverlayDragPointerProbe? pointerProbe;
    private bool dragging;
    private bool rolling;
    public event EventHandler? ResetRequested;
    private double OrbRadius =>
        Math.Max(
            1,
            Math.Min(Bounds.Width, Bounds.Height) / 2 - Math.Min(9, Math.Min(Bounds.Width, Bounds.Height) * 0.15)
        );

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

    /// <summary>Exposes the focusable orb as an interactive control to assistive technology.</summary>
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        UpdateAccessibleAngles();
        AutomationProperties.SetHelpText(
            this,
            LocalizationCatalog.Translate("Arrow keys tilt; Control plus Left or Right rolls; Home resets rotation.")
        );
        return new ControlAutomationPeer(this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == YawProperty || change.Property == PitchProperty || change.Property == RollProperty)
        {
            UpdateAccessibleAngles();
        }
    }

    private void UpdateAccessibleAngles() =>
        AutomationProperties.SetItemStatus(
            this,
            LocalizationCatalog.Translate($"Yaw {Yaw:0.#}°, pitch {Pitch:0.#}°, roll {Roll:0.#}°")
        );

    /// <summary>Draws a theme-aware orientation sphere, rotation arrows and the panel's projective outline.</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double radius = OrbRadius;
        double arrowScale = Math.Min(1, radius / 30);
        var guide = new Pen(GuideBrush, 1);
        var accent = new Pen(Accent, IsFocused ? 2 : 1.5);
        context.DrawEllipse(Brushes.Transparent, accent, center, radius, radius);
        context.DrawEllipse(null, guide, center, radius, radius * 0.35);
        context.DrawEllipse(null, guide, center, radius * 0.35, radius);
        context.DrawLine(
            accent,
            new Point(center.X + radius - 4 * arrowScale, center.Y - 8 * arrowScale),
            new Point(center.X + radius, center.Y)
        );
        context.DrawLine(
            accent,
            new Point(center.X + radius, center.Y),
            new Point(center.X + radius + 4 * arrowScale, center.Y - 8 * arrowScale)
        );
        context.DrawLine(
            accent,
            new Point(center.X - radius + 4 * arrowScale, center.Y + 8 * arrowScale),
            new Point(center.X - radius, center.Y)
        );
        context.DrawLine(
            accent,
            new Point(center.X - radius, center.Y),
            new Point(center.X - radius - 4 * arrowScale, center.Y + 8 * arrowScale)
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
        BeginRotation(e);
    }

    /// <summary>Lets the outer button border start the same captured gesture as the orb itself.</summary>
    internal void BeginRotation(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        if (dragging && ReferenceEquals(dragPointer, e.Pointer))
        {
            e.Handled = true;
            return;
        }
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        Avalonia.Vector offset = e.GetPosition(this) - center;
        rolling = offset.Length > OrbRadius * 0.8;
        var top = TopLevel.GetTopLevel(this);
        dragRenderScaling = top?.RenderScaling ?? 1;
        pointerProbe?.Dispose();
        pointerProbe = X11Native.TryCreatePointerProbe(top?.TryGetPlatformHandle()?.HandleDescriptor);
        // Keep the actual press point even if motion has advanced past a queued press.
        previousScreenPoint = this.PointToScreen(e.GetPosition(this));
        rollCenter = this.PointToScreen(center);
        dragPointer = e.Pointer;
        dragging = true;
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    /// <summary>Applies bounded yaw and pitch, or continuous roll around the rim.</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!dragging || !ReferenceEquals(dragPointer, e.Pointer))
        {
            return;
        }
        OverlayDragPointerSample? sample = pointerProbe?.Read();
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            StopRotation(releasePointer: true);
            e.Handled = true;
            return;
        }
        PixelPoint current = sample is { LeftButtonPressed: true } native
            ? native.Position
            : this.PointToScreen(e.GetPosition(this));
        ApplyRotation(current);
        e.Handled = true;
    }

    private void ApplyRotation(PixelPoint current)
    {
        if (rolling)
        {
            double before = Math.Atan2(previousScreenPoint.Y - rollCenter.Y, previousScreenPoint.X - rollCenter.X);
            double after = Math.Atan2(current.Y - rollCenter.Y, current.X - rollCenter.X);
            SetCurrentValue(RollProperty, NormalizeRoll(Roll - (after - before) * 180 / Math.PI));
        }
        else
        {
            SetCurrentValue(
                YawProperty,
                Math.Clamp(Yaw + (current.X - previousScreenPoint.X) * 0.6 / dragRenderScaling, -85, 85)
            );
            SetCurrentValue(
                PitchProperty,
                Math.Clamp(Pitch - (current.Y - previousScreenPoint.Y) * 0.6 / dragRenderScaling, -85, 85)
            );
        }
        previousScreenPoint = current;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (dragging && ReferenceEquals(dragPointer, e.Pointer))
        {
            ApplyRotation(this.PointToScreen(e.GetPosition(this)));
            StopRotation(releasePointer: true);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        StopRotation(releasePointer: false);
        base.OnPointerCaptureLost(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopRotation(releasePointer: true);
        base.OnDetachedFromVisualTree(e);
    }

    private void StopRotation(bool releasePointer)
    {
        dragging = false;
        pointerProbe?.Dispose();
        pointerProbe = null;
        IPointer? pointer = dragPointer;
        dragPointer = null;
        if (releasePointer && pointer?.Captured == this)
        {
            pointer.Capture(null);
        }
    }

    /// <summary>Offers the same angle control with arrow keys; Control changes roll and Shift uses five-degree steps.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Home)
        {
            ResetRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
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
