using System.Globalization;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Exploration.History;

public sealed partial class ExplorationHistoryCatalog
{
    private const string UnavailableHistoryMessage = "History is not available.";
    private const string RowProjection = """
        SELECT b.id,s.name,s.address,s.x,s.y,s.z,b.first_time,b.last_time,b.settled_time,b.status,b.estimate,b.scans,b.maps,
            original.name,anchor.offset,anchor.hash,COALESCE(notable.flags,0)
        FROM batches b JOIN systems s ON s.key=b.system_key
        LEFT JOIN collection_anchors anchor ON anchor.id=(
            SELECT id FROM collection_anchors WHERE batch_id=b.id ORDER BY id LIMIT 1)
        LEFT JOIN sources original ON original.id=anchor.source_id
        LEFT JOIN notable_systems notable ON notable.system_key=s.key
        """;

    private ExplorationHistoryPage Query(ExplorationHistoryQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        token.ThrowIfCancellationRequested();
        if (!File.Exists(FilePath))
        {
            return new ExplorationHistoryPage([], 0, 0, Math.Clamp(query.PageSize, 1, 200), new());
        }
        using SqliteConnection connection = Open(initialize: false);
        using SqliteTransaction snapshot = connection.BeginTransaction(deferred: true);
        int offset = Math.Max(0, query.Offset);
        int size = Math.Clamp(query.PageSize, 1, 200);
        (string Name, object? Value)[] parameters =
        [
            ("@status", query.Status is { } status ? (int)status : null),
            ("@search", query.SearchText.Trim()),
            ("@from", Timestamp(query.FromUtc)),
            ("@through", Timestamp(query.ThroughUtc)),
        ];
        using SqliteCommand count = Command(
            connection,
            """
            SELECT COUNT(*) FROM batches b JOIN systems s ON s.key=b.system_key
            WHERE (@status IS NULL OR b.status=@status) AND instr(lower(s.name),lower(@search))>0
                AND (@from IS NULL OR b.last_time>=@from) AND (@through IS NULL OR b.last_time<=@through);
            """,
            parameters
        );
        int total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (offset >= total)
        {
            offset = total == 0 ? 0 : (total - 1) / size * size;
        }
        using SqliteCommand command = Command(
            connection,
            RowProjection
                + "\n"
                + """
                WHERE (@status IS NULL OR b.status=@status) AND instr(lower(s.name),lower(@search))>0
                    AND (@from IS NULL OR b.last_time>=@from) AND (@through IS NULL OR b.last_time<=@through)
                ORDER BY b.last_time DESC,b.id DESC LIMIT @size OFFSET @offset;
                """,
            [.. parameters, ("@size", size), ("@offset", offset)]
        );
        using SqliteDataReader reader = command.ExecuteReader();
        var rows = new List<ExplorationHistoryRow>();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            rows.Add(ReadRow(reader));
        }
        long? selectedId = FindSelectedRow(connection, query.SelectionAnchor);
        var page = new ExplorationHistoryPage(
            rows,
            total,
            offset,
            size,
            ReadSummary(connection),
            selectedId,
            ReadSelectedRow(connection, selectedId, rows)
        );
        snapshot.Commit();
        return page;
    }

    private static ExplorationHistoryRow? ReadSelectedRow(
        SqliteConnection connection,
        long? id,
        IReadOnlyList<ExplorationHistoryRow> rows
    )
    {
        if (id is null)
        {
            return null;
        }
        ExplorationHistoryRow? visible = rows.SingleOrDefault(row => row.Id == id);
        if (visible is not null)
        {
            return visible;
        }
        using SqliteCommand command = Command(connection, RowProjection + " WHERE b.id=@id;", ("@id", id));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadRow(reader) : null;
    }

    private static long? FindSelectedRow(SqliteConnection connection, ExplorationHistoryAnchor? anchor)
    {
        if (anchor is null)
        {
            return null;
        }
        using SqliteCommand command = Command(
            connection,
            """
            SELECT DISTINCT o.batch_id FROM collection_anchors o JOIN sources s ON s.id=o.source_id
            WHERE s.name=@name COLLATE NOCASE AND o.offset=@offset AND o.hash=@hash
                LIMIT 2;
            """,
            ("@name", anchor.SourceName),
            ("@offset", anchor.Offset),
            ("@hash", anchor.Hash)
        );
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }
        long id = reader.GetInt64(0);
        return reader.Read() ? null : id;
    }

    private static ExplorationHistoryRow ReadRow(SqliteDataReader reader) =>
        new ExplorationHistoryRow
        {
            Id = reader.GetInt64(0),
            Anchor = reader.IsDBNull(13) ? null : new(reader.GetString(13), reader.GetInt64(14), reader.GetString(15)),
            SystemName = reader.GetString(1),
            SystemAddress = reader.IsDBNull(2) ? null : reader.GetInt64(2),
            X = reader.IsDBNull(3) ? null : reader.GetDouble(3),
            Y = reader.IsDBNull(4) ? null : reader.GetDouble(4),
            Z = reader.IsDBNull(5) ? null : reader.GetDouble(5),
            FirstCollected = ParseTimestamp(reader.IsDBNull(6) ? null : reader.GetString(6)),
            LastCollected = ParseTimestamp(reader.IsDBNull(7) ? null : reader.GetString(7)),
            SettledAt = ParseTimestamp(reader.IsDBNull(8) ? null : reader.GetString(8)),
            Status = (ExplorationDataStatus)reader.GetInt32(9),
            EstimatedValue = reader.GetInt64(10),
            ScannedBodies = reader.GetInt32(11),
            MappedBodies = reader.GetInt32(12),
            Notables = (ExplorationNotables)reader.GetInt32(16),
        };

    private ExplorationHistoryDetails ReadDetails(long rowId, ExplorationHistoryAnchor? anchor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(FilePath))
        {
            return new ExplorationHistoryDetails(rowId, [], UnavailableHistoryMessage);
        }
        using SqliteConnection connection = Open(initialize: false);
        using SqliteTransaction snapshot = connection.BeginTransaction(deferred: true);
        if (anchor is not null)
        {
            long? currentRow = FindSelectedRow(connection, anchor);
            if (currentRow is null)
            {
                return new ExplorationHistoryDetails(rowId, [], UnavailableHistoryMessage);
            }
            rowId = currentRow.Value;
        }
        var references = new List<OriginalReference>();
        using (
            SqliteCommand command = Command(
                connection,
                """
                SELECT source_id,offset,hash FROM observations WHERE batch_id=@batch
                UNION SELECT t.source_id,t.offset,t.hash FROM settlements t JOIN batches b ON b.settlement_id=t.id WHERE b.id=@batch;
                """,
                ("@batch", rowId)
            )
        )
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                references.Add(new OriginalReference(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2)));
            }
        }
        var events = new List<JournalEventEnvelope>();
        bool missing = false;
        foreach (OriginalReference reference in references)
        {
            token.ThrowIfCancellationRequested();
            JournalEventEnvelope? original = ReadOriginal(connection, reference, token);
            if (original is not null)
            {
                events.Add(original);
            }
            else
            {
                missing = true;
            }
        }
        snapshot.Commit();
        return new ExplorationHistoryDetails(
            rowId,
            events.OrderBy(entry => entry.Timestamp).ToArray(),
            missing
                ? "Some original journal records are not available. Cataloged values and statuses have been retained."
                : null
        );
    }
}
