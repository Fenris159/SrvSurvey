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

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Source is null || e.Delta.Y == 0)
        {
            return;
        }

        zoom = Math.Clamp(
            zoom * (e.Delta.Y > 0 ? 1.1 : 0.9),
            0.1,
            10);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Source is null
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        dragOrigin = e.GetPosition(this);
        dragStartOffset = offset;
        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragOrigin is not { } origin)
        {
            return;
        }

        var position = e.GetPosition(this);
        offset = dragStartOffset + (position - origin);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        StopDragging(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
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
