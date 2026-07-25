using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SrvSurvey.Desktop.Controls;

public sealed class CodexImageViewport : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<CodexImageViewport, Bitmap?>(nameof(Source));
    public static readonly StyledProperty<IBrush?> ViewportBackgroundProperty =
        AvaloniaProperty.Register<CodexImageViewport, IBrush?>(
            nameof(ViewportBackground));

    private double zoom = 1;
    private Vector offset;
    private Point? dragOrigin;
    private Vector dragStartOffset;

    static CodexImageViewport()
    {
        AffectsRender<CodexImageViewport>(
            SourceProperty,
            ViewportBackgroundProperty);
        SourceProperty.Changed.AddClassHandler<CodexImageViewport>(
            (control, _) => control.ResetView());
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public IBrush? ViewportBackground
    {
        get => GetValue(ViewportBackgroundProperty);
        set => SetValue(ViewportBackgroundProperty, value);
    }

    public double Zoom => zoom;

    public void ResetView()
    {
        zoom = 1;
        offset = default;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(
            ViewportBackground ?? Brushes.Black,
            null,
            bounds);
        if (Source is not { } source
            || bounds.Width <= 0
            || bounds.Height <= 0
            || source.Size.Width <= 0
            || source.Size.Height <= 0)
        {
            return;
        }

        var fit = Math.Min(
            bounds.Width / source.Size.Width,
            bounds.Height / source.Size.Height);
        var scale = fit * zoom;
        var width = source.Size.Width * scale;
        var height = source.Size.Height * scale;
        var destination = new Rect(
            bounds.Center.X - width / 2 + offset.X,
            bounds.Center.Y - height / 2 + offset.Y,
            width,
            height);
        context.DrawImage(
            source,
            new Rect(source.Size),
            destination);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs eventArgs)
    {
        base.OnPointerWheelChanged(eventArgs);
        if (Source is null || eventArgs.Delta.Y == 0)
        {
            return;
        }

        zoom = Math.Clamp(
            zoom * (eventArgs.Delta.Y > 0 ? 1.1 : 0.9),
            0.1,
            10);
        InvalidateVisual();
        eventArgs.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        base.OnPointerPressed(eventArgs);
        if (Source is null
            || !eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        dragOrigin = eventArgs.GetPosition(this);
        dragStartOffset = offset;
        eventArgs.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
        eventArgs.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs eventArgs)
    {
        base.OnPointerMoved(eventArgs);
        if (dragOrigin is not { } origin)
        {
            return;
        }

        var position = eventArgs.GetPosition(this);
        offset = dragStartOffset + (position - origin);
        InvalidateVisual();
        eventArgs.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs eventArgs)
    {
        base.OnPointerReleased(eventArgs);
        StopDragging(eventArgs.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs eventArgs)
    {
        base.OnPointerCaptureLost(eventArgs);
        StopDragging(null);
    }

    private void StopDragging(IPointer? pointer)
    {
        if (dragOrigin is null)
        {
            return;
        }

        dragOrigin = null;
        pointer?.Capture(null);
        Cursor = new Cursor(StandardCursorType.Hand);
    }
}
