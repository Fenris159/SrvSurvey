using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Media;

namespace SrvSurvey.Desktop.Theming;

public sealed class OverlayThemeStateStore
{
    private static readonly ConcurrentDictionary<string, object> FileLocks = new(
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
    private readonly string path;
    private readonly object fileLock;

    public OverlayThemeStateStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
        fileLock = FileLocks.GetOrAdd(this.path, _ => new object());
    }

    public OverlayThemeStateCollection Load()
    {
        lock (fileLock)
        {
            return LoadCore();
        }
    }

    public OverlayThemeStateSaveResult SaveState(
        string name,
        IReadOnlyDictionary<string, Color> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var normalizedName = NormalizeName(name);
        lock (fileLock)
        {
            var current = LoadCore();
            if (current.Error is not null)
            {
                throw new InvalidDataException(current.Error);
            }

            var states = current.States.ToList();
            var existingIndex = states.FindIndex(state => string.Equals(
                state.Name,
                normalizedName,
                StringComparison.OrdinalIgnoreCase));
            var updated = new OverlayThemeState(
                normalizedName,
                new Dictionary<string, Color>(colors, StringComparer.Ordinal));
            if (existingIndex >= 0)
            {
                states[existingIndex] = updated;
            }
            else
            {
                states.Add(updated);
            }

            var backupPath = Write(states);
            return new OverlayThemeStateSaveResult(
                path,
                backupPath,
                updated.Name,
                existingIndex >= 0);
        }
    }

    public OverlayThemeStateSaveResult DeleteState(string name)
    {
        var normalizedName = NormalizeName(name);
        lock (fileLock)
        {
            var current = LoadCore();
            if (current.Error is not null)
            {
                throw new InvalidDataException(current.Error);
            }

            var states = current.States
                .Where(state => !string.Equals(
                    state.Name,
                    normalizedName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (states.Length == current.States.Count)
            {
                throw new KeyNotFoundException(
                    $"Overlay theme state '{normalizedName}' was not found.");
            }

            var backupPath = Write(states);
            return new OverlayThemeStateSaveResult(
                path,
                backupPath,
                normalizedName,
                ReplacedExisting: true);
        }
    }

    private OverlayThemeStateCollection LoadCore()
    {
        if (!File.Exists(path))
        {
            return new OverlayThemeStateCollection([], null);
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidDataException(
                    "The overlay theme state file is not a JSON object.");
            if (root["version"]?.GetValue<int>() != 1)
            {
                throw new InvalidDataException(
                    "The overlay theme state file version is not supported.");
            }

            var items = root["states"] as JsonArray
                ?? throw new InvalidDataException(
                    "The overlay theme state list is missing.");
            var states = new List<OverlayThemeState>(items.Count);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var state = item as JsonObject
                    ?? throw new InvalidDataException(
                        "An overlay theme state is not a JSON object.");
                var name = NormalizeName(state["name"]?.GetValue<string>());
                if (!names.Add(name))
                {
                    throw new InvalidDataException(
                        $"Overlay theme state '{name}' is duplicated.");
                }

                var colorValues = state["colors"] as JsonObject
                    ?? throw new InvalidDataException(
                        $"Overlay theme state '{name}' has no colours.");
                var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
                foreach (var entry in colorValues)
                {
                    if (entry.Value is not JsonValue value
                        || !value.TryGetValue<string>(out var text)
                        || !LegacyOverlayThemeStore.TryParseHtmlColor(text, out var color))
                    {
                        throw new InvalidDataException(
                            $"Overlay theme state '{name}' has an invalid '{entry.Key}' colour.");
                    }

                    colors.Add(entry.Key, color);
                }

                ValidateColors(name, colors);
                states.Add(new OverlayThemeState(name, colors));
            }

            return new OverlayThemeStateCollection(
                states.OrderBy(state => state.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray(),
                null);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidDataException
                or FormatException
                or InvalidOperationException
                or ArgumentException)
        {
            return new OverlayThemeStateCollection(
                [],
                $"Could not read overlay theme states '{path}': {exception.Message}");
        }
    }

    private string? Write(IReadOnlyCollection<OverlayThemeState> states)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "The overlay theme state path has no parent directory.");
        Directory.CreateDirectory(directory);
        var backupPath = File.Exists(path) ? CreateVerifiedBackup(directory) : null;
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var stateArray = new JsonArray();
            foreach (var state in states.OrderBy(
                         state => state.Name,
                         StringComparer.CurrentCultureIgnoreCase))
            {
                ValidateColors(state.Name, state.Colors);
                var colors = new JsonObject();
                foreach (var entry in state.Colors.OrderBy(
                             entry => entry.Key,
                             StringComparer.Ordinal))
                {
                    colors[entry.Key] = LegacyOverlayThemeStore.FormatHtmlColor(
                        entry.Value);
                }

                stateArray.Add(new JsonObject
                {
                    ["name"] = state.Name,
                    ["colors"] = colors,
                });
            }

            var root = new JsonObject
            {
                ["version"] = 1,
                ["states"] = stateArray,
            };
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

            var verifier = new OverlayThemeStateStore(temporaryPath).Load();
            if (verifier.Error is not null || !StatesEqual(states, verifier.States))
            {
                throw new InvalidDataException(
                    verifier.Error ?? "The written overlay theme states did not verify.");
            }

            File.Move(temporaryPath, path, overwrite: true);
            return backupPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string CreateVerifiedBackup(string directory)
    {
        var backupDirectory = Path.Combine(
            directory,
            "legacy-backups",
            "overlay-theme-states",
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, Path.GetFileName(path));
        File.Copy(path, backupPath, overwrite: false);
        if (!SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(
                SHA256.HashData(File.ReadAllBytes(backupPath))))
        {
            throw new IOException(
                "The overlay theme state backup failed checksum verification.");
        }

        return backupPath;
    }

    private static string NormalizeName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 80)
        {
            throw new ArgumentException(
                "A saved overlay theme name must contain 1 to 80 characters.",
                nameof(name));
        }

        return normalized;
    }

    private static void ValidateColors(
        string name,
        IReadOnlyDictionary<string, Color> colors)
    {
        var defaults = LegacyOverlayThemeStore.CreateDefault().Colors;
        foreach (var required in defaults.Keys)
        {
            if (!colors.ContainsKey(required))
            {
                throw new InvalidDataException(
                    $"Overlay theme state '{name}' does not define '{required}'.");
            }
        }
    }

    private static bool StatesEqual(
        IReadOnlyCollection<OverlayThemeState> expected,
        IReadOnlyList<OverlayThemeState> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        return expected.All(expectedState =>
        {
            var actualState = actual.SingleOrDefault(state => string.Equals(
                state.Name,
                expectedState.Name,
                StringComparison.Ordinal));
            return actualState is not null
                && expectedState.Colors.Count == actualState.Colors.Count
                && expectedState.Colors.All(entry =>
                    actualState.Colors.TryGetValue(entry.Key, out var color)
                    && color == entry.Value);
        });
    }
}

public sealed record OverlayThemeState(
    string Name,
    IReadOnlyDictionary<string, Color> Colors);

public sealed record OverlayThemeStateCollection(
    IReadOnlyList<OverlayThemeState> States,
    string? Error);

public sealed record OverlayThemeStateSaveResult(
    string Path,
    string? BackupPath,
    string StateName,
    bool ReplacedExisting);
