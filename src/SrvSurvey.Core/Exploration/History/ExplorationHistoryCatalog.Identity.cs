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
    private static string? KnownBodyKey(SqliteConnection connection, string system, long? id, string? name)
    {
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
        string? key = UniqueKey(byName);
        if (key is not null && id is not null)
        {
            Execute(connection, "UPDATE bodies SET body_id=@id WHERE key=@key;", ("@id", id), ("@key", key));
        }
        return key;
    }

    /// <summary>An ID-only mapping placeholder must not replace a previously valued name-only body.</summary>
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
            SELECT key FROM bodies WHERE system_key=@system AND name=@name COLLATE NOCASE AND body_id IS NULL
                AND EXISTS(SELECT 1 FROM bodies WHERE key=@placeholder AND scan_source_id IS NULL
                    AND paid_value=0 AND pending_value=0 AND batch_id IS NULL) LIMIT 2;
            """,
            (SystemParameter, system),
            (NameParameter, name),
            ("@placeholder", placeholder)
        );
        string? canonical = UniqueKey(alias);
        if (canonical is null)
        {
            return placeholder;
        }
        Execute(
            connection,
            """
            INSERT OR IGNORE INTO metadata(key,value)
                SELECT @canonicalGuard,value FROM metadata WHERE key=@placeholderGuard;
            DELETE FROM metadata WHERE key=@placeholderGuard;
            DELETE FROM bodies WHERE key=@placeholder;
            UPDATE bodies SET body_id=@id WHERE key=@canonical;
            """,
            ("@canonicalGuard", UnknownMappingPrefix + canonical),
            ("@placeholderGuard", UnknownMappingPrefix + placeholder),
            ("@placeholder", placeholder),
            ("@id", id),
            ("@canonical", canonical)
        );
        return canonical;
    }

    private static string? UniqueKey(SqliteCommand command)
    {
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }
        string key = reader.GetString(0);
        return reader.Read() ? null : key;
    }
}
