using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

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
        bool ConfirmationRequired = false
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
        ReadMetadata(connection, ReconciliationPendingKey) == "1"
        || required.Any(item => item.ConfirmationRequired || (item.FormatVersion == 0 && item.PrefixHash is null));

    private static void RetainDeferredJournals(SqliteConnection connection, params DeferredJournal[][] batches)
    {
        DeferredJournal[] required = CompactDeferredJournals(
            ReadDeferredJournals(connection).Concat(batches.SelectMany(batch => batch))
        );
        if (required.Length > 0)
        {
            SetMetadata(connection, ReconciliationPendingKey, JsonSerializer.Serialize(required));
        }
    }

    private static DeferredJournal[] CompactDeferredJournals(IEnumerable<DeferredJournal> requirements)
    {
        var retained = new List<DeferredJournal>();
        foreach (
            DeferredJournal item in requirements
                .Distinct()
                .OrderByDescending(item => item.PrefixHash is not null)
                .ThenBy(item => item.StartOffset)
                .ThenByDescending(item => item.Length)
                .ThenByDescending(item => item.FormatVersion)
                .Where(item =>
                    !retained.Any(known =>
                        known.PrefixHash is not null
                        && string.Equals(known.Name, item.Name, StringComparison.OrdinalIgnoreCase)
                        && known.HeaderHash == item.HeaderHash
                        && known.StartOffset <= item.StartOffset
                        && known.Length >= item.Length
                    )
                )
        )
        {
            retained.Add(item);
        }
        return retained.ToArray();
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
            if (!rebuild)
            {
                using SqliteCommand command = Command(
                    connection,
                    "SELECT next_offset FROM sources WHERE name=@name;",
                    (NameParameter, source.Name)
                );
                start = command.ExecuteScalar() is long committed ? committed : 0;
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
                        source.Length
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
        IProgress<ExplorationHistoryProgress>? progress = null
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
        return required with { Length = length, HeaderHash = length == 0 ? source.PrefixHash : required.HeaderHash };
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
