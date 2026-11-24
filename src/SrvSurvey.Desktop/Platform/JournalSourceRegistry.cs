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
        if (!ProcessLeaseFile.HasLiveLease(path))
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
        if (ProcessLeaseFile.IsProcessLive(record.ProcessId, record.StartTime))
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
        ProcessLeaseFile.TryDelete(Path.Combine(directory, fileName));
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

/// <summary>Tracks live commander assignments across SrvSurvey instances so automatic switching never steals an owned commander.</summary>
internal sealed class CommanderClaimRegistry(string dataDirectory) : IDisposable
{
    private readonly string directory = Path.Combine(dataDirectory, "CommanderClaims");
    private readonly string fileName = Guid.NewGuid().ToString("N") + ".json";
    private ClaimRecord? current;
    private FileStream? lease;

    /// <summary>Claims the commander currently assigned to this SrvSurvey instance.</summary>
    public bool Claim(string? frontierId)
    {
        string? normalized = string.IsNullOrWhiteSpace(frontierId) ? null : frontierId.Trim().ToUpperInvariant();
        if (normalized is null)
        {
            Dispose();
            return false;
        }

        if (
            current is { } existing
            && string.Equals(existing.FrontierId, normalized, StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            var record = new ClaimRecord(process.Id, process.StartTime.ToUniversalTime().Ticks, normalized);
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

    /// <summary>Returns true when another live SrvSurvey instance currently holds a claim on the given commander.</summary>
    public bool IsClaimedByOtherInstance(string frontierId)
    {
        if (string.IsNullOrWhiteSpace(frontierId) || !Directory.Exists(directory))
        {
            return false;
        }

        string target = frontierId.Trim();
        try
        {
            string[] files = Directory.EnumerateFiles(directory, "*.json").Take(257).ToArray();
            if (files.Length > 256)
            {
                return true;
            }

            return files.Where(path => !IsCurrentClaimFile(path)).Any(path => IsLiveClaimForCommander(path, target));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private bool IsCurrentClaimFile(string path) =>
        string.Equals(
            Path.GetFileName(path),
            fileName,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
        );

    private static bool IsLiveClaimForCommander(string path, string frontierId)
    {
        try
        {
            if (!ProcessLeaseFile.HasLiveLease(path))
            {
                return false;
            }

            if (new FileInfo(path).Length is <= 0 or > 16384)
            {
                return true;
            }

            using var snapshot = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            ClaimRecord? record = JsonSerializer.Deserialize<ClaimRecord>(snapshot);
            if (record is null || string.IsNullOrWhiteSpace(record.FrontierId))
            {
                return true;
            }

            return ProcessLeaseFile.IsProcessLive(record.ProcessId, record.StartTime)
                && string.Equals(record.FrontierId, frontierId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return true;
        }
    }

    /// <summary>Releases this instance's commander claim on shutdown or commander switch.</summary>
    public void Dispose()
    {
        current = null;
        lease?.Dispose();
        lease = null;
        ProcessLeaseFile.TryDelete(Path.Combine(directory, fileName));
    }

    private sealed record ClaimRecord(int ProcessId, long StartTime, string FrontierId);
}

internal static class ProcessLeaseFile
{
    /// <summary>A live owner holds the lease file open; a missing or unlocked file has no live lease.</summary>
    public static bool HasLiveLease(string path)
    {
        try
        {
            using var released = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <summary>Rejects stale registrations, including records whose process ID was reused.</summary>
    public static bool IsProcessLive(int processId, long startTimeTicks)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == startTimeTicks;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A leftover record is ignored once its handle is released.
        }
    }
}
