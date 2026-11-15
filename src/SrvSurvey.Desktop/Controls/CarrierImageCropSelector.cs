using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SrvSurvey.Core.Discord;

namespace SrvSurvey.Desktop.Controls;

/// <summary>Moves an image beneath a fixed frame; the surrounding image stays visible under a dim mask.</summary>
public sealed class CarrierImageCropSelector : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty = AvaloniaProperty.Register<
        CarrierImageCropSelector,
        Bitmap?
    >(nameof(Source));
    public static readonly StyledProperty<DiscordCarrierImageCrop?> CropProperty = AvaloniaProperty.Register<
        CarrierImageCropSelector,
        DiscordCarrierImageCrop?
    >(nameof(Crop), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    private Point? dragOrigin;
    private DiscordCarrierImageCrop? dragCrop;

    static CarrierImageCropSelector() => AffectsRender<CarrierImageCropSelector>(SourceProperty, CropProperty);

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }
    public DiscordCarrierImageCrop? Crop
    {
        get => GetValue(CropProperty);
        set => SetValue(CropProperty, value);
    }

    private Rect Frame
    {
        get
        {
            double width = Math.Max(
                0,
                Math.Min(Bounds.Width - 32, (Bounds.Height - 32) * DiscordCarrierImageCrop.AspectRatio)
            );
            double height = width / DiscordCarrierImageCrop.AspectRatio;
            return new((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#1E1F22")), bounds);
        Rect frame = Frame;
        if (Source is not { } source || frame.Width <= 0)
        {
            return;
        }
        using (context.PushClip(bounds))
        {
            if (Crop is null)
            {
                double fit = Math.Min(bounds.Width / source.Size.Width, bounds.Height / source.Size.Height);
                Size size = source.Size * fit;
                context.DrawImage(
                    source,
                    new Rect(
                        (bounds.Width - size.Width) / 2,
                        (bounds.Height - size.Height) / 2,
                        size.Width,
                        size.Height
                    )
                );
                return;
            }
            DiscordCarrierCropRectangle area = Crop.GetRectangle(source.PixelSize.Width, source.PixelSize.Height);
            double scale = frame.Width / area.Width;
            context.DrawImage(
                source,
                new Rect(
                    frame.X - area.X * scale,
                    frame.Y - area.Y * scale,
                    source.PixelSize.Width * scale,
                    source.PixelSize.Height * scale
                )
            );
            var shade = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));
            context.FillRectangle(shade, new Rect(0, 0, bounds.Width, frame.Top));
            context.FillRectangle(shade, new Rect(0, frame.Bottom, bounds.Width, bounds.Height - frame.Bottom));
            context.FillRectangle(shade, new Rect(0, frame.Top, frame.Left, frame.Height));
            context.FillRectangle(shade, new Rect(frame.Right, frame.Top, bounds.Width - frame.Right, frame.Height));
            var grid = new Pen(new SolidColorBrush(Color.FromArgb(85, 255, 255, 255)), 1);
            for (int index = 1; index <= 2; index++)
            {
                double x = frame.Left + frame.Width * index / 3;
                double y = frame.Top + frame.Height * index / 3;
                context.DrawLine(grid, new Point(x, frame.Top), new Point(x, frame.Bottom));
                context.DrawLine(grid, new Point(frame.Left, y), new Point(frame.Right, y));
            }
            context.DrawRectangle(null, new Pen(Brushes.White, 2), frame);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Source is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        Focus();
        dragOrigin = e.GetPosition(this);
        dragCrop = (Crop ?? new()).Constrain(Source.PixelSize.Width, Source.PixelSize.Height);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragOrigin is { } origin && dragCrop is { } crop && Source is { } source && Frame.Width > 0)
        {
            Vector delta = e.GetPosition(this) - origin;
            DiscordCarrierCropRectangle area = crop.GetRectangle(source.PixelSize.Width, source.PixelSize.Height);
            double scale = Frame.Width / area.Width;
            SetCurrentValue(
                CropProperty,
                (
                    crop with
                    {
                        CenterX = crop.CenterX - delta.X / (scale * source.PixelSize.Width),
                        CenterY = crop.CenterY - delta.Y / (scale * source.PixelSize.Height),
                    }
                ).Constrain(source.PixelSize.Width, source.PixelSize.Height)
            );
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        dragOrigin = null;
        dragCrop = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        dragOrigin = null;
        dragCrop = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Source is null || e.Delta.Y == 0)
        {
            return;
        }
        SetCurrentValue(
            CropProperty,
            ((Crop ?? new()) with { Zoom = (Crop?.EffectiveZoom ?? 1) * Math.Pow(1.1, e.Delta.Y) }).Constrain(
                Source.PixelSize.Width,
                Source.PixelSize.Height
            )
        );
        if (dragOrigin is not null)
        {
            dragOrigin = e.GetPosition(this);
            dragCrop = Crop;
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        Vector delta = e.Key switch
        {
            Key.Left => new(-0.01, 0),
            Key.Right => new(0.01, 0),
            Key.Up => new(0, -0.01),
            Key.Down => new(0, 0.01),
            _ => default,
        };
        if (Source is { } source && delta != default)
        {
            DiscordCarrierImageCrop crop = Crop ?? new();
            SetCurrentValue(
                CropProperty,
                (crop with { CenterX = crop.CenterX + delta.X, CenterY = crop.CenterY + delta.Y }).Constrain(
                    source.PixelSize.Width,
                    source.PixelSize.Height
                )
            );
            e.Handled = true;
        }
    }
}
