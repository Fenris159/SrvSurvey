using System.Diagnostics;
using System.Text.Json;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Serializes layout writers across processes and recovers interrupted multi-file replacements.</summary>
internal static class OverlayLayoutFileAccess
{
    internal static readonly string[] FileNames =
    [
        "plotters.json",
        "settings.json",
        "overlay-scale-overrides.json",
        "overlay-typography-overrides.json",
        "overlay-size-overrides.json",
        "overlay-position-references.json",
    ];
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
                    .Where(file => FileNames.Contains(Path.GetFileName(file), StringComparer.Ordinal))
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
            originals.Count != FileNames.Length
            || originals.Keys.Any(name => !FileNames.Contains(name, StringComparer.Ordinal))
        )
        {
            throw new InvalidDataException($"Overlay recovery journal contains invalid file names: '{journal}'.");
        }
        foreach ((string name, byte[]? content) in originals)
        {
            string path = Path.Combine(directory, name);
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
        Dictionary<string, byte[]?> originals = FileNames.ToDictionary(
            name => name,
            name =>
                File.Exists(Path.Combine(directory, name)) ? File.ReadAllBytes(Path.Combine(directory, name)) : null,
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

        /// <summary>Restores the opening generation when an operation exits without committing.</summary>
        public void Dispose()
        {
            if (!committed)
            {
                Recover(directory);
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
