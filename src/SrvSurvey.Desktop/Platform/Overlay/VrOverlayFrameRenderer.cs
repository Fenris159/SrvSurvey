using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace SrvSurvey.Desktop.Platform.Overlay;

public static class VrOverlayFrameRenderer
{
    private const int MaximumFrameBytes = 256 * 1024 * 1024;

    public static VrOverlayFrame Render(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var scaling = window.RenderScaling;
        var pixelSize = PixelSize.FromSize(window.Bounds.Size, scaling);
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
        {
            throw new InvalidOperationException(
                "The overlay has no renderable pixel dimensions.");
        }

        using var bitmap = new RenderTargetBitmap(
            pixelSize,
            new Vector(96 * scaling, 96 * scaling));
        bitmap.Render(window);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return DecodePng(stream.ToArray());
    }

    public static VrOverlayFrame DecodePng(byte[] pngBytes)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        using var stream = new MemoryStream(pngBytes, writable: false);
        using var codec = SKCodec.Create(stream)
            ?? throw new InvalidDataException("The rendered VR frame is not a PNG image.");
        var info = new SKImageInfo(
            codec.Info.Width,
            codec.Info.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);
        var byteCount = checked((long)info.RowBytes * info.Height);
        if (byteCount <= 0 || byteCount > MaximumFrameBytes)
        {
            throw new InvalidDataException(
                "The rendered VR frame exceeds the 256 MiB safety limit.");
        }

        var pixels = new byte[(int)byteCount];
        var result = codec.GetPixels(info, pixels);
        if (result is not SKCodecResult.Success)
        {
            throw new InvalidDataException(
                $"The rendered VR frame could not be decoded: {result}.");
        }

        return new VrOverlayFrame(info.Width, info.Height, pixels);
    }
}

public sealed record VrOverlayFrame(
    int Width,
    int Height,
    byte[] RgbaBytes);
