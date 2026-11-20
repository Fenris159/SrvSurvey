using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SrvSurvey.Core.Exploration.History;

public sealed partial class ExplorationHistoryCatalog
{
    // Unknown sources need both wall time and the saved source frontier because
    // journal timestamps have only second precision. NULL means ordering is unproven.
    private const string SurveyCreditScopeSql = """
        WITH survey_credit_scope AS (
            SELECT c.amount,b.status,
                CASE
                    WHEN p.id IS NULL THEN 1
                    WHEN e.source_name IS NOT NULL THEN
                        CASE WHEN e.valid<>1 THEN NULL WHEN c.offset>e.offset THEN 1 ELSE 0 END
                    WHEN c.timestamp>p.reset_utc THEN 1
                    WHEN c.timestamp IS NULL THEN NULL
                    WHEN substr(c.timestamp,1,19)<>substr(p.reset_utc,1,19) THEN 0
                    WHEN substr(COALESCE(s.first_time,''),1,19)=substr(p.reset_utc,1,19)
                        AND NOT EXISTS(
                            SELECT 1 FROM survey_value_checkpoints f LEFT JOIN sources known ON known.name=f.source_name COLLATE NOCASE
                            WHERE f.offset>=0 AND (known.id IS NULL OR known.first_time IS NULL OR known.first_time>s.first_time
                                OR (known.first_time=s.first_time AND known.name>=s.name))
                        ) THEN 1
                    ELSE NULL
                END AS in_period
            FROM value_credits c JOIN batches b ON b.id=c.batch_id JOIN sources s ON s.id=c.source_id
            LEFT JOIN survey_value_period p ON p.id=1
            LEFT JOIN survey_value_checkpoints e ON e.source_name=s.name COLLATE NOCASE
        )

        """;

    private sealed record SurveySource(string Name, string Path, long Length, bool AllowCachedBoundary = false);

    private sealed record SurveyIndexedSource(
        string Name,
        long Offset,
        string Hash,
        string? FirstTime,
        string Path,
        long Length,
        bool IsContinuation
    );

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
        // A reset based on consumed journal positions needs a complete index, but
        // an unresolved body valuation in that index cannot move those positions.
        bool usesConsumerFrontier = includeEarlierSources && consumed is not null;
        bool canCapture = usesConsumerFrontier
            ? HasCompleteSourceIndex(connection)
            : ReadSummary(connection).IsComplete;
        if (!canCapture)
        {
            throw includeEarlierSources
                ? new ExplorationSurveyResetRecoveryException()
                : new InvalidOperationException(
                    "Finish exploration history recovery before resetting the automatically tracked value."
                );
        }

        SurveyIndexedSource[] sources = ReadIndexedSurveySources(connection);
        SurveyIndexedSource? acknowledgedSource = FindLatestAcknowledgedSurveySource(
            sources,
            consumed,
            includeEarlierSources
        );
        string? catalogGeneration = ReadMetadata(connection, CatalogGenerationKey);
        ExplorationSurveySourceCheckpoint[] points = sources
            .Select(source =>
            {
                ExplorationSurveySourceCheckpoint? explicitCursor = consumed?.SingleOrDefault(item =>
                    string.Equals(item.SourceName, source.Name, StringComparison.OrdinalIgnoreCase)
                );
                ExplorationSurveySourceCheckpoint marker =
                    explicitCursor
                    ?? SurveyCheckpointForUnobservedSource(
                        source,
                        acknowledgeAll: consumed is null,
                        acknowledgedSource
                    );
                bool verified =
                    (catalogGeneration is not null && marker.CatalogGeneration == catalogGeneration)
                    || IsCurrentSurveyBoundary(source, marker, token);
                // Fresh monitor positions must be verified before the reset is saved.
                // Previously captured positions can be reanchored when their journals return.
                if (includeEarlierSources && explicitCursor is { CatalogGeneration: null } && !verified)
                {
                    throw new ExplorationSurveyResetRecoveryException(marker.SourceName);
                }

                return marker with
                {
                    CatalogGeneration = verified ? catalogGeneration : null,
                };
            })
            .ToArray();
        if (usesConsumerFrontier)
        {
            VerifyContinuationCredits(connection, sources, points);
        }
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

    private static void VerifyContinuationCredits(
        SqliteConnection connection,
        IReadOnlyList<SurveyIndexedSource> sources,
        IReadOnlyList<ExplorationSurveySourceCheckpoint> points
    )
    {
        // History can attribute a continuation via its saved predecessor while
        // the journal monitor lacks a verified position in the continuation.
        foreach (
            (_, ExplorationSurveySourceCheckpoint point) in sources
                .Zip(points)
                .Where(pair => pair.First.IsContinuation && pair.Second.Offset < pair.First.Offset)
        )
        {
            using SqliteCommand command = Command(
                connection,
                """
                SELECT 1 FROM value_credits c JOIN batches b ON b.id=c.batch_id
                    JOIN sources s ON s.id=c.source_id
                WHERE s.name=@name COLLATE NOCASE AND c.offset>@offset AND b.status=0 AND c.amount>0 LIMIT 1;
                """,
                (NameParameter, point.SourceName),
                (OffsetParameter, point.Offset)
            );
            if (command.ExecuteScalar() is not null)
            {
                throw new ExplorationSurveyResetRecoveryException(point.SourceName, requiresJournalReopen: true);
            }
        }
    }

    private static SurveyIndexedSource? FindLatestAcknowledgedSurveySource(
        IReadOnlyList<SurveyIndexedSource> sources,
        IReadOnlyList<ExplorationSurveySourceCheckpoint>? consumed,
        bool includeEarlierSources
    )
    {
        if (!includeEarlierSources || consumed is null)
        {
            return null;
        }

        var indexedNames = sources.Select(source => source.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ExplorationSurveySourceCheckpoint? missing = consumed.FirstOrDefault(marker =>
            marker.Offset >= 0 && marker.CatalogGeneration is null && !indexedNames.Contains(marker.SourceName)
        );
        if (missing is not null)
        {
            throw new ExplorationSurveyResetRecoveryException(missing.SourceName);
        }

        return sources
                .Where(source =>
                    consumed.Any(item =>
                        item.Offset >= 0
                        && string.Equals(item.SourceName, source.Name, StringComparison.OrdinalIgnoreCase)
                    )
                )
                .OrderBy(source => source.FirstTime, StringComparer.Ordinal)
                .ThenBy(source => source.Name, StringComparer.Ordinal)
                .LastOrDefault()
            ?? throw new ExplorationSurveyResetRecoveryException();
    }

    private static SurveyIndexedSource[] ReadIndexedSurveySources(SqliteConnection connection)
    {
        using SqliteCommand command = Command(
            connection,
            """
            SELECT name,last_offset,last_hash,first_time,path,next_offset,
                COALESCE(json_extract(context,'$.JournalPartNumber')>1,0)
            FROM sources ORDER BY name;
            """
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
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt64(5),
                    reader.GetBoolean(6)
                )
            );
        }
        return sources.ToArray();
    }

    private static ExplorationSurveySourceCheckpoint SurveyCheckpointForUnobservedSource(
        SurveyIndexedSource source,
        bool acknowledgeAll,
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
        if (acknowledgeAll || earlier)
        {
            return new(source.Name, source.Offset, source.Hash);
        }
        return new(source.Name, -1, string.Empty);
    }

    private static bool IsCurrentSurveyBoundary(
        SurveyIndexedSource source,
        ExplorationSurveySourceCheckpoint marker,
        CancellationToken token
    ) =>
        FindSurveyBoundary(
            new(source.Name, source.Path, source.Length, marker.Offset == source.Offset && marker.Hash == source.Hash),
            marker.Offset,
            marker.Hash,
            canReanchor: false,
            token
        )
            is not null;

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

        string? generation = ReadMetadata(connection, CatalogGenerationKey);
        var trusted = point
            .Sources.Where(marker =>
                generation is not null
                && point.CatalogGeneration == generation
                && marker.CatalogGeneration == generation
            )
            .Select(marker => marker.SourceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        VerifySurveyCheckpoints(connection, ReadSurveySources(connection, trusted), canReanchor: false, token);
    }

    private static SurveySource[] ReadSurveySources(SqliteConnection connection, HashSet<string> trustedSources)
    {
        using SqliteCommand command = Command(
            connection,
            "SELECT s.name,s.path,s.next_offset FROM sources s JOIN survey_value_checkpoints e ON e.source_name=s.name COLLATE NOCASE;"
        );
        using SqliteDataReader reader = command.ExecuteReader();
        var sources = new List<SurveySource>();
        while (reader.Read())
        {
            string name = reader.GetString(0);
            sources.Add(new(name, reader.GetString(1), reader.GetInt64(2), trustedSources.Contains(name)));
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
            SurveyCreditScopeSql
                + """
                SELECT NOT EXISTS(SELECT 1 FROM survey_value_checkpoints WHERE valid=0)
                AND NOT EXISTS(SELECT 1 FROM survey_credit_scope WHERE status=0 AND in_period IS NULL);
                """
        );
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != 0;
    }
}
