using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace SrvSurvey.Desktop.Platform;

/// <summary>Verifies that concurrent SrvSurvey commanders bind to distinct physical journal folders.</summary>
internal sealed class JournalSourceRegistry(string dataDirectory) : IDisposable
{
    private readonly string directory = Path.Combine(dataDirectory, nameof(JournalSourceRegistry));
    private readonly string fileName = Guid.NewGuid().ToString("N") + ".json";
    private SourceRecord? current;
    private FileStream? lease;

    /// <summary>Publishes only the current journal binding; no journal contents or credentials leave this local registry.</summary>
    public bool Bind(string? journalPath, string? frontierId)
    {
        if (string.IsNullOrWhiteSpace(journalPath) || string.IsNullOrWhiteSpace(frontierId))
        {
            Dispose();
            return false;
        }
        try
        {
            string source = ResolveDirectory(Path.GetDirectoryName(Path.GetFullPath(journalPath))!);
            if (current is { } existing && existing.FrontierId == frontierId && existing.Directory == source)
            {
                return true;
            }
            using var process = Process.GetCurrentProcess();
            var record = new SourceRecord(process.Id, process.StartTime.ToUniversalTime().Ticks, frontierId, source);
            Dispose();
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            lease = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            JsonSerializer.Serialize(lease, record);
            lease.Flush(flushToDisk: true);
            current = record;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Dispose();
            return false;
        }
    }

    /// <summary>Allows concurrent uploads only when every detected client has a live, distinct commander and source binding.</summary>
    public bool CanUseSource(int gameWindowCount)
    {
        if (current is null)
        {
            return gameWindowCount <= 1;
        }
        try
        {
            string[] files = Directory.EnumerateFiles(directory, "*.json").Take(257).ToArray();
            if (files.Length > 256)
            {
                return false;
            }
            var sources = new List<SourceRecord>();
            foreach (string path in files)
            {
                if (!TryReadSource(path, out SourceRecord? record))
                {
                    return false;
                }
                if (record is not null)
                {
                    sources.Add(record);
                }
            }
            StringComparer paths = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            return sources.Contains(current)
                && (gameWindowCount <= 1 || sources.Count == gameWindowCount)
                && sources.Select(record => record.FrontierId).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    == sources.Count
                && sources.Select(record => record.Directory).Distinct(paths).Count() == sources.Count;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>Skips abandoned leases and rejects malformed metadata from an owner still holding its source.</summary>
    private static bool TryReadSource(string path, out SourceRecord? source)
    {
        source = null;
        if (!HasLiveLease(path))
        {
            return true;
        }
        if (new FileInfo(path).Length is <= 0 or > 16384)
        {
            return false;
        }
        using var snapshot = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        SourceRecord? record = JsonSerializer.Deserialize<SourceRecord>(snapshot);
        if (
            record is null
            || string.IsNullOrWhiteSpace(record.FrontierId)
            || string.IsNullOrWhiteSpace(record.Directory)
        )
        {
            return false;
        }
        if (IsLive(record))
        {
            source = record;
        }
        return true;
    }

    /// <summary>Releases this instance's binding on shutdown or commander loss.</summary>
    public void Dispose()
    {
        current = null;
        lease?.Dispose();
        lease = null;
        try
        {
            File.Delete(Path.Combine(directory, fileName));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A leftover record is ignored after this process exits.
        }
    }

    /// <summary>A live owner holds the source file open; an abandoned binding cannot authorize uploads even while its old process lives.</summary>
    private static bool HasLiveLease(string path)
    {
        try
        {
            using var released = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <summary>Rejects stale registrations, including records whose process ID was reused.</summary>
    private static bool IsLive(SourceRecord record)
    {
        try
        {
            using var process = Process.GetProcessById(record.ProcessId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == record.StartTime;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Resolves links in every directory component so Steam aliases cannot masquerade as independent sources.</summary>
    private static string ResolveDirectory(string path)
    {
        string root = Path.GetPathRoot(path)!;
        string resolved = root;
        foreach (
            string component in path[root.Length..]
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries
                )
        )
        {
            var folder = new DirectoryInfo(Path.Combine(resolved, component));
            resolved = folder.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? folder.FullName;
        }
        return Path.TrimEndingDirectorySeparator(resolved);
    }

    /// <summary>A bounded local binding whose process start time protects against stale PID reuse.</summary>
    private sealed record SourceRecord(int ProcessId, long StartTime, string FrontierId, string Directory);
}
