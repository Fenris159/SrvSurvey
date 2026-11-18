using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SrvSurvey.Core.Exploration.History;

public sealed partial class ExplorationHistoryCatalog
{
    private sealed record SurveySource(string Name, string Path, long Length, bool AllowCachedBoundary = false);

    private sealed record SurveyIndexedSource(string Name, long Offset, string Hash, string? FirstTime);

    /// <summary>A supplied consumer frontier leaves omitted sources unread; callers may explicitly acknowledge older closed journals.</summary>
    public Task<ExplorationSurveyResetPoint> CaptureSurveyResetPointAsync(
        IReadOnlyList<ExplorationSurveySourceCheckpoint>? consumed = null,
        CancellationToken cancellationToken = default
    ) => CaptureSurveyResetPointAsync(consumed, includeEarlierSources: false, cancellationToken);

    public Task<ExplorationSurveyResetPoint> CaptureSurveyResetPointAsync(
        IReadOnlyList<ExplorationSurveySourceCheckpoint>? consumed,
        bool includeEarlierSources,
        CancellationToken cancellationToken = default
    ) =>
        Task.Run(
            () =>
            {
                using SqliteConnection connection = Open();
                using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
                ExplorationSurveyResetPoint point = CaptureSurveyResetPoint(
                    connection,
                    consumed,
                    includeEarlierSources,
                    cancellationToken
                );
                transaction.Commit();
                return point;
            },
            cancellationToken
        );

    public Task<ExplorationHistorySummary> ApplySurveyResetPointAsync(
        ExplorationSurveyResetPoint point,
        CancellationToken cancellationToken = default
    ) =>
        Task.Run(
            () =>
            {
                using SqliteConnection connection = Open();
                using SqliteTransaction transaction = connection.BeginTransaction();
                ApplySurveyResetPoint(connection, point, cancellationToken);
                ExplorationHistorySummary result = ReadSummary(connection);
                transaction.Commit();
                return result;
            },
            cancellationToken
        );

    public Task<ExplorationHistorySummary> ResetSurveyValueAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                using SqliteConnection connection = Open();
                using SqliteTransaction transaction = connection.BeginTransaction();
                ExplorationSurveyResetPoint point = CaptureSurveyResetPoint(connection, null, false, cancellationToken);
                ApplySurveyResetPoint(connection, point, cancellationToken);
                ExplorationHistorySummary result = ReadSummary(connection);
                transaction.Commit();
                return result;
            },
            cancellationToken
        );

    private ExplorationSurveyResetPoint CaptureSurveyResetPoint(
        SqliteConnection connection,
        IReadOnlyList<ExplorationSurveySourceCheckpoint>? consumed,
        bool includeEarlierSources,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        if (!ReadSummary(connection).IsComplete)
        {
            throw new InvalidOperationException(
                "Finish exploration history recovery before resetting the automatically tracked value."
            );
        }

        SurveyIndexedSource[] sources = ReadIndexedSurveySources(connection);
        SurveyIndexedSource? latestConsumed = consumed is null
            ? null
            : sources
                .Where(source =>
                    consumed.Any(item =>
                        string.Equals(item.SourceName, source.Name, StringComparison.OrdinalIgnoreCase)
                    )
                )
                .OrderBy(source => source.FirstTime, StringComparer.Ordinal)
                .ThenBy(source => source.Name, StringComparer.Ordinal)
                .LastOrDefault();
        if (includeEarlierSources && consumed is not null && latestConsumed is null)
        {
            throw new InvalidOperationException(
                "Refresh exploration history until the consumed journal source is available before resetting value."
            );
        }
        ExplorationSurveySourceCheckpoint[] points = sources
            .Select(source =>
                SurveyCheckpointForConsumer(source, consumed, includeEarlierSources ? latestConsumed : null)
            )
            .ToArray();
        string? catalogGeneration = ReadMetadata(connection, CatalogGenerationKey);
        DateTimeOffset now = timeProvider.GetUtcNow();
        string identity = ContentHash(
            JsonSerializer.Serialize(
                new
                {
                    frontierId,
                    isLive,
                    now,
                    points,
                    catalogGeneration,
                }
            )
        );
        return new(identity, frontierId, isLive, now, points) { CatalogGeneration = catalogGeneration };
    }

    private static SurveyIndexedSource[] ReadIndexedSurveySources(SqliteConnection connection)
    {
        using SqliteCommand command = Command(
            connection,
            "SELECT name,last_offset,last_hash,first_time FROM sources ORDER BY name;"
        );
        using SqliteDataReader reader = command.ExecuteReader();
        var sources = new List<SurveyIndexedSource>();
        while (reader.Read())
        {
            sources.Add(
                new(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)
                )
            );
        }
        return sources.ToArray();
    }

    private static ExplorationSurveySourceCheckpoint SurveyCheckpointForConsumer(
        SurveyIndexedSource source,
        IReadOnlyList<ExplorationSurveySourceCheckpoint>? consumed,
        SurveyIndexedSource? latestAcknowledged
    )
    {
        bool earlier =
            latestAcknowledged is not null
            && (
                string.CompareOrdinal(source.FirstTime, latestAcknowledged.FirstTime) < 0
                || (
                    source.FirstTime == latestAcknowledged.FirstTime
                    && string.CompareOrdinal(source.Name, latestAcknowledged.Name) < 0
                )
            );
        if (consumed is null || earlier)
        {
            return new(source.Name, source.Offset, source.Hash);
        }
        return consumed.SingleOrDefault(item =>
                string.Equals(item.SourceName, source.Name, StringComparison.OrdinalIgnoreCase)
            ) ?? new(source.Name, -1, string.Empty);
    }

    private void ApplySurveyResetPoint(
        SqliteConnection connection,
        ExplorationSurveyResetPoint point,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(point);
        token.ThrowIfCancellationRequested();
        if (!point.IsValidForScope(frontierId, isLive))
        {
            throw new InvalidDataException(
                "The saved survey starting point does not belong to this commander and galaxy."
            );
        }

        using (SqliteCommand existing = Command(connection, "SELECT point_id FROM survey_value_period WHERE id=1;"))
        {
            if (existing.ExecuteScalar() is string identity && identity == point.Id)
            {
                return;
            }
        }
        Execute(
            connection,
            "DELETE FROM survey_value_checkpoints; DELETE FROM survey_value_period; INSERT INTO survey_value_period(id,point_id,reset_utc) VALUES(1,@id,@time);",
            ("@id", point.Id),
            (TimeParameter, Timestamp(point.CreatedAt))
        );
        foreach (ExplorationSurveySourceCheckpoint item in point.Sources)
        {
            Execute(
                connection,
                "INSERT INTO survey_value_checkpoints(source_name,offset,hash,valid) VALUES(@name,@offset,@hash,@valid);",
                (NameParameter, item.SourceName),
                (OffsetParameter, item.Offset),
                (HashParameter, item.Hash),
                ("@valid", item.Offset < 0)
            );
        }

        bool sameCatalog =
            point.CatalogGeneration is not null
            && point.CatalogGeneration == ReadMetadata(connection, CatalogGenerationKey);
        VerifySurveyCheckpoints(connection, ReadSurveySources(connection, sameCatalog), canReanchor: false, token);
    }

    private static SurveySource[] ReadSurveySources(SqliteConnection connection, bool allowCachedBoundary)
    {
        using SqliteCommand command = Command(
            connection,
            "SELECT s.name,s.path,s.next_offset FROM sources s JOIN survey_value_checkpoints e ON e.source_name=s.name COLLATE NOCASE;"
        );
        using SqliteDataReader reader = command.ExecuteReader();
        var sources = new List<SurveySource>();
        while (reader.Read())
        {
            sources.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), allowCachedBoundary));
        }

        return sources.ToArray();
    }

    private static void VerifySurveyCheckpoints(
        SqliteConnection connection,
        IReadOnlyList<SurveySource> sources,
        bool canReanchor,
        CancellationToken token
    )
    {
        // Keep untouched markers in the same coordinate system as their cached credits.
        // A reconstructed catalog cannot trust a marker whose indexed source is absent.
        Execute(
            connection,
            "UPDATE survey_value_checkpoints SET valid=0 WHERE offset>=0 AND NOT EXISTS(SELECT 1 FROM sources s WHERE s.name=source_name COLLATE NOCASE);"
        );
        foreach (SurveySource source in sources)
        {
            using SqliteCommand command = Command(
                connection,
                "SELECT offset,hash FROM survey_value_checkpoints WHERE source_name=@name COLLATE NOCASE;",
                (NameParameter, source.Name)
            );
            long offset;
            string hash;
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                if (!reader.Read())
                {
                    continue;
                }

                offset = reader.GetInt64(0);
                hash = reader.GetString(1);
            }
            long? current = FindSurveyBoundary(source, offset, hash, canReanchor, token);
            Execute(
                connection,
                "UPDATE survey_value_checkpoints SET offset=COALESCE(@offset,offset),valid=@valid WHERE source_name=@name COLLATE NOCASE;",
                (OffsetParameter, current),
                ("@valid", current is not null),
                (NameParameter, source.Name)
            );
        }
    }

    private static long? FindSurveyBoundary(
        SurveySource source,
        long offset,
        string hash,
        bool canReanchor,
        CancellationToken token
    )
    {
        if (offset < 0)
        {
            return offset;
        }

        try
        {
            JournalLine? line = ReadLines(source.Path, offset, token, source.Length).FirstOrDefault();
            if (line?.Content is { } text && ContentHash(text) == hash)
            {
                return offset;
            }

            if (!canReanchor)
            {
                return null;
            }
            long? found = null;
            foreach (JournalLine candidate in ReadLines(source.Path, 0, token, source.Length))
            {
                if (candidate.Content is not { } content || ContentHash(content) != hash)
                {
                    continue;
                }

                if (found is not null)
                {
                    return null;
                }

                found = candidate.Offset;
            }
            return found;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The generation changes whenever replay replaces cached credit coordinates.
            // An explicit reset may still use a coherent retained catalog after originals are archived.
            return source.AllowCachedBoundary && offset < source.Length ? offset : null;
        }
    }

    private static bool IsSurveyValueAvailable(SqliteConnection connection)
    {
        using SqliteCommand command = Command(
            connection,
            """
            SELECT NOT EXISTS(SELECT 1 FROM survey_value_checkpoints WHERE valid=0)
            AND NOT EXISTS(SELECT 1 FROM value_credits c JOIN batches b ON b.id=c.batch_id JOIN sources s ON s.id=c.source_id
                WHERE b.status=0 AND c.timestamp IS NULL AND EXISTS(SELECT 1 FROM survey_value_period)
                AND NOT EXISTS(SELECT 1 FROM survey_value_checkpoints e WHERE e.source_name=s.name COLLATE NOCASE));
            """
        );
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != 0;
    }
}
