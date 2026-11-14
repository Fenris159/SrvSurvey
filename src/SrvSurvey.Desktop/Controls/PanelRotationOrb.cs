using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SrvSurvey.Desktop.Localization;

namespace SrvSurvey.Desktop.Controls;

/// <summary>A dedicated tilt or roll handle with a full-area captured drag gesture.</summary>
public sealed class PanelRotationOrb : Control
{
    private const double RollDegreesPerLogicalPixel = 0.15;

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
    private double dragRenderScaling = 1;
    private IPointer? dragPointer;
    private bool dragging;
    private bool rolling;
    public event EventHandler? ResetRequested;
    public static readonly StyledProperty<bool> IsRollControlProperty = AvaloniaProperty.Register<
        PanelRotationOrb,
        bool
    >(nameof(IsRollControl));

    public bool IsRollControl
    {
        get => GetValue(IsRollControlProperty);
        set => SetValue(IsRollControlProperty, value);
    }

    static PanelRotationOrb()
    {
        AffectsRender<PanelRotationOrb>(
            IsRollControlProperty,
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
            LocalizationCatalog.Translate(
                IsRollControl
                    ? "Drag left or right to roll; Left and Right keys roll; Home resets rotation."
                    : "Arrow keys tilt; Control plus Left or Right rolls; Home resets rotation."
            )
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

    /// <summary>Shows separate perspective and roll symbols instead of overlapping center/rim targets.</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var pen = new Pen(Accent, IsFocused ? 2 : 1.5);
        double scale = Math.Min(Bounds.Width, Bounds.Height) / 30;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(center.X, center.Y)))
        {
            if (IsRollControl)
            {
                var arc = new StreamGeometry();
                using (StreamGeometryContext geometry = arc.Open())
                {
                    geometry.BeginFigure(new Point(8, 7), false);
                    geometry.ArcTo(new Point(8, -7), new Size(11, 11), 0, true, SweepDirection.Clockwise);
                }
                context.DrawGeometry(null, pen, arc);
                context.DrawLine(pen, new Point(8, -7), new Point(2, -7));
                context.DrawLine(pen, new Point(8, -7), new Point(8, -13));
                context.DrawLine(pen, new Point(-4, 0), new Point(4, 0));
            }
            else
            {
                var plane = new StreamGeometry();
                using (StreamGeometryContext geometry = plane.Open())
                {
                    geometry.BeginFigure(new Point(-6, -4), true);
                    geometry.LineTo(new Point(6, -2));
                    geometry.LineTo(new Point(6, 4));
                    geometry.LineTo(new Point(-6, 2));
                    geometry.EndFigure(true);
                }
                context.DrawGeometry(PanelBrush, pen, plane);
                DrawTiltArrow(context, pen, new Vector(1, 0));
                DrawTiltArrow(context, pen, new Vector(-1, 0));
                DrawTiltArrow(context, pen, new Vector(0, 1));
                DrawTiltArrow(context, pen, new Vector(0, -1));
            }
        }
    }

    private static void DrawTiltArrow(DrawingContext context, Pen pen, Vector direction)
    {
        var side = new Vector(-direction.Y, direction.X);
        Point tip = new(direction.X * 14, direction.Y * 14);
        context.DrawLine(pen, new Point(direction.X * 9, direction.Y * 9), tip);
        context.DrawLine(pen, tip, tip - direction * 2 + side * 2);
        context.DrawLine(pen, tip, tip - direction * 2 - side * 2);
    }

    /// <summary>Captures the handle's dedicated gesture without moving the overlay window.</summary>
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
        rolling = IsRollControl;
        var top = TopLevel.GetTopLevel(this);
        dragRenderScaling = top?.RenderScaling ?? 1;
        // Keep the actual press point even if motion has advanced past a queued press.
        previousScreenPoint = this.PointToScreen(e.GetPosition(this));
        dragPointer = e.Pointer;
        dragging = true;
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    /// <summary>Applies screen-space tilt or horizontal roll until the captured pointer is released.</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!dragging || !ReferenceEquals(dragPointer, e.Pointer))
        {
            return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            StopRotation(releasePointer: true);
            e.Handled = true;
            return;
        }
        PixelPoint current = this.PointToScreen(e.GetPosition(this));
        ApplyRotation(current);
        e.Handled = true;
    }

    private void ApplyRotation(PixelPoint current)
    {
        if (rolling)
        {
            SetCurrentValue(
                RollProperty,
                NormalizeRoll(
                    Roll - (current.X - previousScreenPoint.X) * RollDegreesPerLogicalPixel / dragRenderScaling
                )
            );
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
            if (IsRollControl || e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                SetCurrentValue(RollProperty, NormalizeRoll(Roll - direction * step));
            }
            else
            {
                SetCurrentValue(YawProperty, Math.Clamp(Yaw + direction * step, -85, 85));
            }
        }
        else if (!IsRollControl && e.Key is Key.Up or Key.Down)
        {
            SetCurrentValue(PitchProperty, Math.Clamp(Pitch + direction * step, -85, 85));
        }
        else
        {
            return;
        }
        e.Handled = true;
    }

    private static double NormalizeRoll(double angle) => (((angle % 360) + 540) % 360) - 180;
}
