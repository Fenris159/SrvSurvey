using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Exploration.History;

public sealed partial class ExplorationHistoryCatalog
{
    private sealed record BodyOwnership(long PaidValue, long PendingValue, long? BatchId, OriginalReference? Scan);

    private void ApplyEntry(
        SqliteConnection connection,
        SourceContext context,
        JournalEventEnvelope entry,
        OriginalReference reference
    )
    {
        ApplyContext(context, entry);
        if (!BelongsToCommander(context))
        {
            return;
        }
        SetMetadata(connection, "commander_seen", "1");
        ApplyNotableDiscovery(connection, context, entry);
        switch (entry.EventName)
        {
            case "Scan":
                ApplyScan(connection, context, entry, reference);
                break;
            case "SAAScanComplete":
                ApplyMapping(connection, context, entry, reference);
                break;
            case "SellExplorationData":
            case "MultiSellExplorationData":
                ApplySale(connection, entry, reference);
                break;
            case "Died":
                SetMetadata(connection, "pending_death", Timestamp(entry.Timestamp) ?? string.Empty);
                break;
            case "Resurrect":
                ApplyRespawn(connection, entry, reference);
                break;
            case "FSSBodySignals":
            case "SAASignalsFound":
            case "Touchdown":
                ApplyAdditionalDetails(connection, context, entry, reference);
                break;
        }
    }

    private static void ApplyContext(SourceContext context, JournalEventEnvelope entry)
    {
        JsonElement root = entry.Payload;
        switch (entry.EventName)
        {
            case "Fileheader":
                if (context.JournalPartNumber is not > 1)
                {
                    context.FrontierId = null;
                    context.CommanderRecord = null;
                    context.SystemName = null;
                    context.SystemAddress = null;
                    context.X = null;
                    context.Y = null;
                    context.Z = null;
                    context.StationName = null;
                }
                context.IsLive = IsLiveHeader(root);
                break;
            case "Commander":
            case "LoadGame":
                context.FrontierId = Text(root, "FID") ?? context.FrontierId;
                break;
            case "Location":
            case "FSDJump":
            case "CarrierJump":
                ApplyLocation(context, root);
                break;
            case "Docked":
                context.StationName = Text(root, "StationName");
                break;
        }
    }

    private static void ApplyLocation(SourceContext context, JsonElement root)
    {
        string? name = Text(root, "StarSystem") ?? context.SystemName;
        context.SystemAddress =
            Integer(root, "SystemAddress")
            ?? (
                string.Equals(name, context.SystemName, StringComparison.OrdinalIgnoreCase)
                    ? context.SystemAddress
                    : null
            );
        context.SystemName = name;
        context.StationName = Text(root, "StationName");
        if (
            root.TryGetProperty("StarPos", out JsonElement position)
            && position.ValueKind == JsonValueKind.Array
            && position.GetArrayLength() == 3
        )
        {
            context.X =
                position[0].ValueKind == JsonValueKind.Number && position[0].TryGetDouble(out double x) ? x : null;
            context.Y =
                position[1].ValueKind == JsonValueKind.Number && position[1].TryGetDouble(out double y) ? y : null;
            context.Z =
                position[2].ValueKind == JsonValueKind.Number && position[2].TryGetDouble(out double z) ? z : null;
        }
        else
        {
            context.X = null;
            context.Y = null;
            context.Z = null;
        }
    }

    private static bool? IsLiveHeader(JsonElement root)
    {
        if (
            root.TryGetProperty("Odyssey", out JsonElement galaxy)
            && galaxy.ValueKind is JsonValueKind.True or JsonValueKind.False
        )
        {
            return galaxy.GetBoolean();
        }
        string? version = Text(root, "gameversion");
        return int.TryParse(version?.Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            ? major >= 4
            : null;
    }

    private static string? EnsureSystem(SqliteConnection connection, SourceContext context, JsonElement root)
    {
        long? address = Integer(root, "SystemAddress") ?? context.SystemAddress;
        string? name =
            Text(root, "StarSystem")
            ?? Text(root, "System")
            ?? (address == context.SystemAddress ? context.SystemName : null);
        if (address is null && string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        string key =
            KnownSystemKey(connection, address, name)
            ?? (address is { } value ? "a:" + value.ToString(CultureInfo.InvariantCulture) : "n:" + name);
        bool samePosition = address == context.SystemAddress;
        Execute(
            connection,
            """
            INSERT INTO systems(key,name,address,x,y,z) VALUES(@key,@name,@address,@x,@y,@z)
            ON CONFLICT(key) DO UPDATE SET name=CASE WHEN @known=1 THEN excluded.name ELSE systems.name END,
                address=COALESCE(excluded.address,systems.address),
                x=COALESCE(excluded.x,systems.x),y=COALESCE(excluded.y,systems.y),z=COALESCE(excluded.z,systems.z);
            """,
            ("@key", key),
            (NameParameter, name ?? "System " + address),
            ("@address", address),
            ("@x", samePosition ? context.X : null),
            ("@y", samePosition ? context.Y : null),
            ("@z", samePosition ? context.Z : null),
            ("@known", !string.IsNullOrWhiteSpace(name))
        );
        return key;
    }

    private static string? BodyKey(SqliteConnection connection, string systemKey, JsonElement root)
    {
        long? id = Integer(root, "BodyID");
        string? name = Text(root, "BodyName") ?? Text(root, "Body");
        string? key = name is not null ? systemKey + ":n:" + name : null;
        if (id is { } value)
        {
            key = systemKey + ":i:" + value.ToString(CultureInfo.InvariantCulture);
        }
        return KnownBodyKey(connection, systemKey, id, name) ?? key;
    }

    private static BodyOwnership ReadBody(SqliteConnection connection, string key)
    {
        using SqliteCommand command = Command(
            connection,
            "SELECT paid_value,pending_value,batch_id,scan_source_id,scan_offset,scan_hash FROM bodies WHERE key=@key;",
            ("@key", key)
        );
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new(0, 0, null, null);
        }
        OriginalReference? scan = reader.IsDBNull(3)
            ? null
            : new(reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5));
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), scan);
    }

    private static void ApplyScan(
        SqliteConnection connection,
        SourceContext context,
        JournalEventEnvelope entry,
        OriginalReference reference
    )
    {
        JsonElement root = entry.Payload;
        if (string.Equals(Text(root, "ScanType"), "NavBeaconDetail", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        string? system = EnsureSystem(connection, context, root);
        string? body = system is null ? null : BodyKey(connection, system, root);
        if (body is null)
        {
            return;
        }
        Execute(
            connection,
            """
            INSERT INTO bodies(key,system_key,name,body_id,scan_source_id,scan_offset,scan_hash) VALUES(@key,@system,@name,@bodyId,@source,@offset,@hash)
            ON CONFLICT(key) DO UPDATE SET name=COALESCE(excluded.name,bodies.name),body_id=COALESCE(excluded.body_id,bodies.body_id),scan_source_id=excluded.scan_source_id,scan_offset=excluded.scan_offset,scan_hash=excluded.scan_hash;
            """,
            ("@key", body),
            (SystemParameter, system),
            (NameParameter, Text(root, "BodyName") ?? Text(root, "Body")),
            ("@bodyId", Integer(root, "BodyID")),
            (SourceParameter, reference.SourceId),
            (OffsetParameter, reference.Offset),
            (HashParameter, reference.Hash)
        );
        int candidate = CalculateValue(root, context.IsLive == true, false, false);
        ApplyValue(connection, system!, body, entry, reference, candidate, null);
    }

    private static void ApplyMapping(
        SqliteConnection connection,
        SourceContext context,
        JournalEventEnvelope entry,
        OriginalReference reference
    )
    {
        string? system = EnsureSystem(connection, context, entry.Payload);
        string? body = system is null ? null : BodyKey(connection, system, entry.Payload);
        if (body is null)
        {
            return;
        }
        BodyOwnership owned = ReadBody(connection, body);
        JournalEventEnvelope? scan = owned.Scan is { } source ? ReadOriginal(connection, source) : null;
        if (scan is null)
        {
            if (owned.Scan is null)
            {
                SetMetadata(
                    connection,
                    "notice",
                    "Some mapping values need original scan records that are not available."
                );
                Execute(
                    connection,
                    "INSERT OR IGNORE INTO bodies(key,system_key,name,body_id) VALUES(@key,@system,@name,@id);",
                    ("@key", body),
                    (SystemParameter, system),
                    (NameParameter, Text(entry.Payload, "BodyName") ?? Text(entry.Payload, "Body")),
                    ("@id", Integer(entry.Payload, "BodyID"))
                );
                SetMetadata(connection, UnknownMappingPrefix + body, system!);
                return;
            }
            throw new IOException(
                "An original Scan is unavailable. Restore the journal and refresh; later exploration records have been deferred."
            );
        }
        long probes = Integer(entry.Payload, "ProbesUsed") ?? long.MaxValue;
        long target = Integer(entry.Payload, "EfficiencyTarget") ?? -1;
        int candidate = CalculateValue(scan.Payload, context.IsLive == true, true, probes <= target);
        ApplyValue(connection, system!, body, entry, reference, candidate, owned.Scan);
        if (candidate > 0)
        {
            Execute(connection, "DELETE FROM metadata WHERE key=@key;", ("@key", UnknownMappingPrefix + body));
        }
    }

    private static int CalculateValue(JsonElement scan, bool live, bool mapped, bool efficient) =>
        ExplorationValueCalculator.Calculate(
            new ExplorationValueRequest
            {
                BodyClass = Text(scan, "PlanetClass") ?? Text(scan, "StarType"),
                IsTerraformable = Text(scan, "TerraformState") == "Terraformable",
                Mass = Number(scan, "MassEM") is > 0 and var mass ? mass : Number(scan, "StellarMass") ?? 0,
                IsFirstDiscoverer = !Flag(scan, "WasDiscovered", true),
                IsFirstMapped = !Flag(scan, "WasMapped", true),
                IsMapped = mapped,
                IsOdyssey = live,
                WithEfficiencyBonus = efficient,
            }
        );

    private static void ApplyValue(
        SqliteConnection connection,
        string system,
        string body,
        JournalEventEnvelope entry,
        OriginalReference reference,
        int candidate,
        OriginalReference? scanContext
    )
    {
        if (candidate <= 0)
        {
            return;
        }
        BodyOwnership owned = ReadBody(connection, body);
        long unpaid = Math.Max(0, candidate - owned.PaidValue);
        if (unpaid <= owned.PendingValue)
        {
            if (owned.BatchId is { } existing)
            {
                RecordObservation(connection, existing, body, entry.EventName, reference);
            }
            return;
        }
        using SqliteCommand find = Command(
            connection,
            "SELECT id FROM batches WHERE system_key=@system AND status=0;",
            (SystemParameter, system)
        );
        object? value = find.ExecuteScalar();
        long batch;
        if (value is long id)
        {
            batch = id;
        }
        else
        {
            using SqliteCommand insert = Command(
                connection,
                "INSERT INTO batches(system_key,first_time,last_time) VALUES(@system,@time,@time) RETURNING id;",
                (SystemParameter, system),
                (TimeParameter, Timestamp(entry.Timestamp))
            );
            batch = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        RecordObservation(connection, batch, body, entry.EventName, reference);
        if (scanContext is not null)
        {
            RecordObservation(connection, batch, body, "ScanContext", scanContext);
        }
        Execute(
            connection,
            "UPDATE bodies SET pending_value=@value,batch_id=@batch WHERE key=@body;",
            ("@value", unpaid),
            (BatchParameter, batch),
            ("@body", body)
        );
        Execute(
            connection,
            """
            UPDATE batches SET estimate=estimate+@delta,last_time=COALESCE(@time,last_time),
                scans=(SELECT COUNT(DISTINCT body_key) FROM observations WHERE batch_id=@batch AND kind IN ('Scan','SAAScanComplete')),
                maps=(SELECT COUNT(*) FROM observations WHERE batch_id=@batch AND kind='SAAScanComplete') WHERE id=@batch;
            """,
            ("@delta", unpaid - owned.PendingValue),
            (TimeParameter, Timestamp(entry.Timestamp)),
            (BatchParameter, batch)
        );
    }

    private static void RecordObservation(
        SqliteConnection connection,
        long batch,
        string body,
        string kind,
        OriginalReference reference
    )
    {
        if (kind is "Scan" or "SAAScanComplete")
        {
            // Identity aliases stay immutable while per-body detail references advance.
            Execute(
                connection,
                "INSERT OR IGNORE INTO collection_anchors(batch_id,source_id,offset,hash) VALUES(@batch,@source,@offset,@hash);",
                (BatchParameter, batch),
                (SourceParameter, reference.SourceId),
                (OffsetParameter, reference.Offset),
                (HashParameter, reference.Hash)
            );
        }
        Execute(
            connection,
            """
            INSERT INTO observations(batch_id,body_key,kind,source_id,offset,hash) VALUES(@batch,@body,@kind,@source,@offset,@hash)
            ON CONFLICT(batch_id,body_key,kind) DO UPDATE SET source_id=excluded.source_id,offset=excluded.offset,hash=excluded.hash;
            """,
            (BatchParameter, batch),
            ("@body", body),
            ("@kind", kind),
            (SourceParameter, reference.SourceId),
            (OffsetParameter, reference.Offset),
            (HashParameter, reference.Hash)
        );
    }

    private static long Settlement(
        SqliteConnection connection,
        string kind,
        JournalEventEnvelope entry,
        OriginalReference reference,
        long? earnings
    )
    {
        using SqliteCommand command = Command(
            connection,
            """
            INSERT INTO settlements(kind,source_id,offset,hash,timestamp,earnings) VALUES(@kind,@source,@offset,@hash,@time,@earnings)
            ON CONFLICT(source_id,offset) DO UPDATE SET kind=excluded.kind RETURNING id;
            """,
            ("@kind", kind),
            (SourceParameter, reference.SourceId),
            (OffsetParameter, reference.Offset),
            (HashParameter, reference.Hash),
            (TimeParameter, Timestamp(entry.Timestamp)),
            ("@earnings", earnings)
        );
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void ApplySale(SqliteConnection connection, JournalEventEnvelope entry, OriginalReference reference)
    {
        long earnings =
            Integer(entry.Payload, "TotalEarnings")
            ?? (Integer(entry.Payload, "BaseValue") ?? 0) + (Integer(entry.Payload, "Bonus") ?? 0);
        long settlement = Settlement(connection, "sale", entry, reference, Math.Max(0, earnings));
        string[] names = SoldSystems(entry.Payload).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length == 0)
        {
            SetMetadata(connection, "notice", "A sale did not identify its systems; unconfirmed data remains unsold.");
        }
        foreach (string name in names)
        {
            Execute(
                connection,
                """
                UPDATE bodies SET paid_value=paid_value+pending_value,pending_value=0,batch_id=NULL
                WHERE system_key IN (SELECT key FROM systems WHERE name=@name COLLATE NOCASE);
                UPDATE batches SET status=1,settled_time=@time,settlement_id=@settlement
                WHERE status=0 AND system_key IN (SELECT key FROM systems WHERE name=@name COLLATE NOCASE);
                DELETE FROM metadata WHERE key LIKE @unknown AND value IN (SELECT key FROM systems WHERE name=@name COLLATE NOCASE);
                """,
                (NameParameter, name),
                ("@unknown", UnknownMappingPrefix + "%"),
                (TimeParameter, Timestamp(entry.Timestamp)),
                ("@settlement", settlement)
            );
        }
    }

    private static IEnumerable<string> SoldSystems(JsonElement root)
    {
        if (root.TryGetProperty("Systems", out JsonElement systems) && systems.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement system in systems.EnumerateArray())
            {
                if (system.ValueKind == JsonValueKind.String && system.GetString() is { Length: > 0 } name)
                {
                    yield return name.Trim();
                }
            }
        }
        foreach (string name in DiscoveredSystems(root))
        {
            yield return name;
        }
    }

    private static IEnumerable<string> DiscoveredSystems(JsonElement root)
    {
        if (
            root.TryGetProperty("Discovered", out JsonElement discoveries)
            && discoveries.ValueKind == JsonValueKind.Array
        )
        {
            foreach (JsonElement discovery in discoveries.EnumerateArray())
            {
                if (
                    discovery.ValueKind == JsonValueKind.Object
                    && Text(discovery, "SystemName") is { Length: > 0 } name
                )
                {
                    yield return name.Trim();
                }
            }
        }
    }

    private static void ApplyRespawn(
        SqliteConnection connection,
        JournalEventEnvelope entry,
        OriginalReference reference
    )
    {
        string? option = Text(entry.Payload, "Option");
        if (option is "rebuy" or "free" || (Flag(entry.Payload, "Bankrupt") && option != "rejoin"))
        {
            long settlement = Settlement(connection, "loss", entry, reference, null);
            string? died = ReadMetadata(connection, "pending_death");
            Execute(
                connection,
                """
                UPDATE batches SET status=2,settled_time=@time,settlement_id=@settlement WHERE status=0;
                UPDATE bodies SET pending_value=0,batch_id=NULL;
                DELETE FROM metadata WHERE key LIKE @unknown;
                """,
                (TimeParameter, string.IsNullOrEmpty(died) ? Timestamp(entry.Timestamp) : died),
                ("@unknown", UnknownMappingPrefix + "%"),
                ("@settlement", settlement)
            );
        }
        Execute(connection, "DELETE FROM metadata WHERE key='pending_death';");
    }

    private static void ApplyAdditionalDetails(
        SqliteConnection connection,
        SourceContext context,
        JournalEventEnvelope entry,
        OriginalReference reference
    )
    {
        string? system = EnsureSystem(connection, context, entry.Payload);
        string? body = system is null ? null : BodyKey(connection, system, entry.Payload);
        if (body is null)
        {
            return;
        }
        BodyOwnership owned = ReadBody(connection, body);
        if (owned.BatchId is { } batch)
        {
            RecordObservation(connection, batch, body, entry.EventName, reference);
        }
    }
}
