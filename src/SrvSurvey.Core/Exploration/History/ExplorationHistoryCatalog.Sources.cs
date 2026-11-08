using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Exploration.History;

public sealed partial class ExplorationHistoryCatalog
{
    private const int MaximumLineBytes = 1024 * 1024;
    private static readonly UTF8Encoding JournalEncoding = new(false, true);
    private static readonly JsonSerializerOptions ContextJson = new() { PropertyNameCaseInsensitive = true };
    private readonly ConcurrentDictionary<(JournalSource First, JournalSource Second), bool> verifiedPrefixes = new();

    private sealed record JournalLine(long Offset, long NextOffset, string? Content);

    private sealed record OriginalReference(long SourceId, long Offset, string Hash);

    private sealed record JournalSource(
        string Name,
        string Path,
        DateTimeOffset? FirstTime,
        string PrefixHash,
        long ModifiedTicks,
        long Length,
        long CompleteLength,
        long CreatedTicks,
        JournalFilePart? FilePart,
        bool? IsLive
    );

    private sealed record CommanderProof(string Name, long Offset, string Hash);

    private sealed class SourceContext
    {
        public string? FrontierId { get; set; }
        public CommanderProof? CommanderRecord { get; set; }
        public int? JournalPartNumber { get; set; }
        public bool? IsLive { get; set; }
        public string? SystemName { get; set; }
        public long? SystemAddress { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }
        public string? StationName { get; set; }
    }

    private sealed record SourceCheckpoint(
        long Id,
        long NextOffset,
        long LastOffset,
        long Length,
        string LastHash,
        SourceContext Context
    );

    private static IEnumerable<JournalLine> ReadLines(
        string path,
        long offset,
        CancellationToken cancellationToken,
        long? maximumOffset = null,
        bool requireCapturedLength = false
    )
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024
        );
        stream.Seek(offset, SeekOrigin.Begin);
        using var pending = new MemoryStream();
        byte[] buffer = new byte[64 * 1024];
        long lineOffset = offset;
        long position = offset;
        bool oversized = false;
        // Index only the captured file length so concurrent journal appends belong
        // to the next checkpoint, with their own matching size and modification time.
        long remaining = Math.Max(0, (maximumOffset ?? stream.Length) - offset);
        int read;
        while (remaining > 0 && (read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining))) > 0)
        {
            remaining -= read;
            cancellationToken.ThrowIfCancellationRequested();
            int start = 0;
            while (start < read)
            {
                int newline = Array.IndexOf(buffer, (byte)'\n', start, read - start);
                int count = newline < 0 ? read - start : newline - start;
                oversized = AppendBytes(pending, buffer, start, count, oversized);
                position += count;
                start += count;
                if (newline < 0)
                {
                    break;
                }
                position++;
                start++;
                yield return new JournalLine(lineOffset, position, oversized ? null : DecodeLine(pending));
                pending.SetLength(0);
                oversized = false;
                lineOffset = position;
            }
        }
        EnsureCapturedBytesAvailable(remaining, requireCapturedLength);
        if (ReadTail(pending, oversized) is { } tail)
        {
            yield return new JournalLine(lineOffset, position, tail);
        }
    }

    private static void EnsureCapturedBytesAvailable(long remaining, bool requireCapturedLength)
    {
        if (requireCapturedLength && remaining > 0)
        {
            throw new EndOfStreamException(
                "Captured journal bytes are missing; recovery boundaries have been retained."
            );
        }
    }

    private static bool AppendBytes(MemoryStream pending, byte[] buffer, int start, int count, bool oversized)
    {
        if (!oversized && pending.Length + count <= MaximumLineBytes)
        {
            pending.Write(buffer, start, count);
            return false;
        }
        return true;
    }

    private static string? ReadTail(MemoryStream pending, bool oversized)
    {
        if (pending.Length == 0 || oversized)
        {
            return null;
        }
        string? text = DecodeLine(pending);
        return text is not null && JournalEventEnvelope.TryParse(text, out _, out _) ? text : null;
    }

    private static string? DecodeLine(MemoryStream buffer)
    {
        try
        {
            return JournalEncoding
                .GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length))
                .TrimEnd('\r')
                .TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static string ContentHash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static JournalEventEnvelope? ReadOriginal(
        SqliteConnection connection,
        OriginalReference reference,
        CancellationToken token = default
    )
    {
        using SqliteCommand command = Command(
            connection,
            "SELECT path FROM sources WHERE id=@source;",
            ("@source", reference.SourceId)
        );
        if (command.ExecuteScalar() is not string path)
        {
            return null;
        }
        try
        {
            JournalLine? line = ReadLines(path, reference.Offset, token).FirstOrDefault();
            if (
                line?.Content is { } text
                && ContentHash(text) == reference.Hash
                && JournalEventEnvelope.TryParse(text, out JournalEventEnvelope? entry, out _)
                && entry is not null
            )
            {
                return entry with
                {
                    SourceRecordIdentity =
                        Path.GetFileName(path) + ":" + reference.Offset.ToString(CultureInfo.InvariantCulture),
                };
            }
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private JournalSource[] DiscoverSources(
        IReadOnlyList<string> directories,
        CancellationToken cancellationToken,
        IProgress<ExplorationHistoryProgress>? progress = null
    )
    {
        var found = new Dictionary<string, JournalSource>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in directories.Where(Directory.Exists))
        {
            foreach (string path in Directory.EnumerateFiles(directory, "Journal.*.log", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = Path.GetFileName(path);
                JournalSource candidate = ReadJournalSource(path, cancellationToken, progress);
                found[name] = found.TryGetValue(name, out JournalSource? existing)
                    ? ChooseCompleteSource(existing, candidate, cancellationToken)
                    : candidate;
            }
        }
        return found
            .Values.OrderBy(source => source.FirstTime)
            .ThenBy(source => source.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class JournalSnapshotUnstableException(string name, long completeLength)
        : IOException("Journal " + name + " changed during discovery. Refresh history when the file is stable.")
    {
        public string SourceName { get; } = name;
        public long CompleteLength { get; } = completeLength;
    }

    private static JournalSource ReadJournalSource(
        string path,
        CancellationToken token,
        IProgress<ExplorationHistoryProgress>? progress = null
    )
    {
        long minimumCompleteLength = 0;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                JournalSource candidate = ReadJournalSnapshot(path, token, progress);
                if (candidate.CompleteLength < minimumCompleteLength)
                {
                    throw new JournalSnapshotUnstableException(candidate.Name, minimumCompleteLength);
                }
                return candidate;
            }
            catch (JournalSnapshotUnstableException failure)
            {
                minimumCompleteLength = Math.Max(minimumCompleteLength, failure.CompleteLength);
                if (attempt == 2)
                {
                    throw new JournalSnapshotUnstableException(failure.SourceName, minimumCompleteLength);
                }
                token.ThrowIfCancellationRequested();
            }
        }
        throw new IOException("The journal snapshot could not be captured.");
    }

    private static JournalSource ReadJournalSnapshot(
        string path,
        CancellationToken token,
        IProgress<ExplorationHistoryProgress>? progress
    )
    {
        var snapshot = new FileInfo(path);
        snapshot.Refresh();
        long completeLength;
        try
        {
            completeLength = ReadCompleteLength(path, snapshot.Length, token);
        }
        catch (EndOfStreamException)
        {
            throw new JournalSnapshotUnstableException(Path.GetFileName(path), 0);
        }
        progress?.Report(
            new ExplorationHistoryProgress(0, 0, Path.GetFileName(path), IsPreparing: true, IsDiscovering: true)
        );
        JournalLine? first = ReadLines(path, 0, token, snapshot.Length).FirstOrDefault();
        JournalEventEnvelope? header = null;
        if (first?.Content is { } text && JournalEventEnvelope.TryParse(text, out JournalEventEnvelope? parsed, out _))
        {
            header = parsed;
        }
        DateTimeOffset? firstTime = header?.Timestamp;
        var source = new JournalSource(
            Path.GetFileName(path),
            Path.GetFullPath(path),
            firstTime,
            ContentHash(first?.Content ?? string.Empty),
            snapshot.LastWriteTimeUtc.Ticks,
            snapshot.Length,
            completeLength,
            snapshot.CreationTimeUtc.Ticks,
            JournalFilePart.Read(path, header),
            header is null ? null : IsLiveHeader(header.Payload)
        );
        if (!SameSourceVersion(source))
        {
            throw new JournalSnapshotUnstableException(source.Name, source.CompleteLength);
        }
        return source;
    }

    /// <summary>Finds the terminal complete record using bounded tail reads, without decoding the committed journal prefix.</summary>
    private static long ReadCompleteLength(string path, long length, CancellationToken token)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] buffer = new byte[64 * 1024];
        long position = length;
        long boundary = 0;
        while (position > 0)
        {
            token.ThrowIfCancellationRequested();
            int count = (int)Math.Min(buffer.Length, position);
            position -= count;
            stream.Position = position;
            stream.ReadExactly(buffer.AsSpan(0, count));
            int newline = Array.LastIndexOf(buffer, (byte)'\n', count - 1, count);
            if (newline >= 0)
            {
                boundary = position + newline + 1;
                break;
            }
        }
        return
            length - boundary <= MaximumLineBytes
            && ReadLines(path, boundary, token, length, requireCapturedLength: true).Any()
            ? length
            : boundary;
    }

    private JournalSource? ResolveSourceCopies(IEnumerable<string> paths, CancellationToken token)
    {
        JournalSource? selected = null;
        foreach (string path in paths.Where(File.Exists))
        {
            JournalSource candidate = ReadJournalSource(path, token);
            selected = selected is null ? candidate : ChooseCompleteSource(selected, candidate, token);
        }
        return selected;
    }

    private JournalSource ChooseCompleteSource(JournalSource first, JournalSource second, CancellationToken token)
    {
        if (!MatchingPrefix(first, second, token))
        {
            throw new InvalidDataException(
                "Conflicting copies of journal "
                    + first.Name
                    + " were found. Choose compatible journal folders and refresh history; the existing catalog has been retained."
            );
        }
        if (first.Length != second.Length)
        {
            return first.Length > second.Length ? first : second;
        }
        // Keep a verified equal-length canonical copy stable when an archive catches up.
        // Switching solely because of copy timestamps can needlessly replay retained history.
        string? known = PreferredSourcePath(first.Name);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(first.Path, known, comparison))
        {
            return first;
        }
        if (string.Equals(second.Path, known, comparison))
        {
            return second;
        }
        return first.ModifiedTicks >= second.ModifiedTicks ? first : second;
    }

    private string? PreferredSourcePath(string name)
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }
        using SqliteConnection connection = Open(initialize: false);
        using SqliteCommand command = Command(
            connection,
            "SELECT path FROM sources WHERE name=@name;",
            ("@name", name)
        );
        return command.ExecuteScalar() as string;
    }

    /// <summary>Reuses only unchanged file snapshots; appends, replacements, and timestamp changes require a fresh full comparison.</summary>
    private bool MatchingPrefix(JournalSource first, JournalSource second, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (verifiedPrefixes.TryGetValue((first, second), out bool previous))
        {
            return previous;
        }
        bool matching = ReadMatchingPrefix(first, second, token);
        if (SameSourceVersion(first) && SameSourceVersion(second))
        {
            if (verifiedPrefixes.Count >= 64)
            {
                verifiedPrefixes.Clear();
            }
            verifiedPrefixes.TryAdd((first, second), matching);
        }
        return matching;
    }

    private static bool SameSourceVersion(JournalSource source)
    {
        var current = new FileInfo(source.Path);
        return current.Exists
            && current.Length == source.Length
            && current.LastWriteTimeUtc.Ticks == source.ModifiedTicks
            && current.CreationTimeUtc.Ticks == source.CreatedTicks;
    }

    private static bool ReadMatchingPrefix(JournalSource first, JournalSource second, CancellationToken token)
    {
        using var left = new FileStream(
            first.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        using var right = new FileStream(
            second.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        byte[] leftBytes = new byte[64 * 1024];
        byte[] rightBytes = new byte[64 * 1024];
        long remaining = Math.Min(first.Length, second.Length);
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            int count = (int)Math.Min(leftBytes.Length, remaining);
            left.ReadExactly(leftBytes.AsSpan(0, count));
            right.ReadExactly(rightBytes.AsSpan(0, count));
            if (!leftBytes.AsSpan(0, count).SequenceEqual(rightBytes.AsSpan(0, count)))
            {
                return false;
            }
            remaining -= count;
        }
        return true;
    }

    private static SourceCheckpoint ReadSource(SqliteConnection connection, JournalSource source)
    {
        Execute(
            connection,
            "INSERT INTO sources(name,path,first_time,prefix_hash) VALUES(@name,@path,@time,@hash) ON CONFLICT(name) DO UPDATE SET path=excluded.path;",
            ("@name", source.Name),
            ("@path", source.Path),
            (TimeParameter, Timestamp(source.FirstTime)),
            ("@hash", source.PrefixHash)
        );
        using SqliteCommand command = Command(
            connection,
            "SELECT id,next_offset,last_offset,length,last_hash,context FROM sources WHERE name=@name;",
            ("@name", source.Name)
        );
        using SqliteDataReader reader = command.ExecuteReader();
        reader.Read();
        return new SourceCheckpoint(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetString(4),
            JsonSerializer.Deserialize<SourceContext>(reader.GetString(5), ContextJson) ?? new()
        );
    }

    private static void SaveSource(SqliteConnection connection, JournalSource source, SourceCheckpoint checkpoint) =>
        Execute(
            connection,
            "UPDATE sources SET next_offset=@next,last_offset=@last,last_hash=@lastHash,length=@length,modified_ticks=@modified,context=@context,prefix_hash=@hash WHERE id=@id;",
            ("@next", checkpoint.NextOffset),
            ("@last", checkpoint.LastOffset),
            ("@lastHash", checkpoint.LastHash),
            ("@length", Math.Max(source.Length, checkpoint.NextOffset)),
            ("@modified", source.ModifiedTicks),
            ("@context", JsonSerializer.Serialize(checkpoint.Context, ContextJson)),
            ("@hash", source.PrefixHash),
            ("@id", checkpoint.Id)
        );

    private static string? Timestamp(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset parsed
        )
            ? parsed
            : null;

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? Integer(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out long parsed)
            ? parsed
            : null;

    private static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out double parsed)
            ? parsed
            : null;

    private static bool Flag(JsonElement root, string name, bool fallback = false) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
}
