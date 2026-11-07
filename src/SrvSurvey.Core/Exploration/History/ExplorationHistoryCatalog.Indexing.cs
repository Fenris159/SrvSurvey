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
        return IndexSources(DiscoverSources(directories, token), directories, progress, token);
    }

    private ExplorationHistorySummary IndexSources(
        JournalSource[] sources,
        IReadOnlyList<string> directories,
        IProgress<ExplorationHistoryProgress>? progress,
        CancellationToken token
    )
    {
        using SqliteConnection connection = Open();
        Execute(connection, "BEGIN IMMEDIATE;");
        try
        {
            if (NeedsRebuild(connection, sources, token))
            {
                sources = DiscoverSources(directories, token);
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
            SetMetadata(connection, "complete", "1");
            Execute(connection, "COMMIT;");
            return ReadSummary(connection);
        }
        catch
        {
            Execute(connection, "ROLLBACK;");
            throw;
        }
    }

    private static bool NeedsRebuild(SqliteConnection connection, JournalSource[] sources, CancellationToken token)
    {
        (string? Time, string? Name) latest = LatestSourceOrder(connection);
        foreach (JournalSource source in sources)
        {
            token.ThrowIfCancellationRequested();
            using SqliteCommand known = Command(
                connection,
                "SELECT prefix_hash,next_offset,last_offset,last_hash,length,modified_ticks FROM sources WHERE name=@name;",
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

    private static void Rebuild(SqliteConnection connection, JournalSource[] available)
    {
        var names = available.Select(source => source.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using (SqliteCommand existing = Command(connection, "SELECT name FROM sources;"))
        using (SqliteDataReader reader = existing.ExecuteReader())
        {
            while (reader.Read())
            {
                if (!names.Contains(reader.GetString(0)))
                {
                    throw new InvalidDataException(
                        "Journal history changed, but some indexed journals are missing. Restore those files and refresh history; the existing catalog has been retained."
                    );
                }
            }
        }
        Execute(
            connection,
            """
            DELETE FROM observations;
            DELETE FROM bodies;
            DELETE FROM batches;
            DELETE FROM settlements;
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
        if (checkpoint.NextOffset == 0)
        {
            using SqliteCommand prior = Command(
                connection,
                "SELECT context FROM sources WHERE id<>@id AND first_time<=@time ORDER BY first_time DESC,name DESC LIMIT 1;",
                ("@id", checkpoint.Id),
                (TimeParameter, Timestamp(source.FirstTime))
            );
            if (prior.ExecuteScalar() is string context)
            {
                checkpoint = checkpoint with
                {
                    Context = System.Text.Json.JsonSerializer.Deserialize<SourceContext>(context, ContextJson) ?? new(),
                };
            }
        }
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
            }
            checkpoint = checkpoint with
            {
                NextOffset = line.NextOffset,
                LastOffset = line.Offset,
                LastHash = line.Content is { } content ? ContentHash(content) : string.Empty,
            };
        }
        SaveSource(connection, source, checkpoint);
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
        JournalSource[] sources = names
            .Select(name => directories.Select(directory => Path.Combine(directory, name)).FirstOrDefault(File.Exists))
            .OfType<string>()
            .Select(path => ReadJournalSource(path, token))
            .OrderBy(source => source.FirstTime)
            .ThenBy(source => source.Name, StringComparer.Ordinal)
            .ToArray();
        return IndexSources(sources, directories, null, token);
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
