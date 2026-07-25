using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class LegacyOverlayLayoutStore
{
    private static readonly ConcurrentDictionary<string, object> FileLocks = new(
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
    private readonly string dataDirectory;
    private readonly string plottersPath;
    private readonly object fileLock;

    public LegacyOverlayLayoutStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.dataDirectory = Path.GetFullPath(dataDirectory);
        plottersPath = Path.Combine(this.dataDirectory, "plotters.json");
        fileLock = FileLocks.GetOrAdd(plottersPath, _ => new object());
    }

    public LegacyOverlayLayout Load()
    {
        lock (fileLock)
        {
            return LoadCore();
        }
    }

    public LegacyOverlayLayoutSaveResult Save(
        IReadOnlyDictionary<string, LegacyOverlayPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);
        if (placements.Count == 0)
        {
            throw new ArgumentException(
                "At least one overlay placement is required.",
                nameof(placements));
        }

        lock (fileLock)
        {
            return SaveCore(placements);
        }
    }

    private LegacyOverlayLayout LoadCore()
    {
        var positions = new Dictionary<string, LegacyOverlayPlacement>(
            StringComparer.Ordinal);
        var errors = new List<string>();
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

    private LegacyOverlayLayoutSaveResult SaveCore(
        IReadOnlyDictionary<string, LegacyOverlayPlacement> placements)
    {
        var root = File.Exists(plottersPath)
            ? ParseObject(plottersPath)
            : [];

        ValidateExistingPlacements(root);
        foreach (var entry in placements)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key);
            ArgumentNullException.ThrowIfNull(entry.Value);
            ValidatePlacement(entry.Key, entry.Value);

            var suffix = GetVrSuffix(root[entry.Key]);
            root[entry.Key] = FormatPlacement(entry.Value) + suffix;
        }

        Directory.CreateDirectory(dataDirectory);
        var backupPath = File.Exists(plottersPath)
            ? CreateVerifiedBackup()
            : null;
        var temporaryPath = $"{plottersPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new Utf8JsonWriter(
                       stream,
                       new JsonWriterOptions
                       {
                           Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                           Indented = true,
                       }))
            {
                root.WriteTo(writer);
            }

            var verified = ParseObject(temporaryPath);
            ValidateExistingPlacements(verified);
            foreach (var entry in placements)
            {
                if (verified[entry.Key] is not JsonValue value
                    || !value.TryGetValue<string>(out var text)
                    || ParsePlacement(entry.Key, text) != entry.Value)
                {
                    throw new InvalidDataException(
                        $"Overlay position '{entry.Key}' could not be verified before saving.");
                }
            }

            File.Move(temporaryPath, plottersPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return new LegacyOverlayLayoutSaveResult(
            plottersPath,
            backupPath,
            placements.Count);
    }

    private string CreateVerifiedBackup()
    {
        var backupDirectory = Path.Combine(
            dataDirectory,
            "overlay-layout-backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(
            backupDirectory,
            $"plotters-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.json");
        File.Copy(plottersPath, backupPath, false);

        if (!CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(File.ReadAllBytes(plottersPath)),
                SHA256.HashData(File.ReadAllBytes(backupPath))))
        {
            File.Delete(backupPath);
            throw new IOException(
                "The overlay layout backup did not match its source.");
        }

        return backupPath;
    }

    private static void ValidateExistingPlacements(JsonObject root)
    {
        foreach (var entry in root)
        {
            if (entry.Value is not JsonValue value
                || !value.TryGetValue<string>(out var text))
            {
                throw new InvalidDataException(
                    $"Overlay position '{entry.Key}' must be a string.");
            }

            _ = ParsePlacement(entry.Key, text);
        }
    }

    private static string GetVrSuffix(JsonNode? node)
    {
        if (node is not JsonValue value
            || !value.TryGetValue<string>(out var text))
        {
            return string.Empty;
        }

        var start = text.IndexOf('{');
        return start >= 0 ? " " + text[start..].TrimStart() : string.Empty;
    }

    private static string FormatPlacement(LegacyOverlayPlacement placement)
    {
        var horizontal = placement.Horizontal switch
        {
            LegacyHorizontalAnchor.Left => "left",
            LegacyHorizontalAnchor.Center => "center",
            LegacyHorizontalAnchor.Right => "right",
            _ => "os",
        };
        var vertical = placement.Vertical switch
        {
            LegacyVerticalAnchor.Top => "top",
            LegacyVerticalAnchor.Middle => "middle",
            LegacyVerticalAnchor.Bottom => "bottom",
            _ => "os",
        };
        var opacity = placement.Opacity is null
            ? string.Empty
            : ", " + placement.Opacity.Value.ToString(
                "0.################",
                CultureInfo.InvariantCulture);
        return $"{horizontal}:{placement.HorizontalOffset}, "
            + $"{vertical}:{placement.VerticalOffset}{opacity}";
    }

    private static void ValidatePlacement(
        string name,
        LegacyOverlayPlacement placement)
    {
        if (placement.Opacity is not null
            && (!double.IsFinite(placement.Opacity.Value)
                || placement.Opacity.Value is < 0 or > 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(placement),
                $"Overlay position '{name}' opacity must be from 0 to 1.");
        }
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

public sealed class LegacyOverlayLayout
{
    private LayoutState state;

    public LegacyOverlayLayout(
        IReadOnlyDictionary<string, LegacyOverlayPlacement> placements,
        double? defaultOpacity,
        string? error)
    {
        ArgumentNullException.ThrowIfNull(placements);
        state = new LayoutState(
            new Dictionary<string, LegacyOverlayPlacement>(
                placements,
                StringComparer.Ordinal),
            defaultOpacity,
            error);
    }

    public static LegacyOverlayLayout Empty { get; } = new(
        new Dictionary<string, LegacyOverlayPlacement>(StringComparer.Ordinal),
        null,
        null);

    public IReadOnlyDictionary<string, LegacyOverlayPlacement> Placements =>
        Volatile.Read(ref state).Placements;

    public double? DefaultOpacity => Volatile.Read(ref state).DefaultOpacity;

    public string? Error => Volatile.Read(ref state).Error;

    public void ReplaceWith(LegacyOverlayLayout updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        if (ReferenceEquals(this, Empty))
        {
            throw new InvalidOperationException(
                "The shared empty overlay layout cannot be changed.");
        }

        var updatedState = Volatile.Read(ref updated.state);
        Volatile.Write(
            ref state,
            new LayoutState(
                new Dictionary<string, LegacyOverlayPlacement>(
                    updatedState.Placements,
                    StringComparer.Ordinal),
                updatedState.DefaultOpacity,
                updatedState.Error));
    }

    public PixelPoint? GetPosition(
        string plotterName,
        PixelRect gameBounds,
        PixelSize overlaySize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        var snapshot = Volatile.Read(ref state);
        if (!snapshot.Placements.TryGetValue(plotterName, out var placement))
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
        var snapshot = Volatile.Read(ref state);
        return snapshot.Placements.TryGetValue(plotterName, out var placement)
            && placement.Opacity is not null
                ? placement.Opacity
                : snapshot.DefaultOpacity;
    }

    private sealed record LayoutState(
        IReadOnlyDictionary<string, LegacyOverlayPlacement> Placements,
        double? DefaultOpacity,
        string? Error);
}

public sealed record LegacyOverlayPlacement(
    LegacyHorizontalAnchor Horizontal,
    int HorizontalOffset,
    LegacyVerticalAnchor Vertical,
    int VerticalOffset,
    double? Opacity);

public sealed record LegacyOverlayLayoutSaveResult(
    string Path,
    string? BackupPath,
    int UpdatedPlacementCount);

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
