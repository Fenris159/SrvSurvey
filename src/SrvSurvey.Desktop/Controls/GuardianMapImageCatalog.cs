using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Desktop.Controls;

internal static class GuardianMapImageCatalog
{
    private const string ResourceRoot =
        "avares://SrvSurvey.Desktop/Assets/GuardianMaps/";
    private static readonly Dictionary<string, Bitmap?> Images =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object SyncRoot = new();

    public static IImage? Find(GuardianSiteMapProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var fileName = ResolveFileName(projection);
        lock (SyncRoot)
        {
            if (!Images.TryGetValue(fileName, out var image))
            {
                image = Load(fileName);
                Images[fileName] = image;
            }

            return image;
        }
    }

    internal static string ResolveFileName(
        GuardianSiteMapProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var configuredName = Path.GetFileName(projection.BackgroundImage);
        return string.IsNullOrWhiteSpace(configuredName)
            ? $"{projection.SiteType.ToLowerInvariant()}-background.png"
            : configuredName;
    }

    private static Bitmap? Load(string fileName)
    {
        try
        {
            var uri = new Uri(ResourceRoot + Uri.EscapeDataString(fileName));
            if (!AssetLoader.Exists(uri))
            {
                return null;
            }

            using var stream = AssetLoader.Open(uri);
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
}
