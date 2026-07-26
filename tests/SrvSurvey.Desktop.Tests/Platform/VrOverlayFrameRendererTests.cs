using SkiaSharp;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class VrOverlayFrameRendererTests
{
    [Fact]
    public void PngIsDecodedToExactUnpremultipliedRgbaBytes()
    {
        using var bitmap = new SKBitmap(
            new SKImageInfo(
                1,
                1,
                SKColorType.Rgba8888,
                SKAlphaType.Unpremul));
        bitmap.SetPixel(0, 0, new SKColor(10, 20, 30, 40));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        var frame = VrOverlayFrameRenderer.DecodePng(encoded.ToArray());

        Assert.Equal(1, frame.Width);
        Assert.Equal(1, frame.Height);
        Assert.Equal([10, 20, 30, 40], frame.RgbaBytes);
    }

    [Fact]
    public void TruncatedPngIsRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            VrOverlayFrameRenderer.DecodePng([137, 80, 78, 71]));
    }
}
