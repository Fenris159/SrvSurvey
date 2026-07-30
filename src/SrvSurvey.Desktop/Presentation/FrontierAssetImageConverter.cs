using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SrvSurvey.Desktop.Presentation;

public sealed class FrontierAssetImageConverter : IValueConverter
{
    private readonly Func<Uri, Stream> openAsset;
    private readonly ConcurrentDictionary<string, Bitmap> images =
        new(StringComparer.Ordinal);

    public FrontierAssetImageConverter()
        : this(uri => AssetLoader.Open(uri))
    {
    }

    internal FrontierAssetImageConverter(Func<Uri, Stream> openAsset)
    {
        this.openAsset = openAsset ?? throw new ArgumentNullException(nameof(openAsset));
    }

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is not string path
            || !Uri.TryCreate(path, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "avares", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return images.GetOrAdd(path, _ =>
            {
                using var stream = openAsset(uri);
                return new Bitmap(stream);
            });
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidOperationException
                or ArgumentException)
        {
            return AvaloniaProperty.UnsetValue;
        }
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
