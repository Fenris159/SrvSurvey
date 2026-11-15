using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SrvSurvey.Core.Discord;

namespace SrvSurvey.Desktop.Presentation;

/// <summary>A live crop preview that shares the original bitmap without encoding an image during dragging.</summary>
public sealed class CarrierCroppedImage : IImage
{
    private readonly Bitmap source;
    private readonly Rect cropArea;

    public CarrierCroppedImage(Bitmap source, DiscordCarrierImageCrop crop)
    {
        this.source = source;
        DiscordCarrierCropRectangle area = crop.GetRectangle(source.PixelSize.Width, source.PixelSize.Height);
        double horizontal = source.Size.Width / source.PixelSize.Width;
        double vertical = source.Size.Height / source.PixelSize.Height;
        cropArea = new Rect(area.X * horizontal, area.Y * vertical, area.Width * horizontal, area.Height * vertical);
    }

    public Size Size => new(DiscordCarrierImageCrop.OutputWidth, DiscordCarrierImageCrop.OutputHeight);

    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
    {
        double horizontal = cropArea.Width / Size.Width;
        double vertical = cropArea.Height / Size.Height;
        var requested = new Rect(
            cropArea.X + sourceRect.X * horizontal,
            cropArea.Y + sourceRect.Y * vertical,
            sourceRect.Width * horizontal,
            sourceRect.Height * vertical
        );
        context.DrawImage(source, requested, destRect);
    }
}
