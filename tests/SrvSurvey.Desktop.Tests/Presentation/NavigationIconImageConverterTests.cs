using System.Globalization;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using SrvSurvey.Desktop.Presentation;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class NavigationIconImageConverterTests
{
    private const string Asset = "avares://SrvSurvey.Desktop/Assets/Navigation/overview.png";

    [AvaloniaFact]
    public void TintPreservesSourceAlphaAndFollowsForegroundColorAndOpacity()
    {
        var converter = new NavigationIconImageConverter(_ => CreateAlphaSample());
        var brush = new SolidColorBrush(Color.FromArgb(128, 255, 0, 0), 0.5);
        using Bitmap first = ConvertImage();
        Assert.Equal(new PixelSize(3, 1), first.PixelSize);
        Assert.Equal([new SKColor(0), new SKColor(255, 0, 0, 32), new SKColor(255, 0, 0, 64)], Pixels(first));
        Assert.Same(first, ConvertImage());

        brush.Opacity = 1;
        using Bitmap opaqueBrush = ConvertImage();
        Assert.Equal([new SKColor(0), new SKColor(255, 0, 0, 64), new SKColor(255, 0, 0, 128)], Pixels(opaqueBrush));

        brush.Color = Colors.Lime;
        using Bitmap changedTheme = ConvertImage();
        Assert.Equal([new SKColor(0), new SKColor(0, 255, 0, 128), SKColors.Lime], Pixels(changedTheme));

        Bitmap ConvertImage() =>
            Assert.IsType<Bitmap>(
                converter.Convert(
                    [Asset, brush, brush.Color, brush.Opacity],
                    typeof(IImage),
                    null,
                    CultureInfo.InvariantCulture
                )
            );
    }

    [AvaloniaFact]
    public void UnresolvedOrNonBundledBindingsHaveNoImage()
    {
        var converter = new NavigationIconImageConverter(_ =>
            throw new InvalidOperationException("Not a bundled asset.")
        );
        ISolidColorBrush brush = Brushes.White;
        object?[][] invalid =
        [
            [],
            [null, brush, Colors.White, 1d],
            [" ", brush, Colors.White, 1d],
            ["Assets/Navigation/overview.png", brush, Colors.White, 1d],
            ["https://example.com/icon.png", brush, Colors.White, 1d],
            [Asset, null, Colors.White, 1d],
            [Asset, brush, null, 1d],
            [Asset, brush, Colors.White, null],
        ];
        foreach (object?[] values in invalid)
        {
            Assert.Same(
                AvaloniaProperty.UnsetValue,
                converter.Convert(values, typeof(IImage), null, CultureInfo.InvariantCulture)
            );
        }
    }

    [AvaloniaFact]
    public void FailedLoadsCanRecoverWithoutCachingAnEmptyImage()
    {
        int attempt = 0;
        var converter = new NavigationIconImageConverter(_ =>
            ++attempt switch
            {
                1 => throw new IOException("Unavailable asset."),
                2 => new MemoryStream([1, 2, 3]),
                _ => CreateAlphaSample(),
            }
        );
        object?[] values = [Asset, Brushes.White, Colors.White, 1d];
        Assert.Same(AvaloniaProperty.UnsetValue, ConvertImage());
        Assert.Same(AvaloniaProperty.UnsetValue, ConvertImage());
        using Bitmap recovered = Assert.IsType<Bitmap>(ConvertImage());
        Assert.Equal([new SKColor(0), new SKColor(255, 255, 255, 128), SKColors.White], Pixels(recovered));
        Assert.Same(recovered, ConvertImage());

        object? ConvertImage() => converter.Convert(values, typeof(IImage), null, CultureInfo.InvariantCulture);
    }

    private static MemoryStream CreateAlphaSample()
    {
        using var bitmap = new SKBitmap(3, 1);
        bitmap.SetPixel(0, 0, SKColors.Transparent);
        bitmap.SetPixel(1, 0, new SKColor(255, 255, 255, 128));
        bitmap.SetPixel(2, 0, SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(png.ToArray());
    }

    private static SKColor[] Pixels(Bitmap image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        using var decoded = SKBitmap.Decode(stream);
        return decoded.Pixels;
    }
}
