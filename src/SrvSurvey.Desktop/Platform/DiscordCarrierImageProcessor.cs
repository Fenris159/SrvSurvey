using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using SrvSurvey.Core.Discord;

namespace SrvSurvey.Desktop.Platform;

/// <summary>Bounds native decoding and encodes the same fixed frame shown in the editor.</summary>
public static class DiscordCarrierImageProcessor
{
    private const string AllocationError = "The carrier image could not be allocated.";
    public const int MaximumPixels = 16_000_000;
    public const int MaximumDimension = 16384;

    public static Bitmap CreatePreview(DiscordCarrierImage original)
    {
        try
        {
            using SKBitmap decoded = Decode(original, AllocateBitmap);
            return new Bitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Premul,
                decoded.GetPixels(),
                new PixelSize(decoded.Width, decoded.Height),
                new Vector(96, 96),
                decoded.RowBytes
            );
        }
        catch (InvalidOperationException exception) when (IsNativeAllocationFailure(exception))
        {
            throw new InvalidDataException(AllocationError, exception);
        }
    }

    public static DiscordCarrierImage Crop(DiscordCarrierImage original, DiscordCarrierImageCrop crop) =>
        Crop(original, crop, AllocateBitmap, image => image.Encode(SKEncodedImageFormat.Png, 100));

    internal static DiscordCarrierImage Crop(
        DiscordCarrierImage original,
        DiscordCarrierImageCrop crop,
        Func<SKImageInfo, SKBitmap?> allocate,
        Func<SKImage, SKData?> encode
    )
    {
        try
        {
            return EncodeCrop(original, crop, allocate, encode);
        }
        catch (InvalidOperationException exception) when (IsNativeAllocationFailure(exception))
        {
            throw new InvalidDataException(AllocationError, exception);
        }
    }

    private static DiscordCarrierImage EncodeCrop(
        DiscordCarrierImage original,
        DiscordCarrierImageCrop crop,
        Func<SKImageInfo, SKBitmap?> allocate,
        Func<SKImage, SKData?> encode
    )
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(crop);
        using SKBitmap source = Decode(original, allocate);
        DiscordCarrierCropRectangle area = crop.GetRectangle(source.Width, source.Height);
        using SKBitmap output =
            allocate(
                new SKImageInfo(
                    DiscordCarrierImageCrop.OutputWidth,
                    DiscordCarrierImageCrop.OutputHeight,
                    SKColorType.Bgra8888,
                    SKAlphaType.Premul
                )
            ) ?? throw new InvalidDataException(AllocationError);
        using (var canvas = new SKCanvas(output))
        using (
            SKImage sourceImage =
                SKImage.FromBitmap(source) ?? throw new InvalidDataException("The carrier image could not be rendered.")
        )
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(
                sourceImage,
                new SKRect((float)area.X, (float)area.Y, (float)(area.X + area.Width), (float)(area.Y + area.Height)),
                new SKRect(0, 0, DiscordCarrierImageCrop.OutputWidth, DiscordCarrierImageCrop.OutputHeight),
                new SKSamplingOptions(SKFilterMode.Linear)
            );
        }
        using SKImage encoded =
            SKImage.FromBitmap(output) ?? throw new InvalidDataException("The carrier image could not be rendered.");
        using SKData data =
            encode(encoded) ?? throw new InvalidDataException("The carrier image could not be encoded.");
        return new(data.ToArray(), "carrier.png", "image/png");
    }

    private static SKBitmap Decode(DiscordCarrierImage original, Func<SKImageInfo, SKBitmap?> allocate)
    {
        using var stream = new MemoryStream(original.Bytes, writable: false);
        using SKCodec codec =
            SKCodec.Create(stream) ?? throw new InvalidDataException("The carrier image could not be decoded.");
        SKImageInfo info = codec.Info;
        if (
            info.Width is <= 0 or > MaximumDimension
            || info.Height is <= 0 or > MaximumDimension
            || (long)info.Width * info.Height > MaximumPixels
        )
        {
            throw new InvalidDataException("Choose an image up to 16 megapixels and 16,384 pixels per side.");
        }
        info = info.WithColorType(SKColorType.Bgra8888).WithAlphaType(SKAlphaType.Premul);
        SKBitmap? bitmap = allocate(info) ?? throw new InvalidDataException(AllocationError);
        try
        {
            if (
                codec.GetPixels(info, bitmap.GetPixels())
                is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)
            )
            {
                throw new InvalidDataException("The carrier image could not be decoded.");
            }
            bitmap.SetImmutable();
            SKBitmap result = bitmap;
            bitmap = null;
            return result;
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    private static SKBitmap? AllocateBitmap(SKImageInfo info)
    {
        var bitmap = new SKBitmap();
        if (bitmap.TryAllocPixels(info))
        {
            return bitmap;
        }
        bitmap.Dispose();
        return null;
    }

    private static bool IsNativeAllocationFailure(InvalidOperationException exception) =>
        exception.Message == "Unable to create a new SKBitmap instance.";
}
