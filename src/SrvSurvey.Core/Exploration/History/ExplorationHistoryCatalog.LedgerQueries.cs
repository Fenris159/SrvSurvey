using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SrvSurvey.Core.Exploration.History;

public sealed partial class ExplorationHistoryCatalog
{
    private const string PaymentProjection = """
        SELECT -t.id,'Exploration data sold',NULL,NULL,NULL,NULL,t.timestamp,t.timestamp,t.timestamp,3,0,
            COALESCE((SELECT SUM(scans) FROM batches WHERE settlement_id=t.id),0),
            COALESCE((SELECT SUM(maps) FROM batches WHERE settlement_id=t.id),0),
            original.name,t.offset,t.hash,0,r.actual_earnings,r.jumps,r.distance,
            CASE WHEN r.settlement_id IS NULL THEN NULL ELSE
                (SELECT COUNT(DISTINCT o.body_key) FROM observations o JOIN batches b ON b.id=o.batch_id
                    WHERE b.settlement_id=t.id AND o.kind='PlanetaryLanding') END,
            COALESCE(r.system_names,(SELECT group_concat(s.name,char(10)) FROM batches b
                JOIN systems s ON s.key=b.system_key WHERE b.settlement_id=t.id)),
            EXISTS(SELECT 1 FROM batches WHERE settlement_id=t.id)
        FROM settlements t JOIN sources original ON original.id=t.source_id
        LEFT JOIN sale_receipts r ON r.settlement_id=t.id WHERE t.kind='sale'
        """;

    private const string LedgerProjection =
        "WITH ledger(id,system_name,address,x,y,z,first_time,last_time,settled_time,status,estimate,scans,maps,source_name,source_offset,source_hash,notables,payout,jumps,distance,landed,system_names,has_body_details) AS (\n"
        + BatchColumns
        + ",NULL,NULL,NULL,NULL,NULL,0\n"
        + BatchTables
        + "\nUNION ALL\n"
        + PaymentProjection
        + "\n)\n";

    private const string LedgerFilter = """
        WHERE (@status IS NULL OR status=@status)
            AND instr(lower(system_name||char(10)||COALESCE(system_names,'')),lower(@search))>0
            AND (@from IS NULL OR last_time>=@from) AND (@through IS NULL OR last_time<=@through)
        """;

    private ExplorationHistoryPage QueryLedger(ExplorationHistoryQuery query, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int size = Math.Clamp(query.PageSize, 1, 200);
        if (!File.Exists(FilePath))
        {
            return new([], 0, 0, size, new());
        }
        using SqliteConnection connection = Open(initialize: false);
        using SqliteTransaction snapshot = connection.BeginTransaction(deferred: true);
        (string Name, object? Value)[] parameters =
        [
            ("@status", query.Status is { } status ? (int)status : null),
            ("@search", query.SearchText.Trim()),
            ("@from", Timestamp(query.FromUtc)),
            ("@through", Timestamp(query.ThroughUtc)),
        ];
        using SqliteCommand count = Command(
            connection,
            LedgerProjection + "SELECT COUNT(*) FROM ledger\n" + LedgerFilter,
            parameters
        );
        int total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        int offset = Math.Max(0, query.Offset);
        if (offset >= total)
        {
            offset = total == 0 ? 0 : (total - 1) / size * size;
        }
        using SqliteCommand command = Command(
            connection,
            LedgerProjection
                + "SELECT * FROM ledger\n"
                + LedgerFilter
                + "\nORDER BY last_time DESC,(status=3) DESC,abs(id) DESC LIMIT @size OFFSET @offset;",
            [.. parameters, ("@size", size), ("@offset", offset)]
        );
        var rows = new List<ExplorationHistoryRow>();
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                rows.Add(ReadLedgerRow(reader));
            }
        }
        long? selectedId = FindSelectedRow(connection, query.SelectionAnchor);
        ExplorationHistoryRow? selected = rows.SingleOrDefault(row => row.Id == selectedId);
        if (selectedId is { } id && selected is null)
        {
            using SqliteCommand selection = Command(
                connection,
                LedgerProjection + "SELECT * FROM ledger WHERE id=@id;",
                ("@id", id)
            );
            using SqliteDataReader reader = selection.ExecuteReader();
            selected = reader.Read() ? ReadLedgerRow(reader) : null;
        }
        var page = new ExplorationHistoryPage(rows, total, offset, size, ReadSummary(connection), selectedId, selected);
        snapshot.Commit();
        return page;
    }

    private static ExplorationHistoryRow ReadLedgerRow(SqliteDataReader reader)
    {
        ExplorationHistoryRow row = ReadRow(reader);
        if (row.Status != ExplorationDataStatus.Paid)
        {
            return row;
        }
        return row with
        {
            Payment = new(
                reader.IsDBNull(17) ? null : reader.GetInt64(17),
                reader.IsDBNull(18) ? null : reader.GetInt32(18),
                reader.IsDBNull(19) ? null : reader.GetDouble(19),
                reader.IsDBNull(20) ? null : reader.GetInt32(20),
                reader.IsDBNull(21) ? [] : reader.GetString(21).Split('\n', StringSplitOptions.RemoveEmptyEntries),
                reader.GetInt32(22) != 0
            ),
        };
    }

    private static long? FindPaymentRow(SqliteConnection connection, ExplorationHistoryAnchor? anchor)
    {
        if (anchor is null)
        {
            return null;
        }
        using SqliteCommand command = Command(
            connection,
            """
            SELECT -t.id FROM settlements t JOIN sources s ON s.id=t.source_id
            WHERE t.kind='sale' AND s.name=@name COLLATE NOCASE AND t.offset=@offset AND t.hash=@hash LIMIT 2;
            """,
            (NameParameter, anchor.SourceName),
            (OffsetParameter, anchor.Offset),
            (HashParameter, anchor.Hash)
        );
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }
        long id = reader.GetInt64(0);
        return reader.Read() ? null : id;
    }
}
