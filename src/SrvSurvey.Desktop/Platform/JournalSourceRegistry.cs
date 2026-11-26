using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    internal static string ResolveDirectory(string path)
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

/// <summary>An exclusive OS-backed lease granting one SrvSurvey instance ownership of a single commander.</summary>
internal sealed class CommanderClaimLease : IDisposable
{
    private FileStream? stream;

    internal CommanderClaimLease(string frontierId, FileStream stream)
    {
        FrontierId = frontierId;
        this.stream = stream;
    }

    public string FrontierId { get; }

    public void Dispose()
    {
        Interlocked.Exchange(ref stream, null)?.Dispose();
    }
}

/// <summary>Tracks exclusive commander ownership across SrvSurvey instances using per-commander OS file leases.</summary>
internal sealed class CommanderClaimRegistry(string dataDirectory) : IDisposable
{
    private readonly string directory = Path.Combine(dataDirectory, "CommanderClaims");
    private CommanderClaimLease? currentLease;
    private CommanderClaimLease? reservedLease;

    /// <summary>Claims exclusive ownership of the commander currently assigned to this SrvSurvey instance.</summary>
    public bool Claim(string? frontierId)
    {
        string? normalized = NormalizeFrontierId(frontierId);
        if (normalized is null)
        {
            DisposeLease(ref currentLease);
            return false;
        }

        if (MatchesLease(currentLease, normalized))
        {
            return true;
        }

        if (MatchesLease(reservedLease, normalized))
        {
            CommanderClaimLease promoted = reservedLease!;
            reservedLease = null;
            DisposeLease(ref currentLease);
            currentLease = promoted;
            return true;
        }

        CommanderClaimLease? acquired = TryAcquireLease(normalized);
        if (acquired is null)
        {
            return false;
        }

        DisposeLease(ref currentLease);
        currentLease = acquired;
        return true;
    }

    /// <summary>Atomically reserves an exclusive lease for an upcoming commander switch while keeping the current claim held.</summary>
    public bool TryReserve(string? frontierId)
    {
        string? normalized = NormalizeFrontierId(frontierId);
        if (normalized is null)
        {
            return false;
        }

        if (MatchesLease(reservedLease, normalized) || MatchesLease(currentLease, normalized))
        {
            return true;
        }

        DisposeLease(ref reservedLease);
        reservedLease = TryAcquireLease(normalized);
        return reservedLease is not null;
    }

    /// <summary>Adopts a previously reserved lease transferred across an in-process commander switch.</summary>
    public void Adopt(CommanderClaimLease? lease)
    {
        if (lease is null || ReferenceEquals(currentLease, lease))
        {
            return;
        }

        if (ReferenceEquals(reservedLease, lease))
        {
            reservedLease = null;
        }

        DisposeLease(ref currentLease);
        currentLease = lease;
    }

    /// <summary>Detaches a matching reserved lease so the caller can hand it off across teardown without releasing the OS lock.</summary>
    public CommanderClaimLease? DetachReservedLease(string? expectedFrontierId = null)
    {
        CommanderClaimLease? detached = reservedLease;
        reservedLease = null;
        if (detached is null)
        {
            return null;
        }

        string? normalizedExpected = NormalizeFrontierId(expectedFrontierId);
        if (
            normalizedExpected is not null
            && !string.Equals(detached.FrontierId, normalizedExpected, StringComparison.OrdinalIgnoreCase)
        )
        {
            detached.Dispose();
            return null;
        }

        return detached;
    }

    /// <summary>Returns true when another live SrvSurvey instance currently holds the exclusive lease for the given commander.</summary>
    public bool IsClaimedByOtherInstance(string frontierId)
    {
        string? normalized = NormalizeFrontierId(frontierId);
        if (normalized is null)
        {
            return false;
        }

        if (MatchesLease(currentLease, normalized) || MatchesLease(reservedLease, normalized))
        {
            return false;
        }

        return ProcessLeaseFile.HasLiveLease(GetClaimPath(normalized));
    }

    /// <summary>Releases all commander leases held by this instance without unlinking deterministic lock files.</summary>
    public void Dispose()
    {
        DisposeLease(ref reservedLease);
        DisposeLease(ref currentLease);
    }

    /// <summary>Acquires a standalone exclusive lease for a commander during startup before opening its workspace.</summary>
    public static CommanderClaimLease? TryAcquire(string dataDirectory, string? frontierId)
    {
        string? normalized = NormalizeFrontierId(frontierId);
        if (normalized is null)
        {
            return null;
        }

        return TryAcquireLease(Path.Combine(dataDirectory, "CommanderClaims"), normalized);
    }

    private CommanderClaimLease? TryAcquireLease(string normalizedFrontierId) =>
        TryAcquireLease(directory, normalizedFrontierId);

    private static CommanderClaimLease? TryAcquireLease(string claimsDirectory, string normalizedFrontierId)
    {
        FileStream? stream = null;
        try
        {
            Directory.CreateDirectory(claimsDirectory);
            string path = GetClaimPath(claimsDirectory, normalizedFrontierId);
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using var process = Process.GetCurrentProcess();
            var record = new ClaimRecord(process.Id, process.StartTime.ToUniversalTime().Ticks, normalizedFrontierId);
            stream.SetLength(0);
            JsonSerializer.Serialize(stream, record);
            stream.Flush(flushToDisk: true);
            CommanderClaimLease acquired = new(normalizedFrontierId, stream);
            stream = null;
            return acquired;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            stream?.Dispose();
            return null;
        }
    }

    private string GetClaimPath(string normalizedFrontierId) => GetClaimPath(directory, normalizedFrontierId);

    private static string GetClaimPath(string claimsDirectory, string normalizedFrontierId) =>
        Path.Combine(claimsDirectory, GetClaimFileStem(normalizedFrontierId) + ".lock");

    internal static string GetClaimFileStem(string normalizedFrontierId) =>
        IsSafeClaimFileName(normalizedFrontierId)
            ? normalizedFrontierId
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedFrontierId)))[..16];

    private static bool IsSafeClaimFileName(string value) =>
        value.Length is > 0 and <= 64
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    internal static string? NormalizeFrontierId(string? frontierId) =>
        string.IsNullOrWhiteSpace(frontierId) ? null : frontierId.Trim().ToUpperInvariant();

    private static bool MatchesLease(CommanderClaimLease? lease, string normalizedFrontierId) =>
        lease is not null && string.Equals(lease.FrontierId, normalizedFrontierId, StringComparison.OrdinalIgnoreCase);

    private static void DisposeLease(ref CommanderClaimLease? lease)
    {
        CommanderClaimLease? existing = lease;
        lease = null;
        existing?.Dispose();
    }

    private sealed record ClaimRecord(int ProcessId, long StartTime, string FrontierId);
}

/// <summary>Persists confirmed journal consumption checkpoints per commander so peer instances never guess historical boundaries.</summary>
internal static class CommanderJournalCheckpointStore
{
    private const int MaxStoredEntries = 16;
    private const long MaxCheckpointFileBytes = 65536;

    public static bool TrySave(
        string dataDirectory,
        string? frontierId,
        SrvSurvey.Core.Journal.JournalCheckpoint checkpoint
    )
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        string? normalized = CommanderClaimRegistry.NormalizeFrontierId(frontierId);
        if (normalized is null || string.IsNullOrWhiteSpace(checkpoint.JournalPath) || checkpoint.ByteOffset <= 0)
        {
            return false;
        }

        string? tempPath = null;
        try
        {
            string claimsDirectory = Path.Combine(dataDirectory, "CommanderClaims");
            Directory.CreateDirectory(claimsDirectory);
            string path = GetCheckpointPath(claimsDirectory, normalized);
            Dictionary<string, CheckpointEntry> entries = LoadEntries(path);
            string resolvedJournalPath = ResolveJournalPath(checkpoint.JournalPath);
            long updatedOffset = entries.TryGetValue(resolvedJournalPath, out CheckpointEntry? existing)
                ? Math.Max(existing.ByteOffset, checkpoint.ByteOffset)
                : checkpoint.ByteOffset;
            entries[resolvedJournalPath] = new CheckpointEntry(
                resolvedJournalPath,
                updatedOffset,
                DateTimeOffset.UtcNow.UtcTicks
            );
            CheckpointEntry[] retained = entries
                .Values.OrderByDescending(entry => entry.UpdatedUtcTicks)
                .Take(MaxStoredEntries)
                .ToArray();
            tempPath = Path.Combine(
                claimsDirectory,
                CommanderClaimRegistry.GetClaimFileStem(normalized) + "." + Guid.NewGuid().ToString("N") + ".tmp"
            );
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new CheckpointDocument(retained));
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
            tempPath = null;
            return true;
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            if (tempPath is not null)
            {
                ProcessLeaseFile.TryDelete(tempPath);
            }

            return false;
        }
    }

    public static IReadOnlyDictionary<string, long> TryLoad(string dataDirectory, string? frontierId)
    {
        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        string? normalized = CommanderClaimRegistry.NormalizeFrontierId(frontierId);
        if (normalized is null)
        {
            return new Dictionary<string, long>(pathComparer);
        }

        string path = GetCheckpointPath(Path.Combine(dataDirectory, "CommanderClaims"), normalized);
        Dictionary<string, CheckpointEntry> entries = LoadEntries(path);
        var offsets = new Dictionary<string, long>(entries.Count, pathComparer);
        foreach (CheckpointEntry entry in entries.Values)
        {
            offsets[entry.JournalPath] = entry.ByteOffset;
        }

        return offsets;
    }

    private static Dictionary<string, CheckpointEntry> LoadEntries(string path)
    {
        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var result = new Dictionary<string, CheckpointEntry>(pathComparer);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > MaxCheckpointFileBytes)
            {
                return result;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            CheckpointDocument? document = JsonSerializer.Deserialize<CheckpointDocument>(stream);
            if (document?.Entries is null)
            {
                return result;
            }

            foreach (CheckpointEntry entry in document.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.JournalPath) || entry.ByteOffset <= 0)
                {
                    continue;
                }

                string resolved = ResolveJournalPath(entry.JournalPath);
                if (
                    !result.TryGetValue(resolved, out CheckpointEntry? existing)
                    || entry.ByteOffset > existing.ByteOffset
                )
                {
                    result[resolved] = new CheckpointEntry(
                        resolved,
                        entry.ByteOffset,
                        Math.Max(entry.UpdatedUtcTicks, existing?.UpdatedUtcTicks ?? 0)
                    );
                }
            }
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            result.Clear();
        }

        return result;
    }

    private static string GetCheckpointPath(string claimsDirectory, string normalizedFrontierId) =>
        Path.Combine(
            claimsDirectory,
            CommanderClaimRegistry.GetClaimFileStem(normalizedFrontierId) + ".checkpoint.json"
        );

    private static string ResolveJournalPath(string journalPath)
    {
        string fullPath = Path.GetFullPath(journalPath);
        string? directory = Path.GetDirectoryName(fullPath);
        return string.IsNullOrEmpty(directory)
            ? fullPath
            : Path.Combine(JournalSourceRegistry.ResolveDirectory(directory), Path.GetFileName(fullPath));
    }

    private sealed record CheckpointDocument(IReadOnlyList<CheckpointEntry>? Entries);

    private sealed record CheckpointEntry(string JournalPath, long ByteOffset, long UpdatedUtcTicks);
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
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
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
