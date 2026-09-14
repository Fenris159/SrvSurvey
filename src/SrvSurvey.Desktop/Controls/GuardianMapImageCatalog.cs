using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Desktop.Controls;

internal static class GuardianMapImageCatalog
{
    private const string ResourceRoot = "avares://SrvSurvey.Desktop/Assets/GuardianMaps/";
    private static readonly Dictionary<string, Bitmap?> Images = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock SyncRoot = new();

    public static IImage? Find(GuardianSiteMapProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (TryResolveLocalFile(projection.BackgroundImage) is { } localPath)
        {
            return FindCached("file:" + localPath, () => LoadFile(localPath));
        }

        string fileName = ResolveFileName(projection);
        return FindCached("asset:" + fileName, () => LoadAsset(fileName));
    }

    private static Bitmap? FindCached(string key, Func<Bitmap?> load)
    {
        lock (SyncRoot)
        {
            if (!Images.TryGetValue(key, out Bitmap? image))
            {
                image = load();
                Images[key] = image;
            }

            return image;
        }
    }

    internal static string ResolveFileName(GuardianSiteMapProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        string configuredName = Path.GetFileName(projection.BackgroundImage);
        return string.IsNullOrWhiteSpace(configuredName)
            ? $"{projection.SiteType.ToLowerInvariant()}-background.png"
            : configuredName;
    }

    private static string? TryResolveLocalFile(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath) || !Path.IsPathRooted(configuredPath))
        {
            return null;
        }

        try
        {
            string path = Path.GetFullPath(configuredPath);
            return File.Exists(path) ? path : null;
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static Bitmap? LoadAsset(string fileName)
    {
        try
        {
            var uri = new Uri(ResourceRoot + Uri.EscapeDataString(fileName));
            if (!AssetLoader.Exists(uri))
            {
                return null;
            }

            using Stream stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static Bitmap? LoadFile(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
