using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class LegacyOverlayLayoutStore
{
    private readonly string dataDirectory;

    public LegacyOverlayLayoutStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.dataDirectory = Path.GetFullPath(dataDirectory);
    }

    public LegacyOverlayLayout Load()
    {
        var positions = new Dictionary<string, LegacyOverlayPlacement>(
            StringComparer.Ordinal);
        var errors = new List<string>();
        var plottersPath = Path.Combine(dataDirectory, "plotters.json");
        if (File.Exists(plottersPath))
        {
            try
            {
                var root = ParseObject(plottersPath);
                foreach (var entry in root)
                {
                    if (entry.Value is not JsonValue value
                        || !value.TryGetValue<string>(out var text))
                    {
                        throw new InvalidDataException(
                            $"Overlay position '{entry.Key}' must be a string.");
                    }

                    positions[entry.Key] = ParsePlacement(entry.Key, text);
                }
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or JsonException
                    or InvalidDataException
                    or FormatException
                    or OverflowException)
            {
                positions.Clear();
                errors.Add(
                    $"Could not read legacy overlay layout '{plottersPath}': "
                    + exception.Message);
            }
        }

        var defaultOpacity = LoadDefaultOpacity(errors);
        return new LegacyOverlayLayout(
            positions,
            defaultOpacity,
            errors.Count == 0 ? null : string.Join(" ", errors));
    }

    private double? LoadDefaultOpacity(ICollection<string> errors)
    {
        var settingsPath = Path.Combine(dataDirectory, "settings.json");
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        try
        {
            var root = ParseObject(settingsPath);
            if (root["plotterOpacity"] is not JsonValue opacity)
            {
                return null;
            }

            if (!opacity.TryGetValue<double>(out var percent)
                || !double.IsFinite(percent))
            {
                throw new InvalidDataException(
                    "Legacy plotterOpacity must be a finite number.");
            }

            return Math.Clamp(percent / 100d, 0, 1);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidDataException)
        {
            errors.Add(
                $"Could not read legacy overlay opacity '{settingsPath}': "
                + exception.Message);
            return null;
        }
    }

    private static JsonObject ParseObject(string path)
    {
        return JsonNode.Parse(
                File.ReadAllText(path),
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                })
            as JsonObject
            ?? throw new InvalidDataException(
                $"'{path}' is not a JSON object.");
    }

    private static LegacyOverlayPlacement ParsePlacement(
        string name,
        string value)
    {
        var vrStart = value.IndexOf('{');
        var desktop = vrStart >= 0 ? value[..vrStart] : value;
        var parts = desktop.Split(
            [':', ','],
            StringSplitOptions.TrimEntries
                | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 4 or > 5)
        {
            throw new InvalidDataException(
                $"Overlay position '{name}' has an invalid desktop layout.");
        }

        var horizontal = ParseHorizontal(name, parts[0]);
        var horizontalOffset = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var vertical = ParseVertical(name, parts[2]);
        var verticalOffset = int.Parse(parts[3], CultureInfo.InvariantCulture);
        double? opacity = null;
        if (parts.Length == 5)
        {
            opacity = ParseOpacity(name, parts[4]);
        }

        return new LegacyOverlayPlacement(
            horizontal,
            horizontalOffset,
            vertical,
            verticalOffset,
            opacity);
    }

    private static LegacyHorizontalAnchor ParseHorizontal(
        string name,
        string value)
    {
        return value.ToLowerInvariant() switch
        {
            "left" => LegacyHorizontalAnchor.Left,
            "center" => LegacyHorizontalAnchor.Center,
            "right" => LegacyHorizontalAnchor.Right,
            "screen" or "os" => LegacyHorizontalAnchor.Screen,
            _ => throw new InvalidDataException(
                $"Overlay position '{name}' has unknown horizontal anchor '{value}'."),
        };
    }

    private static LegacyVerticalAnchor ParseVertical(
        string name,
        string value)
    {
        return value.ToLowerInvariant() switch
        {
            "top" => LegacyVerticalAnchor.Top,
            "middle" => LegacyVerticalAnchor.Middle,
            "bottom" => LegacyVerticalAnchor.Bottom,
            "screen" or "os" => LegacyVerticalAnchor.Screen,
            _ => throw new InvalidDataException(
                $"Overlay position '{name}' has unknown vertical anchor '{value}'."),
        };
    }

    private static double ParseOpacity(string name, string value)
    {
        if ((!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out var opacity)
             && !double.TryParse(
                 value,
                 NumberStyles.Float,
                 CultureInfo.InvariantCulture,
                 out opacity))
            || !double.IsFinite(opacity)
            || opacity is < 0 or > 1)
        {
            throw new InvalidDataException(
                $"Overlay position '{name}' opacity must be from 0 to 1.");
        }

        return opacity;
    }
}

public sealed record LegacyOverlayLayout(
    IReadOnlyDictionary<string, LegacyOverlayPlacement> Placements,
    double? DefaultOpacity,
    string? Error)
{
    public static LegacyOverlayLayout Empty { get; } = new(
        new Dictionary<string, LegacyOverlayPlacement>(StringComparer.Ordinal),
        null,
        null);

    public PixelPoint? GetPosition(
        string plotterName,
        PixelRect gameBounds,
        PixelSize overlaySize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        if (!Placements.TryGetValue(plotterName, out var placement))
        {
            return null;
        }

        var x = placement.Horizontal switch
        {
            LegacyHorizontalAnchor.Left =>
                gameBounds.X + placement.HorizontalOffset,
            LegacyHorizontalAnchor.Center =>
                gameBounds.X + ((gameBounds.Width - overlaySize.Width) / 2)
                    + placement.HorizontalOffset,
            LegacyHorizontalAnchor.Right =>
                gameBounds.Right - overlaySize.Width
                    - placement.HorizontalOffset,
            _ => placement.HorizontalOffset,
        };
        var y = placement.Vertical switch
        {
            LegacyVerticalAnchor.Top =>
                gameBounds.Y + placement.VerticalOffset,
            LegacyVerticalAnchor.Middle =>
                gameBounds.Y + ((gameBounds.Height - overlaySize.Height) / 2)
                    + placement.VerticalOffset,
            LegacyVerticalAnchor.Bottom =>
                gameBounds.Bottom - overlaySize.Height
                    - placement.VerticalOffset,
            _ => placement.VerticalOffset,
        };
        return new PixelPoint(x, y);
    }

    public double? GetOpacity(string plotterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        return Placements.TryGetValue(plotterName, out var placement)
            && placement.Opacity is not null
                ? placement.Opacity
                : DefaultOpacity;
    }
}

public sealed record LegacyOverlayPlacement(
    LegacyHorizontalAnchor Horizontal,
    int HorizontalOffset,
    LegacyVerticalAnchor Vertical,
    int VerticalOffset,
    double? Opacity);

public enum LegacyHorizontalAnchor
{
    Left,
    Center,
    Right,
    Screen,
}

public enum LegacyVerticalAnchor
{
    Top,
    Middle,
    Bottom,
    Screen,
}
