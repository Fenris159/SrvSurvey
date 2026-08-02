using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class LegacyOverlayLayoutStore
{
    private static readonly ConcurrentDictionary<string, object> FileLocks = new(
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
    private readonly string dataDirectory;
    private readonly string plottersPath;
    private readonly string settingsPath;
    private readonly string scaleOverridesPath;
    private readonly object fileLock;

    public LegacyOverlayLayoutStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.dataDirectory = Path.GetFullPath(dataDirectory);
        plottersPath = Path.Combine(this.dataDirectory, "plotters.json");
        settingsPath = Path.Combine(this.dataDirectory, "settings.json");
        scaleOverridesPath = Path.Combine(
            this.dataDirectory,
            "overlay-scale-overrides.json");
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
        return Save(placements, 1d, updateDefaultOpacity: false);
    }

    public LegacyOverlayLayoutSaveResult Save(
        IReadOnlyDictionary<string, LegacyOverlayPlacement> placements,
        double defaultOpacity,
        bool updateDefaultOpacity)
    {
        ArgumentNullException.ThrowIfNull(placements);
        if (placements.Count == 0 && !updateDefaultOpacity)
        {
            throw new ArgumentException(
                "At least one overlay placement or a global opacity change is required.",
                nameof(placements));
        }

        if (updateDefaultOpacity
            && (!double.IsFinite(defaultOpacity)
                || defaultOpacity is < 0 or > 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultOpacity),
                "Global overlay opacity must be from 0 to 1.");
        }

        lock (fileLock)
        {
            var settingsExisted = File.Exists(settingsPath);
            var scaleOverridesExisted = File.Exists(scaleOverridesPath);
            string? settingsBackupPath = null;
            string? scaleOverridesBackupPath = null;
            var updatedScaleOverrideCount = 0;
            try
            {
                if (updateDefaultOpacity)
                {
                    settingsBackupPath = SaveDefaultOpacityCore(defaultOpacity);
                }

                (scaleOverridesBackupPath, updatedScaleOverrideCount) =
                    SaveScaleOverridesCore(placements);
                var result = placements.Count > 0
                    ? SaveCore(placements)
                    : new LegacyOverlayLayoutSaveResult(
                        settingsPath,
                        null,
                        0);
                return result with
                {
                    SettingsBackupPath = settingsBackupPath,
                    UpdatedDefaultOpacity = updateDefaultOpacity,
                    ScaleOverridesBackupPath = scaleOverridesBackupPath,
                    UpdatedScaleOverrideCount = updatedScaleOverrideCount,
                };
            }
            catch (Exception saveException)
            {
                try
                {
                    if (updateDefaultOpacity)
                    {
                        RestoreFile(
                            settingsPath,
                            settingsBackupPath,
                            settingsExisted);
                    }

                    if (updatedScaleOverrideCount > 0)
                    {
                        RestoreFile(
                            scaleOverridesPath,
                            scaleOverridesBackupPath,
                            scaleOverridesExisted);
                    }
                }
                catch (Exception rollbackException)
                {
                    throw new IOException(
                        "The overlay layout save failed and its settings rollback also failed.",
                        new AggregateException(saveException, rollbackException));
                }

                throw;
            }
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
        var scaleOverrides = LoadScaleOverrides(errors);
        foreach (var entry in scaleOverrides)
        {
            if (positions.TryGetValue(entry.Key, out var placement))
            {
                positions[entry.Key] = placement with
                {
                    ScaleIndex = entry.Value,
                };
            }
        }

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
                    || !HasSameDesktopPlacement(
                        ParsePlacement(entry.Key, text),
                        entry.Value))
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

    private string? SaveDefaultOpacityCore(double defaultOpacity)
    {
        var root = File.Exists(settingsPath)
            ? ParseObject(settingsPath)
            : [];
        root["plotterOpacity"] = defaultOpacity * 100d;

        Directory.CreateDirectory(dataDirectory);
        var backupPath = File.Exists(settingsPath)
            ? CreateVerifiedBackup(settingsPath, "settings")
            : null;
        var temporaryPath = $"{settingsPath}.{Guid.NewGuid():N}.tmp";
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
            if (verified["plotterOpacity"] is not JsonValue value
                || !value.TryGetValue<double>(out var percent)
                || !double.IsFinite(percent)
                || Math.Abs((percent / 100d) - defaultOpacity) > 0.0001d)
            {
                throw new InvalidDataException(
                    "Global overlay opacity could not be verified before saving.");
            }

            File.Move(temporaryPath, settingsPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return backupPath;
    }

    private (string? BackupPath, int UpdatedCount) SaveScaleOverridesCore(
        IReadOnlyDictionary<string, LegacyOverlayPlacement> placements)
    {
        if (placements.Count == 0)
        {
            return (null, 0);
        }

        var root = File.Exists(scaleOverridesPath)
            ? ParseObject(scaleOverridesPath)
            : [];
        var updatedCount = 0;
        foreach (var entry in placements)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key);
            ArgumentNullException.ThrowIfNull(entry.Value);
            var previous = ReadScaleOverride(root[entry.Key], entry.Key);
            if (previous == entry.Value.ScaleIndex)
            {
                continue;
            }

            updatedCount++;
            if (entry.Value.ScaleIndex is { } scaleIndex)
            {
                ValidateScaleIndex(entry.Key, scaleIndex);
                root[entry.Key] = scaleIndex;
            }
            else
            {
                root.Remove(entry.Key);
            }
        }

        if (updatedCount == 0)
        {
            return (null, 0);
        }

        Directory.CreateDirectory(dataDirectory);
        var backupPath = File.Exists(scaleOverridesPath)
            ? CreateVerifiedBackup(scaleOverridesPath, "overlay-scale-overrides")
            : null;
        var temporaryPath = $"{scaleOverridesPath}.{Guid.NewGuid():N}.tmp";
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
            foreach (var entry in placements)
            {
                var actual = ReadScaleOverride(verified[entry.Key], entry.Key);
                if (actual != entry.Value.ScaleIndex)
                {
                    throw new InvalidDataException(
                        $"Overlay scale override '{entry.Key}' could not be verified before saving.");
                }
            }

            File.Move(temporaryPath, scaleOverridesPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return (backupPath, updatedCount);
    }

    private string CreateVerifiedBackup()
    {
        return CreateVerifiedBackup(plottersPath, "plotters");
    }

    private string CreateVerifiedBackup(string sourcePath, string filePrefix)
    {
        var backupDirectory = Path.Combine(
            dataDirectory,
            "overlay-layout-backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(
            backupDirectory,
            $"{filePrefix}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.json");
        File.Copy(sourcePath, backupPath, false);

        if (!CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(File.ReadAllBytes(sourcePath)),
                SHA256.HashData(File.ReadAllBytes(backupPath))))
        {
            File.Delete(backupPath);
            throw new IOException(
                "The overlay layout backup did not match its source.");
        }

        return backupPath;
    }

    private static void RestoreFile(
        string path,
        string? backupPath,
        bool existed)
    {
        if (existed && backupPath is not null)
        {
            File.Copy(backupPath, path, true);
        }
        else if (!existed && File.Exists(path))
        {
            File.Delete(path);
        }
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

    private static bool HasSameDesktopPlacement(
        LegacyOverlayPlacement actual,
        LegacyOverlayPlacement expected)
    {
        return actual.Horizontal == expected.Horizontal
            && actual.HorizontalOffset == expected.HorizontalOffset
            && actual.Vertical == expected.Vertical
            && actual.VerticalOffset == expected.VerticalOffset
            && NullableOpacityEquals(actual.Opacity, expected.Opacity);
    }

    private static bool NullableOpacityEquals(double? left, double? right)
    {
        return left is null || right is null
            ? left == right
            : Math.Abs(left.Value - right.Value) <= 0.0000001d;
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

        if (placement.ScaleIndex is { } scaleIndex)
        {
            ValidateScaleIndex(name, scaleIndex);
        }
    }

    private Dictionary<string, int> LoadScaleOverrides(
        List<string> errors)
    {
        if (!File.Exists(scaleOverridesPath))
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        try
        {
            var root = ParseObject(scaleOverridesPath);
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in root)
            {
                var scaleIndex = ReadScaleOverride(entry.Value, entry.Key)
                    ?? throw new InvalidDataException(
                        $"Overlay scale override '{entry.Key}' must be an integer.");
                ValidateScaleIndex(entry.Key, scaleIndex);
                result[entry.Key] = scaleIndex;
            }

            return result;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidDataException
                or ArgumentException)
        {
            errors.Add(
                $"Could not read overlay scale overrides '{scaleOverridesPath}': "
                + exception.Message);
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    private static int? ReadScaleOverride(JsonNode? node, string name)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value
            && value.TryGetValue<int>(out var scaleIndex))
        {
            return scaleIndex;
        }

        throw new InvalidDataException(
            $"Overlay scale override '{name}' must be an integer.");
    }

    private static void ValidateScaleIndex(string name, int scaleIndex)
    {
        if (!OverlayScaleCatalog.IsSupported(scaleIndex))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scaleIndex),
                $"Overlay scale override '{name}' uses unsupported index {scaleIndex}.");
        }
    }

    private double? LoadDefaultOpacity(ICollection<string> errors)
    {
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
    private int scaleIndex;

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

    public event EventHandler? ScaleIndexChanged;

    public event EventHandler? Changed;

    public IReadOnlyDictionary<string, LegacyOverlayPlacement> Placements =>
        Volatile.Read(ref state).Placements;

    public double? DefaultOpacity => Volatile.Read(ref state).DefaultOpacity;

    public string? Error => Volatile.Read(ref state).Error;

    public int ScaleIndex => Volatile.Read(ref scaleIndex);

    public void SetScaleIndex(int index)
    {
        if (!OverlayScaleCatalog.IsSupported(index))
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                $"Overlay scale index {index} is not supported.");
        }

        if (Volatile.Read(ref scaleIndex) == index)
        {
            return;
        }

        Volatile.Write(ref scaleIndex, index);
        ScaleIndexChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

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
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool SetPlacement(
        string plotterName,
        LegacyOverlayPlacement placement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        ArgumentNullException.ThrowIfNull(placement);
        if (ReferenceEquals(this, Empty))
        {
            throw new InvalidOperationException(
                "The shared empty overlay layout cannot be changed.");
        }

        while (true)
        {
            var current = Volatile.Read(ref state);
            if (current.Placements.TryGetValue(plotterName, out var existing)
                && existing == placement)
            {
                return false;
            }

            var placements = new Dictionary<string, LegacyOverlayPlacement>(
                current.Placements,
                StringComparer.Ordinal)
            {
                [plotterName] = placement,
            };
            var updated = new LayoutState(
                placements,
                current.DefaultOpacity,
                current.Error);
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref state, updated, current),
                    current))
            {
                Changed?.Invoke(this, EventArgs.Empty);
                return true;
            }
        }
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

    public int GetScaleIndex(string plotterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        var snapshot = Volatile.Read(ref state);
        return snapshot.Placements.TryGetValue(plotterName, out var placement)
            && placement.ScaleIndex is { } placementScaleIndex
                ? placementScaleIndex
                : ScaleIndex;
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
    double? Opacity,
    int? ScaleIndex = null);

public sealed record LegacyOverlayLayoutSaveResult(
    string Path,
    string? BackupPath,
    int UpdatedPlacementCount)
{
    public string? SettingsBackupPath { get; init; }

    public bool UpdatedDefaultOpacity { get; init; }

    public string? ScaleOverridesBackupPath { get; init; }

    public int UpdatedScaleOverrideCount { get; init; }
}

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
