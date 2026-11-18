using System.Globalization;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Exploration.History;

public sealed partial class ExplorationHistoryCatalog
{
    private const string ReceiptJumpsKey = "receipt_trip_jumps";
    private const string ReceiptDistanceKey = "receipt_trip_distance";

    private static void ApplyReceiptTravel(SqliteConnection connection, JournalEventEnvelope entry)
    {
        if (entry.EventName != "FSDJump")
        {
            return;
        }
        int.TryParse(
            ReadMetadata(connection, ReceiptJumpsKey),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int jumps
        );
        double.TryParse(
            ReadMetadata(connection, ReceiptDistanceKey),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double distance
        );
        double? recorded = Number(entry.Payload, "JumpDist");
        double added = recorded ?? 0;
        if (recorded is null || !double.IsFinite(added) || added < 0 || !double.IsFinite(distance + added))
        {
            added = 0;
            SetMetadata(connection, "receipt_trip_distance_unknown", "1");
        }
        SetMetadata(connection, ReceiptJumpsKey, checked(jumps + 1).ToString(CultureInfo.InvariantCulture));
        SetMetadata(connection, ReceiptDistanceKey, (distance + added).ToString("R", CultureInfo.InvariantCulture));
    }

    private static void RecordPaymentReceipt(
        SqliteConnection connection,
        long settlement,
        JournalEventEnvelope entry,
        IReadOnlyList<string> systems
    )
    {
        long? actual = Integer(entry.Payload, "TotalEarnings");
        if (actual < 0)
        {
            actual = null;
        }
        int.TryParse(
            ReadMetadata(connection, ReceiptJumpsKey),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int jumps
        );
        double.TryParse(
            ReadMetadata(connection, ReceiptDistanceKey),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double distance
        );
        Execute(
            connection,
            """
            INSERT INTO sale_receipts(settlement_id,actual_earnings,system_names,jumps,distance)
            VALUES(@id,@actual,@systems,@jumps,@distance) ON CONFLICT(settlement_id) DO NOTHING;
            """,
            ("@id", settlement),
            ("@actual", actual),
            ("@systems", string.Join('\n', systems)),
            ("@jumps", jumps),
            ("@distance", ReadMetadata(connection, "receipt_trip_distance_unknown") is null ? distance : null)
        );
        SetMetadata(connection, ReceiptJumpsKey, "0");
        SetMetadata(connection, ReceiptDistanceKey, "0");
        Execute(connection, "DELETE FROM metadata WHERE key='receipt_trip_distance_unknown';");
    }
}
