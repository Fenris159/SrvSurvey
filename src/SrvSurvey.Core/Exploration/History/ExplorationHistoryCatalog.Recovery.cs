using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Exploration.History;

public sealed partial class ExplorationHistoryCatalog
{
    /// <summary>Only source identities and the uncommitted complete-record range are retained for deferred replay.</summary>
    private sealed record DeferredJournal(
        string Name,
        long Length,
        string HeaderHash,
        string? PrefixHash,
        long StartOffset = 0,
        int FormatVersion = 0,
        long ModifiedTicks = 0,
        long CreatedTicks = 0,
        long RawLength = 0,
        bool ConfirmationRequired = false,
        long? RecordOffset = null,
        string? RecordHash = null,
        string? CheckpointContext = null
    );

    private static DeferredJournal[] ReadDeferredJournals(SqliteConnection connection)
    {
        string? value = ReadMetadata(connection, ReconciliationPendingKey);
        if (value is null or "1")
        {
            return [];
        }
        try
        {
            DeferredJournal[]? required = JsonSerializer.Deserialize<DeferredJournal[]>(value, ContextJson);
            if (
                required is not { Length: > 0 }
                || required.Any(item =>
                    item is null
                    || string.IsNullOrWhiteSpace(item.Name)
                    || item.StartOffset < 0
                    || item.Length < item.StartOffset
                )
            )
            {
                throw new InvalidDataException(
                    "The deferred journal identities are unavailable. Restore the original journals before recovery."
                );
            }
            return required;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The deferred journal identities are unavailable.", exception);
        }
    }

    private static bool NeedsRecoveryConfirmation(SqliteConnection connection, DeferredJournal[] required) =>
        ReadMetadata(connection, RecoveryConfirmationKey) == "1"
        || ReadMetadata(connection, ReconciliationPendingKey) == "1"
        || required.Any(item => item.ConfirmationRequired || (item.FormatVersion == 0 && item.PrefixHash is null));

    private static void RetainDeferredJournals(SqliteConnection connection, params DeferredJournal[][] batches)
    {
        DeferredJournal[] required = CompactDeferredJournals(
            ReadDeferredJournals(connection).Concat(batches.SelectMany(batch => batch))
        );
        if (required.Any(item => item.ConfirmationRequired))
        {
            SetMetadata(connection, RecoveryConfirmationKey, "1");
        }
        if (required.Length > 0)
        {
            SetMetadata(connection, ReconciliationPendingKey, JsonSerializer.Serialize(required));
        }
    }

    private static DeferredJournal[] CompactDeferredJournals(IEnumerable<DeferredJournal> requirements)
    {
        var retained = new List<DeferredJournal>();
        foreach (
            IGrouping<string, DeferredJournal> group in requirements
                .Distinct()
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        )
        {
            DeferredJournal[] known = group
                .Where(item => item.PrefixHash is not null)
                .Select(item =>
                    item with
                    {
                        ModifiedTicks = 0,
                        CreatedTicks = 0,
                        RawLength = 0,
                        ConfirmationRequired = false,
                    }
                )
                .Distinct()
                .ToArray();
            retained.AddRange(
                known.Where(item =>
                    !known.Any(other =>
                        other != item
                        && other.HeaderHash == item.HeaderHash
                        && other.StartOffset <= item.StartOffset
                        && other.Length >= item.Length
                        && (other.StartOffset < item.StartOffset || other.Length > item.Length)
                    )
                )
            );
            DeferredJournal[] unknown = group.Where(item => item.PrefixHash is null).ToArray();
            RetainUnknownEnvelope(retained, unknown, known);
        }
        return retained.ToArray();
    }

    private static void RetainUnknownEnvelope(
        List<DeferredJournal> retained,
        DeferredJournal[] unknown,
        DeferredJournal[] known
    )
    {
        if (unknown.Length > 0)
        {
            DeferredJournal first = unknown[0];
            bool changed = unknown.Any(item =>
                item.ConfirmationRequired
                || item.HeaderHash != first.HeaderHash
                || item.ModifiedTicks != first.ModifiedTicks
                || item.CreatedTicks != first.CreatedTicks
                || item.RawLength != first.RawLength
            );
            // Unknown history is an obligation, even when a later known range happens to contain it.
            DeferredJournal envelope = first with
            {
                StartOffset = unknown.Min(item => item.StartOffset),
                Length = unknown.Max(item => item.Length),
                ConfirmationRequired = changed,
            };
            bool covered = known.Any(item =>
                item.HeaderHash == envelope.HeaderHash
                && item.StartOffset <= envelope.StartOffset
                && item.Length >= envelope.Length
            );
            if (changed || !covered)
            {
                retained.Add(envelope);
                retained.AddRange(unknown.Where(item => item.RecordHash is not null && item != first));
            }
        }
    }

    private static DeferredJournal[] CreateDeferredJournals(
        SqliteConnection connection,
        JournalSource[] sources,
        bool rebuild
    )
    {
        var required = new List<DeferredJournal>();
        foreach (JournalSource source in sources)
        {
            long start = 0;
            string? context = null;
            if (!rebuild)
            {
                using SqliteCommand command = Command(
                    connection,
                    "SELECT context FROM sources WHERE name=@name;",
                    (NameParameter, source.Name)
                );
                if (command.ExecuteScalar() is string stored)
                {
                    context = stored;
                    using SqliteCommand offset = Command(
                        connection,
                        "SELECT next_offset FROM sources WHERE name=@name;",
                        (NameParameter, source.Name)
                    );
                    start = (long)offset.ExecuteScalar()!;
                }
            }
            if (source.CompleteLength > start)
            {
                required.Add(
                    new(
                        source.Name,
                        source.CompleteLength,
                        source.PrefixHash,
                        null,
                        start,
                        1,
                        source.ModifiedTicks,
                        source.CreatedTicks,
                        source.Length,
                        CheckpointContext: context
                    )
                );
            }
        }
        return required.ToArray();
    }

    private static void PrepareDeferredJournals(
        DeferredJournal[] required,
        JournalSource[] sources,
        CancellationToken token,
        bool confirmed = false,
        IProgress<ExplorationHistoryProgress>? progress = null,
        SqliteConnection? contextConnection = null,
        bool reconstructContext = false
    )
    {
        Dictionary<string, JournalSource> available = sources.ToDictionary(
            source => source.Name,
            StringComparer.OrdinalIgnoreCase
        );
        for (int index = 0; index < required.Length; index++)
        {
            DeferredJournal item = required[index];
            if (
                item.PrefixHash is null
                && !confirmed
                && available.TryGetValue(item.Name, out JournalSource? source)
                && (
                    item.ModifiedTicks != source.ModifiedTicks
                    || item.CreatedTicks != source.CreatedTicks
                    || item.RawLength != source.Length
                )
            )
            {
                required[index] = item with { ConfirmationRequired = true };
                throw new InvalidDataException(
                    "An unreadable journal changed before its records could be verified. Restore all original journals and confirm recovery."
                );
            }
            required[index] = VerifyDeferredJournal(item, available, confirmed, token);
            if (contextConnection is not null)
            {
                ValidateCommittedContext(contextConnection, required[index], available, reconstructContext, token);
            }
            progress?.Report(new ExplorationHistoryProgress(index + 1, required.Length, item.Name, IsPreparing: true));
        }
    }

    private static DeferredJournal VerifyDeferredJournal(
        DeferredJournal required,
        Dictionary<string, JournalSource> available,
        bool confirmed,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        if (!available.TryGetValue(required.Name, out JournalSource? source))
        {
            throw new InvalidDataException(
                "Restore deferred journal "
                    + required.Name
                    + " before resuming history; the existing catalog has been retained."
            );
        }
        VerifyLegacyRecord(required, source, token);
        required = NormalizeConfirmedLegacy(required, source, confirmed, token);
        if (source.PrefixHash != required.HeaderHash)
        {
            throw new InvalidDataException(
                "The deferred header changed in "
                    + required.Name
                    + "; restore the original journal before resuming history."
            );
        }
        long length = required.Length;
        if (source.CompleteLength < length)
        {
            throw new InvalidDataException(
                "Complete records from "
                    + required.Name
                    + " are missing; restore the captured journal before resuming history."
            );
        }
        string hash = HashJournalRange(source.Path, required.StartOffset, length, token);
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
        return required with
        {
            Name = source.Name,
            Length = length,
            PrefixHash = hash,
            FormatVersion = 1,
            ConfirmationRequired = false,
        };
    }

    private static void VerifyLegacyRecord(DeferredJournal required, JournalSource source, CancellationToken token)
    {
        if (required.RecordOffset is not { } offset || required.RecordHash is not { } expected)
        {
            return;
        }
        JournalLine? record = ReadLines(source.Path, offset, token, source.Length).FirstOrDefault();
        if (
            required.PrefixHash is not null
            && required.StartOffset <= offset
            && record is { } captured
            && captured.NextOffset <= required.Length
        )
        {
            return;
        }
        if (record?.Content is not { } content || ContentHash(content) != expected)
        {
            throw new InvalidDataException(
                "A verified legacy record changed in "
                    + required.Name
                    + "; restore the original journal before recovery."
            );
        }
    }

    private static DeferredJournal NormalizeConfirmedLegacy(
        DeferredJournal required,
        JournalSource source,
        bool confirmed,
        CancellationToken token
    )
    {
        if (!confirmed || required.FormatVersion != 0 || required.PrefixHash is not null)
        {
            return required;
        }
        long length = ReadLines(source.Path, 0, token, Math.Min(required.Length, source.Length))
            .Select(line => line.NextOffset)
            .LastOrDefault();
        return required with
        {
            Length = length,
            HeaderHash = required.HeaderHash == ContentHash(string.Empty) ? source.PrefixHash : required.HeaderHash,
        };
    }

    private static string HashJournalRange(string path, long start, long end, CancellationToken token)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(start, SeekOrigin.Begin);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        long remaining = end - start;
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                throw new EndOfStreamException("A deferred journal became incomplete during recovery verification.");
            }
            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void CaptureCommanderProof(
        SourceContext context,
        JournalEventEnvelope entry,
        string source,
        long offset,
        string hash
    )
    {
        if (entry.EventName is "Commander" or "LoadGame" && Text(entry.Payload, "FID") is not null)
        {
            context.CommanderRecord = new(source, offset, hash);
        }
    }

    private static void ValidateCommittedContext(
        SqliteConnection connection,
        DeferredJournal required,
        Dictionary<string, JournalSource> available,
        bool reconstruct,
        CancellationToken token
    )
    {
        if (required.StartOffset == 0 || required.CheckpointContext is null)
        {
            return;
        }
        SourceContext expected =
            JsonSerializer.Deserialize<SourceContext>(required.CheckpointContext, ContextJson) ?? new();
        VerifyCommanderProof(connection, expected.CommanderRecord, required.Name, available, token);
        if (!reconstruct || !available.TryGetValue(required.Name, out JournalSource? source))
        {
            return;
        }
        SourceContext actual = ReadContinuationContext(connection, source);
        actual.JournalPartNumber = source.FilePart?.Number;
        foreach (
            JournalLine line in ReadLines(source.Path, 0, token, required.StartOffset, requireCapturedLength: true)
        )
        {
            if (
                line.Content is { } text
                && JournalEventEnvelope.TryParse(text, out JournalEventEnvelope? entry, out _)
                && entry is not null
            )
            {
                ApplyContext(actual, entry);
                CaptureCommanderProof(actual, entry, source.Name, line.Offset, ContentHash(text));
            }
        }
        if (JsonSerializer.Serialize(actual) != JsonSerializer.Serialize(expected))
        {
            throw new InvalidDataException(
                "The committed context changed in " + required.Name + "; restore the original journal before recovery."
            );
        }
    }

    private static void VerifyCommanderProof(
        SqliteConnection connection,
        CommanderProof? proof,
        string requiredName,
        Dictionary<string, JournalSource> available,
        CancellationToken token
    )
    {
        if (proof is null)
        {
            return;
        }
        using SqliteCommand command = Command(
            connection,
            "SELECT path FROM sources WHERE name=@name;",
            (NameParameter, proof.Name)
        );
        string? path = available.TryGetValue(proof.Name, out JournalSource? source)
            ? source.Path
            : command.ExecuteScalar() as string;
        // A retained parent context can remain usable while its archived source is absent.
        if (path is null || !File.Exists(path))
        {
            if (string.Equals(proof.Name, requiredName, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "The committed commander source is unavailable. Restore the journal before recovery."
                );
            }
            return;
        }
        JournalLine? record = ReadLines(path, proof.Offset, token).FirstOrDefault();
        if (record?.Content is not { } text || ContentHash(text) != proof.Hash)
        {
            throw new InvalidDataException(
                "The committed commander changed in " + proof.Name + "; restore the original journal before recovery."
            );
        }
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
            ValidateCommittedContext(connection, source, available, reconstruct: false, token);
            using SqliteCommand command = Command(
                connection,
                "SELECT next_offset FROM sources WHERE name=@name;",
                (NameParameter, source.Name)
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
                || HashJournalRange(current.Path, source.StartOffset, source.Length, token) != source.PrefixHash
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
