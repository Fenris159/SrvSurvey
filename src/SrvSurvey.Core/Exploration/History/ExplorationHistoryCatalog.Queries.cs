using System.Globalization;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Exploration.History;

public sealed partial class ExplorationHistoryCatalog
{
    private ExplorationHistoryPage Query(ExplorationHistoryQuery query, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        token.ThrowIfCancellationRequested();
        if (!File.Exists(FilePath))
        {
            return new ExplorationHistoryPage([], 0, 0, Math.Clamp(query.PageSize, 1, 200), new());
        }
        using SqliteConnection connection = Open(initialize: false);
        int offset = Math.Max(0, query.Offset);
        int size = Math.Clamp(query.PageSize, 1, 200);
        (string Name, object? Value)[] parameters =
        [
            ("@status", query.Status is { } status ? (int)status : null),
            ("@search", query.SearchText.Trim()),
            ("@size", size),
            ("@offset", offset),
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
        using SqliteCommand command = Command(
            connection,
            """
            SELECT b.id,s.name,s.address,s.x,s.y,s.z,b.first_time,b.last_time,b.settled_time,b.status,b.estimate,b.scans,b.maps
            FROM batches b JOIN systems s ON s.key=b.system_key
            WHERE (@status IS NULL OR b.status=@status) AND instr(lower(s.name),lower(@search))>0
                AND (@from IS NULL OR b.last_time>=@from) AND (@through IS NULL OR b.last_time<=@through)
            ORDER BY b.last_time DESC,b.id DESC LIMIT @size OFFSET @offset;
            """,
            parameters
        );
        using SqliteDataReader reader = command.ExecuteReader();
        var rows = new List<ExplorationHistoryRow>();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            rows.Add(ReadRow(reader));
        }
        return new ExplorationHistoryPage(rows, total, offset, size, ReadSummary(connection));
    }

    private static ExplorationHistoryRow ReadRow(SqliteDataReader reader) =>
        new ExplorationHistoryRow
        {
            Id = reader.GetInt64(0),
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
        };

    private ExplorationHistoryDetails ReadDetails(long rowId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(FilePath))
        {
            return new ExplorationHistoryDetails(rowId, [], "History is not available.");
        }
        using SqliteConnection connection = Open(initialize: false);
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
                references.Add(new OriginalReference(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2)));
            }
        }
        var events = new List<JournalEventEnvelope>();
        bool missing = false;
        foreach (OriginalReference reference in references)
        {
            token.ThrowIfCancellationRequested();
            JournalEventEnvelope? original = ReadOriginal(connection, reference);
            if (original is not null)
            {
                events.Add(original);
            }
            else
            {
                missing = true;
            }
        }
        return new ExplorationHistoryDetails(
            rowId,
            events.OrderBy(entry => entry.Timestamp).ToArray(),
            missing
                ? "Some original journal records are not available. Cataloged values and statuses have been retained."
                : null
        );
    }
}
