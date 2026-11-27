using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace SrvSurvey.Desktop.Presentation;

/// <summary>Tints source-resolution PNGs so Image can filter the final application-scale transform.</summary>
public sealed class NavigationIconImageConverter : IMultiValueConverter
{
    private readonly Func<Uri, Stream> openAsset;
    private readonly ConditionalWeakTable<ISolidColorBrush, BrushImages> images = [];

    public NavigationIconImageConverter()
        : this(uri => AssetLoader.Open(uri)) { }

    internal NavigationIconImageConverter(Func<Uri, Stream> openAsset)
    {
        this.openAsset = openAsset;
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (
            values.Count != 4
            || values[0] is not string path
            || !Uri.TryCreate(path, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != "avares"
            || values[1] is not ISolidColorBrush brush
            || values[2] is not Color color
            || values[3] is not double opacity
        )
        {
            return AvaloniaProperty.UnsetValue;
        }

        var tint = Color.FromArgb((byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1)), color.R, color.G, color.B);
        BrushImages cache = images.GetValue(brush, _ => new BrushImages());
        lock (cache)
        {
            if (cache.Tint != tint)
            {
                cache.Bitmaps.Clear();
                cache.Tint = tint;
            }

            if (cache.Bitmaps.TryGetValue(path, out Bitmap? bitmap))
            {
                return bitmap;
            }

            try
            {
                bitmap = LoadTintedImage(uri, tint);
                cache.Bitmaps.Add(path, bitmap);
                return bitmap;
            }
            catch (Exception exception)
                when (exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException
                )
            {
                return AvaloniaProperty.UnsetValue;
            }
        }
    }

    private Bitmap LoadTintedImage(Uri uri, Color tint)
    {
        using Stream stream = openAsset(uri);
        using SKBitmap source = SKBitmap.Decode(stream) ?? throw new InvalidDataException();
        using var tinted = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(tinted);
        using var filter = SKColorFilter.CreateBlendMode(
            new SKColor(tint.R, tint.G, tint.B, tint.A),
            SKBlendMode.SrcIn
        );
        using var paint = new SKPaint { ColorFilter = filter };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, 0, 0, paint);
        return new Bitmap(
            PixelFormat.Bgra8888,
            AlphaFormat.Premul,
            tinted.GetPixels(),
            new PixelSize(tinted.Width, tinted.Height),
            new Vector(96, 96),
            tinted.RowBytes
        );
    }

    // Cache lifetime follows the foreground brush; changing a theme releases its unused bitmaps.
    private sealed class BrushImages
    {
        public Color Tint { get; set; }
        public Dictionary<string, Bitmap> Bitmaps { get; } = new(StringComparer.Ordinal);
    }
}
