using Microsoft.Data.Sqlite;

namespace SrvSurvey.Core.Exploration.History;

public sealed partial class ExplorationHistoryCatalog
{
    /// <summary>Retains the original parent key when an addressless system gains an identifier. Distinct known addresses never merge by name.</summary>
    private static string? KnownSystemKey(SqliteConnection connection, long? address, string? name)
    {
        if (address is not null)
        {
            using SqliteCommand byAddress = Command(
                connection,
                "SELECT key FROM systems WHERE address=@address;",
                ("@address", address)
            );
            if (byAddress.ExecuteScalar() is string known)
            {
                return known;
            }
        }
        using SqliteCommand byName = Command(
            connection,
            "SELECT key FROM systems WHERE name=@name COLLATE NOCASE AND (@address IS NULL OR address IS NULL) LIMIT 2;",
            ("@name", name),
            ("@address", address)
        );
        return UniqueKey(byName);
    }

    /// <summary>Body names bridge missing identifiers while keys keep paid ownership, observations, and collection anchors stable.</summary>
    private static string? KnownBodyKey(
        SqliteConnection connection,
        string system,
        long? id,
        string? name,
        out bool ambiguous
    )
    {
        ambiguous = false;
        if (id is not null)
        {
            using SqliteCommand byId = Command(
                connection,
                "SELECT key FROM bodies WHERE system_key=@system AND body_id=@id;",
                ("@system", system),
                ("@id", id)
            );
            if (byId.ExecuteScalar() is string known)
            {
                return ReconcileBodyPlaceholder(connection, known, system, id.Value, name);
            }
        }
        using SqliteCommand byName = Command(
            connection,
            "SELECT key FROM bodies WHERE system_key=@system AND name=@name COLLATE NOCASE AND (@id IS NULL OR body_id IS NULL) LIMIT 2;",
            ("@system", system),
            ("@name", name),
            ("@id", id)
        );
        string? key = UniqueKey(byName, out ambiguous);
        if (key is not null && id is not null)
        {
            if (HasCompetingBodyId(connection, system, name, id.Value))
            {
                return null;
            }
            Execute(connection, "UPDATE bodies SET body_id=@id WHERE key=@key;", ("@id", id), ("@key", key));
        }
        return key;
    }

    /// <summary>Only an unvalued placeholder can merge into the proven body that owns scans and payments.</summary>
    private static string ReconcileBodyPlaceholder(
        SqliteConnection connection,
        string placeholder,
        string system,
        long id,
        string? name
    )
    {
        using SqliteCommand alias = Command(
            connection,
            """
            SELECT key FROM bodies WHERE system_key=@system AND name=@name COLLATE NOCASE AND body_id IS NULL LIMIT 2;
            """,
            (SystemParameter, system),
            (NameParameter, name)
        );
        string? canonical = UniqueKey(alias);
        if (canonical is null || HasCompetingBodyId(connection, system, name, id))
        {
            return placeholder;
        }
        if (!IsUnvaluedBodyPlaceholder(connection, placeholder))
        {
            if (!IsUnvaluedBodyPlaceholder(connection, canonical))
            {
                return placeholder;
            }
            (canonical, placeholder) = (placeholder, canonical);
        }
        Execute(
            connection,
            """
            INSERT OR IGNORE INTO metadata(key,value)
                SELECT @canonicalGuard,value FROM metadata WHERE key=@placeholderGuard;
            DELETE FROM metadata WHERE key=@placeholderGuard;
            INSERT OR IGNORE INTO pending_landings(body_key,source_id,offset,hash)
                SELECT @canonical,source_id,offset,hash FROM pending_landings WHERE body_key=@placeholder;
            DELETE FROM pending_landings WHERE body_key=@placeholder;
            DELETE FROM bodies WHERE key=@placeholder;
            UPDATE bodies SET body_id=@id,name=COALESCE(name,@name) WHERE key=@canonical;
            """,
            ("@canonicalGuard", UnknownMappingPrefix + canonical),
            ("@placeholderGuard", UnknownMappingPrefix + placeholder),
            ("@placeholder", placeholder),
            ("@id", id),
            (NameParameter, name),
            ("@canonical", canonical)
        );
        if (ReadBody(connection, canonical).BatchId is { } batch)
        {
            AttachPendingLanding(connection, batch, canonical);
        }
        return canonical;
    }

    private static bool HasCompetingBodyId(SqliteConnection connection, string system, string? name, long id)
    {
        using SqliteCommand command = Command(
            connection,
            "SELECT EXISTS(SELECT 1 FROM bodies WHERE system_key=@system AND name=@name COLLATE NOCASE AND body_id IS NOT NULL AND body_id<>@id);",
            (SystemParameter, system),
            (NameParameter, name),
            ("@id", id)
        );
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != 0;
    }

    private static bool IsUnvaluedBodyPlaceholder(SqliteConnection connection, string body)
    {
        using SqliteCommand command = Command(
            connection,
            "SELECT COUNT(*) FROM bodies WHERE key=@body AND scan_source_id IS NULL AND paid_value=0 AND pending_value=0 AND batch_id IS NULL;",
            (BodyParameter, body)
        );
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private static string? UniqueKey(SqliteCommand command) => UniqueKey(command, out _);

    private static string? UniqueKey(SqliteCommand command, out bool ambiguous)
    {
        ambiguous = false;
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }
        string key = reader.GetString(0);
        ambiguous = reader.Read();
        return ambiguous ? null : key;
    }
}
