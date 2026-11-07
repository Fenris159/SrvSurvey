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

    private sealed record JournalLine(long Offset, long NextOffset, string? Content);

    private sealed record OriginalReference(long SourceId, long Offset, string Hash);

    private sealed record JournalSource(
        string Name,
        string Path,
        DateTimeOffset? FirstTime,
        string PrefixHash,
        long ModifiedTicks,
        long Length
    );

    private sealed class SourceContext
    {
        public string? FrontierId { get; set; }
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
        long? maximumOffset = null
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
        if (ReadTail(pending, oversized) is { } tail)
        {
            yield return new JournalLine(lineOffset, position, tail);
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

    private static JournalEventEnvelope? ReadOriginal(SqliteConnection connection, OriginalReference reference)
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
            JournalLine? line = ReadLines(path, reference.Offset, CancellationToken.None).FirstOrDefault();
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

    private static JournalSource[] DiscoverSources(
        IReadOnlyList<string> directories,
        CancellationToken cancellationToken
    )
    {
        var found = new Dictionary<string, JournalSource>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in directories.Where(Directory.Exists))
        {
            foreach (string path in Directory.EnumerateFiles(directory, "Journal.*.log", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = Path.GetFileName(path);
                if (found.ContainsKey(name))
                {
                    continue;
                }
                found[name] = ReadJournalSource(path, cancellationToken);
            }
        }
        return found
            .Values.OrderBy(source => source.FirstTime)
            .ThenBy(source => source.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static JournalSource ReadJournalSource(string path, CancellationToken token)
    {
        JournalLine? first = ReadLines(path, 0, token).FirstOrDefault();
        DateTimeOffset? firstTime =
            first?.Content is { } text && JournalEventEnvelope.TryParse(text, out JournalEventEnvelope? entry, out _)
                ? entry?.Timestamp
                : null;
        var snapshot = new FileInfo(path);
        snapshot.Refresh();
        return new JournalSource(
            Path.GetFileName(path),
            Path.GetFullPath(path),
            firstTime,
            ContentHash(first?.Content ?? string.Empty),
            snapshot.LastWriteTimeUtc.Ticks,
            snapshot.Length
        );
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
