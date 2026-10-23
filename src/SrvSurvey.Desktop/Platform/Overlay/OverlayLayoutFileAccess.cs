using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Serializes layout writers across processes and recovers interrupted multi-file replacements.</summary>
internal static class OverlayLayoutFileAccess
{
    internal static readonly string[] FileNames =
    [
        "plotters.json",
        "overlay-scale-overrides.json",
        "overlay-typography-overrides.json",
        "overlay-size-overrides.json",
        "overlay-position-references.json",
        "overlay-growth-overrides.json",
    ];

    // Only plotterOpacity belongs to overlays; the rest of settings.json is shared data.
    internal const string SharedSettingsFileName = "settings.json";
    internal static readonly string[] JournalFileNames = [.. FileNames, SharedSettingsFileName];
    private const string OpacityProperty = "plotterOpacity";
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private const string JournalName = "overlay-layout-pending.json";

    /// <summary>Keeps a stable lock file; deleting it on release would permit concurrent locks on different Unix inodes.</summary>
    internal static FileStream Acquire(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "overlay-layout.lock");
        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception)
            {
                if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(2))
                {
                    throw new IOException(
                        $"Overlay layout is busy or inaccessible at '{directory}'. Retry after the other writer finishes.",
                        exception
                    );
                }
                Thread.Sleep(10);
            }
        }
    }

    /// <summary>Acquires distinct layout folders in a fixed order and releases all leases if any acquisition fails.</summary>
    internal static IDisposable AcquireForFiles(IEnumerable<string> files)
    {
        var leases = new Leases();
        try
        {
            foreach (
                string directory in files
                    .Where(file => JournalFileNames.Contains(Path.GetFileName(file), StringComparer.Ordinal))
                    .Select(file => Path.GetDirectoryName(Path.GetFullPath(file))!)
                    .Distinct(PathComparer)
                    .Order(PathComparer)
            )
            {
                leases.Streams.Add(Acquire(directory));
                Recover(directory);
            }
            return leases;
        }
        catch
        {
            leases.Dispose();
            throw;
        }
    }

    /// <summary>Restores all original files before another read or write, retaining the journal until recovery completes.</summary>
    internal static void Recover(string directory)
    {
        string journal = Path.Combine(directory, JournalName);
        if (!File.Exists(journal))
        {
            return;
        }
        if (new FileInfo(journal).Length > 64 * 1024 * 1024)
        {
            throw new InvalidDataException($"Overlay recovery journal is too large: '{journal}'.");
        }
        Dictionary<string, byte[]?> originals;
        try
        {
            originals =
                JsonSerializer.Deserialize<Dictionary<string, byte[]?>>(File.ReadAllBytes(journal))
                ?? throw new InvalidDataException($"Invalid overlay recovery journal: '{journal}'.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Invalid overlay recovery journal: '{journal}'.", exception);
        }
        if (
            (
                originals.Count != JournalFileNames.Length
                && !(
                    originals.Count == JournalFileNames.Length - 1
                    && !originals.ContainsKey("overlay-growth-overrides.json")
                )
            ) || originals.Keys.Any(name => !JournalFileNames.Contains(name, StringComparer.Ordinal))
        )
        {
            throw new InvalidDataException($"Overlay recovery journal contains invalid file names: '{journal}'.");
        }
        // Older journals may contain a whole settings file; use only its opacity property.
        RestoreSettingsOpacity(Path.Combine(directory, SharedSettingsFileName), originals[SharedSettingsFileName]);
        foreach (string name in FileNames)
        {
            string path = Path.Combine(directory, name);
            // An older generation did not own growth preferences; leave those untouched.
            if (!originals.TryGetValue(name, out byte[]? content))
            {
                continue;
            }
            if (content is null)
            {
                File.Delete(path);
            }
            else
            {
                WriteAtomic(path, content);
            }
        }
        File.Delete(journal);
    }

    /// <summary>Snapshots the complete layout before its first replacement; callers must already hold the folder lease.</summary>
    internal static Transaction BeginTransaction(string directory)
    {
        Recover(directory);
        Dictionary<string, byte[]?> originals = JournalFileNames.ToDictionary(
            name => name,
            name =>
            {
                string path = Path.Combine(directory, name);
                if (name == SharedSettingsFileName)
                {
                    return SnapshotSettingsOpacity(path);
                }
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            },
            StringComparer.Ordinal
        );
        byte[] journal = JsonSerializer.SerializeToUtf8Bytes(originals);
        if (journal.Length > 64 * 1024 * 1024)
        {
            throw new InvalidDataException(
                "Overlay settings exceed the supported recovery size of 64 MiB. No files were changed."
            );
        }
        WriteAtomic(Path.Combine(directory, JournalName), journal);
        return new Transaction(directory);
    }

    /// <summary>Snapshots only opacity, excluding unrelated properties from the recovery journal.</summary>
    internal static byte[]? SnapshotSettingsOpacity(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        JsonObject settings = LegacyOverlayLayoutStore.ParseObject(path);
        var snapshot = new JsonObject();
        if (settings.TryGetPropertyValue(OpacityProperty, out JsonNode? opacity))
        {
            snapshot[OpacityProperty] = opacity?.DeepClone();
        }
        return JsonSerializer.SerializeToUtf8Bytes(snapshot);
    }

    /// <summary>Restores or removes only opacity while preserving the latest unrelated settings, including in old journals.</summary>
    internal static void RestoreSettingsOpacity(string path, byte[]? content)
    {
        JsonObject? snapshot = content is null ? null : ReadSettingsSnapshot(content);
        JsonObject settings = File.Exists(path) ? LegacyOverlayLayoutStore.ParseObject(path) : [];
        if (snapshot?.ContainsKey(OpacityProperty) == true)
        {
            settings[OpacityProperty] = snapshot[OpacityProperty]?.DeepClone();
        }
        else if (!settings.Remove(OpacityProperty))
        {
            return;
        }
        WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(settings));
    }

    /// <summary>Reads both old whole-file and new property-only settings snapshots using legacy JSON syntax.</summary>
    private static JsonObject ReadSettingsSnapshot(byte[] content) =>
        JsonNode.Parse(
            content,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            }
        ) as JsonObject
        ?? throw new InvalidDataException("Invalid shared settings recovery data.");

    /// <summary>Flushes a recovery file before atomically replacing its destination on the same filesystem.</summary>
    internal static void WriteAtomic(string path, byte[] content)
    {
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>Commits only after every replacement succeeds; an unsuccessful operation retains recoverable originals.</summary>
    internal sealed class Transaction(string directory) : IDisposable
    {
        private bool committed;

        /// <summary>Marks the whole layout generation complete after its files have been verified.</summary>
        internal void Commit()
        {
            File.Delete(Path.Combine(directory, JournalName));
            committed = true;
        }

        /// <summary>Attempts rollback without replacing the original failure; an incomplete journal remains retryable.</summary>
        public void Dispose()
        {
            if (!committed)
            {
                try
                {
                    Recover(directory);
                }
                catch (Exception exception) when (LegacyOverlayLayoutStore.IsPersistenceFailure(exception))
                {
                    Trace.TraceWarning(
                        $"Overlay rollback could not finish at '{directory}'; its journal was retained: {exception}"
                    );
                }
            }
        }
    }

    /// <summary>Owns a set of folder leases and releases them in reverse acquisition order.</summary>
    private sealed class Leases : IDisposable
    {
        internal List<FileStream> Streams { get; } = [];

        /// <summary>Releases every acquired lease.</summary>
        public void Dispose()
        {
            foreach (FileStream stream in Streams.AsEnumerable().Reverse())
            {
                stream.Dispose();
            }
        }
    }
}
