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
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
    );
    private readonly string path;
    private readonly object fileLock;

    public OverlayThemeStateStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
        fileLock = FileLocks.GetOrAdd(this.path, _ => new object());
    }

    public OverlayThemeStateLoadResult Load()
    {
        lock (fileLock)
        {
            return LoadCore();
        }
    }

    public OverlayThemeStateSaveResult SaveState(
        string name,
        IReadOnlyDictionary<string, Color> colors,
        OverlayTypographySettings? typography = null
    )
    {
        ArgumentNullException.ThrowIfNull(colors);
        string normalizedName = NormalizeName(name);
        lock (fileLock)
        {
            OverlayThemeStateLoadResult current = LoadCore();
            if (current.Error is not null)
            {
                throw new InvalidDataException(current.Error);
            }

            var states = current.States.ToList();
            int existingIndex = states.FindIndex(state =>
                string.Equals(state.Name, normalizedName, StringComparison.OrdinalIgnoreCase)
            );
            var updated = new OverlayThemeState(
                normalizedName,
                new Dictionary<string, Color>(colors, StringComparer.Ordinal),
                typography ?? OverlayTypographySettings.Default
            );
            if (existingIndex >= 0)
            {
                states[existingIndex] = updated;
            }
            else
            {
                states.Add(updated);
            }

            string? backupPath = Write(states);
            return new OverlayThemeStateSaveResult(path, backupPath, updated.Name, existingIndex >= 0);
        }
    }

    public OverlayThemeStateSaveResult DeleteState(string name)
    {
        string normalizedName = NormalizeName(name);
        lock (fileLock)
        {
            OverlayThemeStateLoadResult current = LoadCore();
            if (current.Error is not null)
            {
                throw new InvalidDataException(current.Error);
            }

            OverlayThemeState[] states = current
                .States.Where(state => !string.Equals(state.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (states.Length == current.States.Count)
            {
                throw new KeyNotFoundException($"Overlay theme state '{normalizedName}' was not found.");
            }

            string? backupPath = Write(states);
            return new OverlayThemeStateSaveResult(path, backupPath, normalizedName, ReplacedExisting: true);
        }
    }

    private OverlayThemeStateLoadResult LoadCore()
    {
        if (!File.Exists(path))
        {
            return new OverlayThemeStateLoadResult([], null);
        }

        try
        {
            JsonObject root =
                JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidDataException("The overlay theme state file is not a JSON object.");
            if (root["version"]?.GetValue<int>() != 1)
            {
                throw new InvalidDataException("The overlay theme state file version is not supported.");
            }

            JsonArray items =
                root["states"] as JsonArray
                ?? throw new InvalidDataException("The overlay theme state list is missing.");
            var states = new List<OverlayThemeState>(items.Count);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonNode? item in items)
            {
                JsonObject state =
                    item as JsonObject
                    ?? throw new InvalidDataException("An overlay theme state is not a JSON object.");
                string name = NormalizeName(state["name"]?.GetValue<string>());
                if (!names.Add(name))
                {
                    throw new InvalidDataException($"Overlay theme state '{name}' is duplicated.");
                }

                JsonObject colorValues =
                    state["colors"] as JsonObject
                    ?? throw new InvalidDataException($"Overlay theme state '{name}' has no colours.");
                var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, JsonNode?> entry in colorValues)
                {
                    if (
                        entry.Value is not JsonValue value
                        || !value.TryGetValue<string>(out string? text)
                        || !LegacyOverlayThemeStore.TryParseHtmlColor(text, out Color color)
                    )
                    {
                        throw new InvalidDataException(
                            $"Overlay theme state '{name}' has an invalid '{entry.Key}' colour."
                        );
                    }

                    colors.Add(entry.Key, color);
                }

                _ = OverlayThemePresetCatalog.AddMissingHeaderColor(colors);
                _ = OverlayThemePresetCatalog.AddMissingExpandedBiologyColors(colors);
                ValidateColors(name, colors);
                var typography = OverlayTypographySettings.Parse(
                    state["typography"] as JsonObject,
                    $"Overlay theme state '{name}'"
                );
                states.Add(new OverlayThemeState(name, colors, typography));
            }

            return new OverlayThemeStateLoadResult(
                states.OrderBy(state => state.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
                null
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or InvalidDataException
                        or FormatException
                        or InvalidOperationException
                        or ArgumentException
            )
        {
            return new OverlayThemeStateLoadResult(
                [],
                $"Could not read overlay theme states '{path}': {exception.Message}"
            );
        }
    }

    private string? Write(IReadOnlyCollection<OverlayThemeState> states)
    {
        string directory =
            Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The overlay theme state path has no parent directory.");
        Directory.CreateDirectory(directory);
        string? backupPath = File.Exists(path) ? CreateVerifiedBackup(directory) : null;
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var stateArray = new JsonArray();
            foreach (
                OverlayThemeState? state in states.OrderBy(state => state.Name, StringComparer.CurrentCultureIgnoreCase)
            )
            {
                ValidateColors(state.Name, state.Colors);
                var colors = new JsonObject();
                foreach (
                    KeyValuePair<string, Color> entry in state.Colors.OrderBy(
                        entry => entry.Key,
                        StringComparer.Ordinal
                    )
                )
                {
                    colors[entry.Key] = LegacyOverlayThemeStore.FormatHtmlColor(entry.Value);
                }

                stateArray.Add(
                    new JsonObject
                    {
                        ["name"] = state.Name,
                        ["colors"] = colors,
                        ["typography"] = state.EffectiveTypography.ToJson(),
                    }
                );
            }

            var root = new JsonObject { ["version"] = 1, ["states"] = stateArray };
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (
                var writer = new Utf8JsonWriter(
                    stream,
                    new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = true }
                )
            )
            {
                root.WriteTo(writer);
            }

            OverlayThemeStateLoadResult verifier = new OverlayThemeStateStore(temporaryPath).Load();
            if (verifier.Error is not null || !StatesEqual(states, verifier.States))
            {
                throw new InvalidDataException(verifier.Error ?? "The written overlay theme states did not verify.");
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
        string backupDirectory = Path.Combine(
            directory,
            "legacy-backups",
            "overlay-theme-states",
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture)
        );
        Directory.CreateDirectory(backupDirectory);
        string backupPath = Path.Combine(backupDirectory, Path.GetFileName(path));
        File.Copy(path, backupPath, overwrite: false);
        if (
            !SHA256
                .HashData(File.ReadAllBytes(path))
                .AsSpan()
                .SequenceEqual(SHA256.HashData(File.ReadAllBytes(backupPath)))
        )
        {
            throw new IOException("The overlay theme state backup failed checksum verification.");
        }

        return backupPath;
    }

    private static string NormalizeName(string? name)
    {
        string normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 80)
        {
            throw new ArgumentException("A saved overlay theme name must contain 1 to 80 characters.", nameof(name));
        }

        return normalized;
    }

    private static void ValidateColors(string name, IReadOnlyDictionary<string, Color> colors)
    {
        IReadOnlyDictionary<string, Color> defaults = LegacyOverlayThemeStore.CreateDefault().Colors;
        string? missingColor = defaults.Keys.FirstOrDefault(required => !colors.ContainsKey(required));
        if (missingColor is not null)
        {
            throw new InvalidDataException($"Overlay theme state '{name}' does not define '{missingColor}'.");
        }
    }

    private static bool StatesEqual(
        IReadOnlyCollection<OverlayThemeState> expected,
        IReadOnlyList<OverlayThemeState> actual
    )
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        return expected.All(expectedState =>
        {
            OverlayThemeState? actualState = actual.SingleOrDefault(state =>
                string.Equals(state.Name, expectedState.Name, StringComparison.Ordinal)
            );
            return actualState is not null
                && expectedState.Colors.Count == actualState.Colors.Count
                && expectedState.EffectiveTypography == actualState.EffectiveTypography
                && expectedState.Colors.All(entry =>
                    actualState.Colors.TryGetValue(entry.Key, out Color color) && color == entry.Value
                );
        });
    }
}

public sealed record OverlayThemeState(
    string Name,
    IReadOnlyDictionary<string, Color> Colors,
    OverlayTypographySettings? Typography = null
)
{
    public OverlayTypographySettings EffectiveTypography => Typography ?? OverlayTypographySettings.Default;
}

public sealed record OverlayThemeStateLoadResult(IReadOnlyList<OverlayThemeState> States, string? Error);

public sealed record OverlayThemeStateSaveResult(
    string Path,
    string? BackupPath,
    string StateName,
    bool ReplacedExisting
);
