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
                return known;
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
