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
        double added = Number(entry.Payload, "JumpDist") ?? 0;
        if (!double.IsFinite(added) || added < 0)
        {
            added = 0;
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
            ("@distance", distance)
        );
        SetMetadata(connection, ReceiptJumpsKey, "0");
        SetMetadata(connection, ReceiptDistanceKey, "0");
    }

    /// <summary>Starts a survey-value period without changing collected history, payment receipts, or the odometer.</summary>
    public Task<ExplorationHistorySummary> ResetSurveyValueAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ResetSurveyValue(cancellationToken), cancellationToken);

    private ExplorationHistorySummary ResetSurveyValue(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        if (!ReadSummary(connection).IsComplete)
        {
            throw new InvalidOperationException(
                "Finish exploration history recovery before resetting the automatically tracked value."
            );
        }
        // Keep the consumed frontier, not just credited observations: an older duplicate scan
        // can become the contributor during replay, but must remain outside this survey period.
        Execute(
            connection,
            """
            DELETE FROM survey_value_checkpoints;
            INSERT INTO survey_value_checkpoints(source_name,first_time,offset)
            SELECT name,first_time,last_offset FROM sources;
            """
        );
        ExplorationHistorySummary summary = ReadSummary(connection);
        transaction.Commit();
        return summary;
    }
}
