using SkiaSharp;
using SrvSurvey.Core.Discord;

namespace SrvSurvey.Desktop.Platform;

/// <summary>Encodes the same fixed frame shown in the editor as the actual Discord attachment.</summary>
public static class DiscordCarrierImageProcessor
{
    public static DiscordCarrierImage Crop(DiscordCarrierImage original, DiscordCarrierImageCrop crop)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(crop);
        using SKBitmap source =
            SKBitmap.Decode(original.Bytes)
            ?? throw new InvalidDataException("The carrier image could not be decoded.");
        DiscordCarrierCropRectangle area = crop.GetRectangle(source.Width, source.Height);
        using var output = new SKBitmap(
            DiscordCarrierImageCrop.OutputWidth,
            DiscordCarrierImageCrop.OutputHeight,
            SKColorType.Bgra8888,
            SKAlphaType.Premul
        );
        using (var canvas = new SKCanvas(output))
        using (var sourceImage = SKImage.FromBitmap(source))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(
                sourceImage,
                new SKRect((float)area.X, (float)area.Y, (float)(area.X + area.Width), (float)(area.Y + area.Height)),
                new SKRect(0, 0, DiscordCarrierImageCrop.OutputWidth, DiscordCarrierImageCrop.OutputHeight),
                new SKSamplingOptions(SKFilterMode.Linear)
            );
        }
        using var encoded = SKImage.FromBitmap(output);
        using SKData data = encoded.Encode(SKEncodedImageFormat.Png, 100);
        return new(data.ToArray(), "carrier.png", "image/png");
    }
}
