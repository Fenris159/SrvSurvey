using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SrvSurvey.Core.Exploration.History;

public sealed partial class ExplorationHistoryCatalog
{
    /// <summary>Only journal identities, complete-record boundaries, and fingerprints are retained while a batch is deferred.</summary>
    private sealed record DeferredJournal(
        string Name,
        long Length,
        string HeaderHash,
        string? PrefixHash,
        long? RecordOffset,
        string? RecordHash
    );

    private static DeferredJournal[] ReadDeferredJournals(SqliteConnection connection)
    {
        string? value = ReadMetadata(connection, ReconciliationPendingKey);
        // The previous boolean marker requires a protected full rebuild, enforced by IndexSources.
        if (value is null or "1")
        {
            return [];
        }
        try
        {
            DeferredJournal[]? required = JsonSerializer.Deserialize<DeferredJournal[]>(value, ContextJson);
            if (
                required is not { Length: > 0 }
                || required.Any(item => item is null || string.IsNullOrWhiteSpace(item.Name) || item.Length < 0)
            )
            {
                throw new InvalidDataException(
                    "The deferred journal identities are unavailable. Restore the original journals and rebuild the derived history index."
                );
            }
            return required;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The deferred journal identities require a rebuild from restored original journals.",
                exception
            );
        }
    }

    private static void WriteDeferredJournals(SqliteConnection connection, DeferredJournal[] required) =>
        SetMetadata(connection, ReconciliationPendingKey, JsonSerializer.Serialize(required));

    /// <summary>Capture only journals with uncommitted bytes before replay can invoke callbacks or external files can disappear.</summary>
    private static DeferredJournal[] CaptureDeferredJournals(
        SqliteConnection connection,
        JournalSource[] sources,
        CancellationToken token
    )
    {
        var captured = new List<DeferredJournal>();
        foreach (JournalSource source in sources)
        {
            using SqliteCommand command = Command(
                connection,
                "SELECT length FROM sources WHERE name=@name;",
                (NameParameter, source.Name)
            );
            if (command.ExecuteScalar() is long committedLength && committedLength == source.Length)
            {
                continue;
            }
            long length = ReadLines(source.Path, 0, token, source.Length, requireCapturedLength: true)
                .Select(line => line.NextOffset)
                .LastOrDefault();
            string hash = HashJournalPrefix(source.Path, length, token);
            if (!SameSourceVersion(source))
            {
                throw new IOException(
                    "A journal changed while capturing recovery records. History will retry when the file is stable."
                );
            }
            if (length > 0)
            {
                captured.Add(new(source.Name, length, source.PrefixHash, hash, null, null));
            }
        }
        return captured.ToArray();
    }

    /// <summary>Rollback retains every captured uncommitted source without relying on files remaining available afterward.</summary>
    private static void RetainDeferredJournals(
        SqliteConnection connection,
        DeferredJournal[] captured,
        JournalSource? interrupted,
        OriginalScanUnavailableException failure
    )
    {
        DeferredJournal[] required = ReadDeferredJournals(connection)
            .Concat(
                captured.Select(source =>
                    source.Name == interrupted?.Name
                        ? source with
                        {
                            RecordOffset = failure.Record.Offset,
                            RecordHash = failure.Record.Hash,
                        }
                        : source
                )
            )
            .Distinct()
            .ToArray();
        WriteDeferredJournals(connection, required);
    }

    private static DeferredJournal[] PrepareDeferredJournals(
        DeferredJournal[] required,
        JournalSource[] sources,
        CancellationToken token
    )
    {
        Dictionary<string, JournalSource> available = sources.ToDictionary(
            source => source.Name,
            StringComparer.OrdinalIgnoreCase
        );
        return required
            .Where(item => item.Length > 0)
            .Select(item => VerifyDeferredJournal(item, available, token))
            .Where(item => item.Length > 0)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Length).First())
            .ToArray();
    }

    private static DeferredJournal VerifyDeferredJournal(
        DeferredJournal required,
        Dictionary<string, JournalSource> available,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        if (!available.TryGetValue(required.Name, out JournalSource? source) || source.Length < required.Length)
        {
            throw new InvalidDataException(
                "Restore deferred journal "
                    + required.Name
                    + " before resuming history; the existing catalog has been retained."
            );
        }
        long length = required.Length;
        if (source.PrefixHash != required.HeaderHash)
        {
            throw new InvalidDataException(
                "The deferred header changed in "
                    + required.Name
                    + "; restore the captured journal before resuming history."
            );
        }
        if (required.RecordOffset is { } offset && required.RecordHash is { } expected)
        {
            JournalLine? record = ReadLines(source.Path, offset, token, source.Length).FirstOrDefault();
            if (record?.Content is not { } content || ContentHash(content) != expected)
            {
                throw new InvalidDataException(
                    "A deferred mapping record changed in "
                        + required.Name
                        + "; restore the captured journal before resuming history."
                );
            }
        }
        string hash = HashJournalPrefix(source.Path, length, token);
        if (required.PrefixHash is { } prefix && hash != prefix)
        {
            throw new InvalidDataException(
                "Captured journal records changed in " + required.Name + "; restore them before resuming history."
            );
        }
        if (!SameSourceVersion(source))
        {
            throw new IOException(
                "Journal "
                    + required.Name
                    + " changed during recovery verification. History will retry when the file is stable."
            );
        }
        return required with { Name = source.Name, Length = length, PrefixHash = hash };
    }

    private static string HashJournalPrefix(string path, long length, CancellationToken token)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        while (length > 0)
        {
            token.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read == 0)
            {
                throw new EndOfStreamException("A deferred journal became incomplete during recovery verification.");
            }
            hash.AppendData(buffer, 0, read);
            length -= read;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void VerifyDeferredProgress(
        SqliteConnection connection,
        DeferredJournal[] required,
        JournalSource[] sources,
        CancellationToken token
    )
    {
        Dictionary<string, JournalSource> available = sources.ToDictionary(
            source => source.Name,
            StringComparer.OrdinalIgnoreCase
        );
        foreach (DeferredJournal source in required)
        {
            using SqliteCommand command = Command(
                connection,
                "SELECT next_offset FROM sources WHERE name=@name;",
                ("@name", source.Name)
            );
            if (command.ExecuteScalar() is not long offset || offset < source.Length)
            {
                throw new InvalidDataException(
                    "Complete records from " + source.Name + " have not replayed; history recovery remains pending."
                );
            }
            if (
                !available.TryGetValue(source.Name, out JournalSource? current)
                || source.PrefixHash is null
                || HashJournalPrefix(current.Path, source.Length, token) != source.PrefixHash
            )
            {
                throw new InvalidDataException(
                    "Deferred records in "
                        + source.Name
                        + " changed during replay; the existing catalog has been retained."
                );
            }
        }
    }
}
