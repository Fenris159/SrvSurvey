using System.Globalization;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Exploration.History;

public sealed partial class ExplorationHistoryCatalog
{
    private ExplorationHistorySummary Synchronize(
        IReadOnlyList<string> directories,
        IProgress<ExplorationHistoryProgress>? progress,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(directories);
        return IndexSources(
            () => DiscoverSources(directories, token, progress),
            directories,
            progress,
            fullBackfill: true,
            token: token
        );
    }

    private ExplorationHistorySummary IndexSources(
        Func<JournalSource[]> discover,
        IReadOnlyList<string> directories,
        IProgress<ExplorationHistoryProgress>? progress,
        bool fullBackfill,
        CancellationToken token,
        bool confirmRecovery = false
    )
    {
        using SqliteConnection connection = Open();
        JournalSource[] sources;
        DeferredJournal[] required = [];
        DeferredJournal[] captured = [];
        DeferredJournal[] original = [];
        bool confirmationObligation = false;
        Execute(connection, "BEGIN IMMEDIATE;");
        Execute(connection, "SAVEPOINT recovery_batch;");
        try
        {
            sources = DiscoverSafely(connection, discover);
            bool previouslyComplete = ReadMetadata(connection, "complete") == "1";
            original = ReadDeferredJournals(connection);
            required = original.ToArray();
            bool confirmation = NeedsRecoveryConfirmation(connection, required);
            confirmationObligation = confirmation;
            if (confirmation && !confirmRecovery)
            {
                ExplorationHistorySummary paused = ReadSummary(connection);
                Execute(connection, "COMMIT;");
                return paused;
            }
            captured = CreateDeferredJournals(connection, sources, rebuild: false);
            if (required.Length > 0 || confirmation || ReadMetadata(connection, PendingDiscoveryKey) is not null)
            {
                sources = DiscoverSources(directories, token, progress);
                captured = captured
                    .Concat(CreateDeferredJournals(connection, sources, rebuild: false))
                    .Distinct()
                    .ToArray();
                PrepareDeferredJournals(
                    required,
                    sources,
                    token,
                    confirmRecovery,
                    contextConnection: connection,
                    reconstructContext: true
                );
                fullBackfill |= previouslyComplete;
            }
            bool rebuild =
                confirmation
                || ReadMetadata(connection, ValuationVersionKey) != CurrentValuationVersion
                || NeedsRebuild(connection, sources, token);
            if (rebuild)
            {
                sources = DiscoverSources(directories, token, progress);
                captured = captured
                    .Concat(CreateDeferredJournals(connection, sources, rebuild: false))
                    .Distinct()
                    .ToArray();
                PrepareDeferredJournals(
                    required,
                    sources,
                    token,
                    confirmRecovery,
                    contextConnection: connection,
                    reconstructContext: true
                );
                ValidateRebuildSources(connection, sources);
            }
            captured = captured.Concat(CreateDeferredJournals(connection, sources, rebuild)).Distinct().ToArray();
            PrepareDeferredJournals(captured, sources, token, progress: progress, contextConnection: connection);
            ValidatePendingDiscovery(connection, sources);
            ReplaySources(connection, sources, required.Concat(captured).ToArray(), rebuild, progress, token);
            SetMetadata(connection, ValuationVersionKey, CurrentValuationVersion);
            Execute(
                connection,
                "DELETE FROM metadata WHERE key IN (@pending,@confirmation,@discovery);",
                ("@pending", ReconciliationPendingKey),
                ("@confirmation", RecoveryConfirmationKey),
                ("@discovery", PendingDiscoveryKey)
            );
            if (fullBackfill || (rebuild && previouslyComplete))
            {
                SetMetadata(connection, "complete", "1");
            }
            ExplorationHistorySummary result = ReadSummary(connection);
            Execute(connection, "COMMIT;");
            return result;
        }
        catch (Exception failure)
        {
            PersistFailedReplay(connection, failure, original, required, captured, confirmationObligation);
            throw;
        }
    }

    private static void PersistFailedReplay(
        SqliteConnection connection,
        Exception failure,
        DeferredJournal[] original,
        DeferredJournal[] attempted,
        DeferredJournal[] captured,
        bool confirmationObligation
    )
    {
        Execute(connection, "ROLLBACK TO recovery_batch;");
        RetainDiscoveryFailure(connection, failure);
        if (
            confirmationObligation
            || attempted.Any(item => item.ConfirmationRequired)
            || captured.Any(item => item.ConfirmationRequired)
        )
        {
            SetMetadata(connection, RecoveryConfirmationKey, "1");
        }
        RetainDeferredJournals(connection, original, captured);
        Execute(connection, "COMMIT;");
    }

    private sealed record DiscoveryRequirement(string Name, long Length);

    private static JournalSource[] DiscoverSafely(SqliteConnection connection, Func<JournalSource[]> discover)
    {
        try
        {
            return discover();
        }
        catch (JournalSnapshotUnstableException failure)
        {
            RetainDiscoveryFailure(connection, failure);
            throw;
        }
    }

    private static void RetainDiscoveryFailure(SqliteConnection connection, Exception failure)
    {
        if (failure is not JournalSnapshotUnstableException unstable)
        {
            return;
        }
        DiscoveryRequirement[] known = ReadMetadata(connection, PendingDiscoveryKey) is { } json
            ? System.Text.Json.JsonSerializer.Deserialize<DiscoveryRequirement[]>(json, ContextJson) ?? []
            : [];
        DiscoveryRequirement[] required = known
            .Append(new(unstable.SourceName, unstable.CompleteLength))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Length).First())
            .ToArray();
        SetMetadata(connection, PendingDiscoveryKey, System.Text.Json.JsonSerializer.Serialize(required));
    }

    private static void ValidatePendingDiscovery(SqliteConnection connection, JournalSource[] sources)
    {
        if (ReadMetadata(connection, PendingDiscoveryKey) is not { } json)
        {
            return;
        }
        DiscoveryRequirement[] required =
            System.Text.Json.JsonSerializer.Deserialize<DiscoveryRequirement[]>(json, ContextJson) ?? [];
        DiscoveryRequirement? missing = required.FirstOrDefault(item =>
            !sources.Any(source =>
                string.Equals(source.Name, item.Name, StringComparison.OrdinalIgnoreCase)
                && source.CompleteLength >= item.Length
            )
        );
        if (missing is not null)
        {
            throw new InvalidDataException("Restore discovery source " + missing.Name + " before resuming history.");
        }
    }

    private void ReplaySources(
        SqliteConnection connection,
        JournalSource[] sources,
        DeferredJournal[] required,
        bool rebuild,
        IProgress<ExplorationHistoryProgress>? progress,
        CancellationToken token
    )
    {
        if (rebuild)
        {
            Rebuild(connection, sources);
        }
        int processed = 0;
        foreach (JournalSource source in sources)
        {
            token.ThrowIfCancellationRequested();
            IndexSource(connection, source, ReadSource(connection, source), token);
            progress?.Report(new ExplorationHistoryProgress(++processed, sources.Length, source.Name));
        }
        token.ThrowIfCancellationRequested();
        VerifyDeferredProgress(connection, required, sources, token);
    }

    private static bool NeedsRebuild(SqliteConnection connection, JournalSource[] sources, CancellationToken token)
    {
        (string? Time, string? Name) latest = LatestSourceOrder(connection);
        foreach (JournalSource source in sources)
        {
            token.ThrowIfCancellationRequested();
            using SqliteCommand known = Command(
                connection,
                "SELECT prefix_hash,next_offset,last_offset,last_hash,length,modified_ticks,path FROM sources WHERE name=@name;",
                ("@name", source.Name)
            );
            using SqliteDataReader reader = known.ExecuteReader();
            bool older = IsEarlierSource(source, latest);
            if (!reader.Read())
            {
                if (older)
                {
                    return true;
                }
                continue;
            }
            if (SourceChanged(reader, source, older, token))
            {
                return true;
            }
        }
        return false;
    }

    private static (string? Time, string? Name) LatestSourceOrder(SqliteConnection connection)
    {
        using SqliteCommand command = Command(
            connection,
            "SELECT first_time,name FROM sources ORDER BY first_time DESC,name DESC LIMIT 1;"
        );
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return (null, null);
        }
        string? time = reader.IsDBNull(0) ? null : reader.GetString(0);
        return (time, reader.GetString(1));
    }

    private static bool IsEarlierSource(JournalSource source, (string? Time, string? Name) latest)
    {
        int order = string.CompareOrdinal(Timestamp(source.FirstTime), latest.Time);
        return latest.Name is not null
            && (order < 0 || (order == 0 && string.CompareOrdinal(source.Name, latest.Name) < 0));
    }

    private static bool SourceChanged(
        SqliteDataReader reader,
        JournalSource source,
        bool older,
        CancellationToken token
    )
    {
        long next = reader.GetInt64(1);
        long last = reader.GetInt64(2);
        if (
            source.PrefixHash != reader.GetString(0)
            || source.Length < next
            || (older && source.Length > reader.GetInt64(4))
            || (source.Length == reader.GetInt64(4) && source.ModifiedTicks != reader.GetInt64(5))
        )
        {
            return true;
        }
        if (last >= 0 && reader.GetString(3) is { Length: > 0 } lastHash)
        {
            JournalLine? original = ReadLines(source.Path, last, token).FirstOrDefault();
            if (original?.Content is not { } text || ContentHash(text) != lastHash)
            {
                return true;
            }
        }
        return false;
    }

    private static void ValidateRebuildSources(SqliteConnection connection, JournalSource[] available)
    {
        Dictionary<string, JournalSource> sources = available.ToDictionary(
            source => source.Name,
            StringComparer.OrdinalIgnoreCase
        );
        using (SqliteCommand existing = Command(connection, "SELECT name,path,next_offset FROM sources;"))
        using (SqliteDataReader reader = existing.ExecuteReader())
        {
            while (reader.Read())
            {
                if (!sources.TryGetValue(reader.GetString(0), out JournalSource? source))
                {
                    throw new InvalidDataException(
                        "Journal history changed, but some indexed journals are missing. Restore those files and refresh history; the existing catalog has been retained."
                    );
                }
                if (
                    source.Length < reader.GetInt64(2)
                    && !string.Equals(
                        source.Path,
                        reader.GetString(1),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
                    )
                )
                {
                    throw new InvalidDataException(
                        "Only an incomplete copy of journal "
                            + source.Name
                            + " is available. Restore the complete journal and refresh history; the existing catalog has been retained."
                    );
                }
            }
        }
    }

    private static void Rebuild(SqliteConnection connection, JournalSource[] available)
    {
        ValidateRebuildSources(connection, available);
        Execute(
            connection,
            """
            DELETE FROM collection_anchors;
            DELETE FROM observations;
            DELETE FROM bodies;
            DELETE FROM batches;
            DELETE FROM settlements;
            DELETE FROM notable_systems;
            DELETE FROM systems;
            DELETE FROM sources;
            DELETE FROM metadata WHERE key NOT IN ('commander','galaxy');
            """
        );
    }

    private void IndexSource(
        SqliteConnection connection,
        JournalSource source,
        SourceCheckpoint checkpoint,
        CancellationToken token
    )
    {
        string notableKey = "notable_source:" + checkpoint.Id.ToString(CultureInfo.InvariantCulture);
        if (checkpoint.NextOffset > 0 && ReadMetadata(connection, notableKey) is null)
        {
            BackfillNotables(connection, source, checkpoint.NextOffset, token);
        }
        if (checkpoint.NextOffset == 0)
        {
            checkpoint = checkpoint with { Context = ReadContinuationContext(connection, source) };
        }
        checkpoint.Context.JournalPartNumber = source.FilePart?.Number;
        foreach (JournalLine line in ReadLines(source.Path, checkpoint.NextOffset, token, source.Length))
        {
            if (
                line.Content is { } text
                && JournalEventEnvelope.TryParse(text, out JournalEventEnvelope? entry, out _)
                && entry is not null
            )
            {
                ApplyEntry(
                    connection,
                    checkpoint.Context,
                    entry,
                    new OriginalReference(checkpoint.Id, line.Offset, ContentHash(text))
                );
                CaptureCommanderProof(checkpoint.Context, entry, source.Name, line.Offset, ContentHash(text));
            }
            checkpoint = checkpoint with
            {
                NextOffset = line.NextOffset,
                LastOffset = line.Offset,
                LastHash = line.Content is { } content ? ContentHash(content) : string.Empty,
            };
        }
        SaveSource(connection, source, checkpoint);
        SetMetadata(connection, notableKey, "1");
    }

    private static SourceContext ReadContinuationContext(SqliteConnection connection, JournalSource source)
    {
        if (source.FilePart?.PreviousPath is not { } previous || source.IsLive is null)
        {
            return new();
        }
        using SqliteCommand command = Command(
            connection,
            "SELECT context FROM sources WHERE name=@previous COLLATE NOCASE AND first_time<=@time;",
            ("@previous", Path.GetFileName(previous)),
            (TimeParameter, Timestamp(source.FirstTime))
        );
        if (command.ExecuteScalar() is not string json)
        {
            return new();
        }
        SourceContext context = System.Text.Json.JsonSerializer.Deserialize<SourceContext>(json, ContextJson) ?? new();
        return
            context.JournalPartNumber == source.FilePart.Number - 1
            && context.IsLive == source.IsLive
            && !string.IsNullOrWhiteSpace(context.FrontierId)
            ? context
            : new();
    }

    private ExplorationHistorySummary Observe(
        IReadOnlyList<JournalEventEnvelope> events,
        IReadOnlyList<string> directories,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(directories);
        // Read the appended source rather than trusting that a UI update includes its
        // Fileheader, Commander, Location, or every intervening sale and death record.
        string[] names = events
            .Select(entry => TrySourceIdentity(entry.SourceRecordIdentity, out string name, out _) ? name : null)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return IndexSources(
            () =>
                names
                    .Select(name =>
                        ResolveSourceCopies(directories.Select(directory => Path.Combine(directory, name)), token)
                    )
                    .OfType<JournalSource>()
                    .OrderBy(source => source.FirstTime)
                    .ThenBy(source => source.Name, StringComparer.Ordinal)
                    .ToArray(),
            directories,
            null,
            fullBackfill: false,
            token: token
        );
    }

    private static bool TrySourceIdentity(string? identity, out string name, out long offset)
    {
        int separator = identity?.LastIndexOf(':') ?? -1;
        name = separator > 0 ? Path.GetFileName(identity![..separator]) : string.Empty;
        offset = 0;
        return name.Length > 0
            && long.TryParse(identity![(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out offset);
    }
}
