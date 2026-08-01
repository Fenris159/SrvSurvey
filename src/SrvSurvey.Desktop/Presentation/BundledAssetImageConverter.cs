using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SrvSurvey.Desktop.Presentation;

/// <summary>
/// Loads and caches a bundled Avalonia bitmap referenced by an avares URI.
/// </summary>
public sealed class BundledAssetImageConverter : IValueConverter
{
    private readonly Func<Uri, Stream> openAsset;
    private readonly Func<Stream, object> decodeAsset;
    private readonly ConcurrentDictionary<string, object> images =
        new(StringComparer.Ordinal);

    public BundledAssetImageConverter()
        : this(uri => AssetLoader.Open(uri), stream => new Bitmap(stream))
    {
    }

    internal BundledAssetImageConverter(
        Func<Uri, Stream> openAsset,
        Func<Stream, object> decodeAsset)
    {
        this.openAsset = openAsset ?? throw new ArgumentNullException(nameof(openAsset));
        this.decodeAsset = decodeAsset
            ?? throw new ArgumentNullException(nameof(decodeAsset));
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
                return decodeAsset(stream);
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
