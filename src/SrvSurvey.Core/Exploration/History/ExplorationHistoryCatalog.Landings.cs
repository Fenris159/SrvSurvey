using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Exploration.History;

public sealed partial class ExplorationHistoryCatalog
{
    private const string BodyParameter = "@body";

    private static void RetainPendingLanding(
        SqliteConnection connection,
        string system,
        string body,
        JournalEventEnvelope entry,
        OriginalReference reference
    )
    {
        Execute(
            connection,
            "INSERT OR IGNORE INTO bodies(key,system_key,name,body_id) VALUES(@body,@system,@name,@id);",
            (BodyParameter, body),
            (SystemParameter, system),
            (NameParameter, Text(entry.Payload, "BodyName") ?? Text(entry.Payload, "Body")),
            ("@id", Integer(entry.Payload, "BodyID"))
        );
        Execute(
            connection,
            """
            INSERT INTO pending_landings(body_key,source_id,offset,hash) VALUES(@body,@source,@offset,@hash)
            ON CONFLICT(body_key) DO UPDATE SET source_id=excluded.source_id,offset=excluded.offset,hash=excluded.hash;
            """,
            (BodyParameter, body),
            (SourceParameter, reference.SourceId),
            (OffsetParameter, reference.Offset),
            (HashParameter, reference.Hash)
        );
    }

    private static void AttachPendingLanding(SqliteConnection connection, long batch, string body)
    {
        OriginalReference? original;
        using (
            SqliteCommand command = Command(
                connection,
                "SELECT source_id,offset,hash FROM pending_landings WHERE body_key=@body;",
                (BodyParameter, body)
            )
        )
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            original = reader.Read() ? new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2)) : null;
        }

        if (original is null)
        {
            return;
        }

        RecordObservation(connection, batch, body, "Touchdown", original);
        RecordObservation(connection, batch, body, "PlanetaryLanding", original);
        Execute(connection, "DELETE FROM pending_landings WHERE body_key=@body;", (BodyParameter, body));
    }
}
