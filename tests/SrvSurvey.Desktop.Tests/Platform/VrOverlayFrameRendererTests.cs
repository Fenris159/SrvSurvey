using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.Infrastructure;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class VrOverlayFrameRendererTests
{
    [AvaloniaFact]
    public void RenderPreservesLogicalPointerDimensions()
    {
        var visual = new Border { Width = 25, Height = 10 };

        VrOverlayFrame frame = VrOverlayFrameRenderer.Render(visual, new Size(25, 10), scaling: 2);

        Assert.Equal(50, frame.Width);
        Assert.Equal(20, frame.Height);
        Assert.Equal(25, frame.PointerWidth);
        Assert.Equal(10, frame.PointerHeight);
    }

    [Fact]
    public void PngIsDecodedToExactUnpremultipliedRgbaBytes()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.SetPixel(0, 0, new SKColor(10, 20, 30, 40));
        using var image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        VrOverlayFrame frame = VrOverlayFrameRenderer.DecodePng(encoded.ToArray());

        Assert.Equal(1, frame.Width);
        Assert.Equal(1, frame.Height);
        Assert.Equal([10, 20, 30, 40], frame.RgbaBytes);
    }

    [Fact]
    public void TruncatedPngIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => VrOverlayFrameRenderer.DecodePng([137, 80, 78, 71]));
    }
}
