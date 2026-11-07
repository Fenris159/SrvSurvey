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
        if (value is null)
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

    /// <summary>Rollback also undoes earlier sources in the batch, so every captured source remains required until a successful replay.</summary>
    private static void RetainDeferredJournals(
        SqliteConnection connection,
        JournalSource[] sources,
        JournalSource? interrupted,
        OriginalScanUnavailableException failure,
        CancellationToken token
    )
    {
        DeferredJournal[] required = ReadDeferredJournals(connection)
            .Concat(
                sources.Select(source => new DeferredJournal(
                    source.Name,
                    source.Length,
                    source.PrefixHash,
                    null,
                    source.Name == interrupted?.Name ? failure.Record.Offset : null,
                    source.Name == interrupted?.Name ? failure.Record.Hash : null
                ))
            )
            .Distinct()
            .ToArray();
        // Save identities before optional fingerprint I/O: disappearing files or cancellation must not lose the retry.
        WriteDeferredJournals(connection, required);
        try
        {
            WriteDeferredJournals(connection, PrepareDeferredJournals(required, sources, token));
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or OperationCanceledException
            )
        {
            // The durable requirements remain incomplete; a later retry can finish their fingerprints when files settle.
            return;
        }
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
        // An unfinished first record has no data to preserve, even if its header later completes.
        long length = required.PrefixHash is null
            ? ReadLines(source.Path, 0, token, required.Length).Select(line => line.NextOffset).LastOrDefault()
            : required.Length;
        if (length == 0 && required.RecordOffset is null)
        {
            return required with { Length = 0 };
        }
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
