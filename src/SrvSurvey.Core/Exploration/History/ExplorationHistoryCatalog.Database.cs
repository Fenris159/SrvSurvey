using Microsoft.Data.Sqlite;

namespace SrvSurvey.Core.Exploration.History;

public sealed partial class ExplorationHistoryCatalog
{
    private const string TimeParameter = "@time";

    private SqliteConnection Open(bool initialize = true)
    {
        if (initialize)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        }
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = FilePath,
                Mode = initialize ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 10,
            }.ToString()
        );
        try
        {
            connection.Open();
            using SqliteCommand version = Command(connection, "PRAGMA user_version;");
            if (Convert.ToInt32(version.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 1)
            {
                throw new InvalidDataException(
                    "This exploration history catalog requires a newer application version."
                );
            }
            if (!initialize)
            {
                ValidateScope(connection);
                return connection;
            }
            Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
            Execute(
                connection,
                """
                CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS sources (
                    id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE, path TEXT NOT NULL,
                    first_time TEXT, prefix_hash TEXT NOT NULL DEFAULT '', length INTEGER NOT NULL DEFAULT 0, modified_ticks INTEGER NOT NULL DEFAULT 0,
                    next_offset INTEGER NOT NULL DEFAULT 0, last_offset INTEGER NOT NULL DEFAULT -1, last_hash TEXT NOT NULL DEFAULT '',
                    context TEXT NOT NULL DEFAULT '{}');
                CREATE TABLE IF NOT EXISTS systems (
                    key TEXT PRIMARY KEY, name TEXT NOT NULL COLLATE NOCASE, address INTEGER,
                    x REAL, y REAL, z REAL);
                CREATE TABLE IF NOT EXISTS settlements (
                    id INTEGER PRIMARY KEY, kind TEXT NOT NULL, source_id INTEGER NOT NULL REFERENCES sources(id),
                    offset INTEGER NOT NULL, hash TEXT NOT NULL, timestamp TEXT, earnings INTEGER,
                    UNIQUE(source_id, offset));
                CREATE TABLE IF NOT EXISTS batches (
                    id INTEGER PRIMARY KEY, system_key TEXT NOT NULL REFERENCES systems(key),
                    first_time TEXT, last_time TEXT, status INTEGER NOT NULL DEFAULT 0,
                    estimate INTEGER NOT NULL DEFAULT 0, scans INTEGER NOT NULL DEFAULT 0, maps INTEGER NOT NULL DEFAULT 0,
                    settled_time TEXT, settlement_id INTEGER REFERENCES settlements(id));
                CREATE INDEX IF NOT EXISTS batch_status ON batches(status, last_time);
                CREATE TABLE IF NOT EXISTS bodies (
                    key TEXT PRIMARY KEY, system_key TEXT NOT NULL REFERENCES systems(key),
                    paid_value INTEGER NOT NULL DEFAULT 0, pending_value INTEGER NOT NULL DEFAULT 0,
                    batch_id INTEGER REFERENCES batches(id),
                    scan_source_id INTEGER REFERENCES sources(id), scan_offset INTEGER, scan_hash TEXT);
                CREATE TABLE IF NOT EXISTS observations (
                    id INTEGER PRIMARY KEY, batch_id INTEGER NOT NULL REFERENCES batches(id),
                    body_key TEXT NOT NULL, kind TEXT NOT NULL,
                    source_id INTEGER NOT NULL REFERENCES sources(id), offset INTEGER NOT NULL, hash TEXT NOT NULL,
                    UNIQUE(batch_id, body_key, kind));
                CREATE INDEX IF NOT EXISTS observation_batch ON observations(batch_id);
                CREATE INDEX IF NOT EXISTS observation_source ON observations(source_id,offset);
                PRAGMA user_version=1;
                """
            );
            ValidateScope(connection);
            SetMetadata(connection, "commander", frontierId);
            SetMetadata(connection, "galaxy", isLive ? "live" : "legacy");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private void ValidateScope(SqliteConnection connection)
    {
        string? owner = ReadMetadata(connection, "commander");
        string? galaxy = ReadMetadata(connection, "galaxy");
        if (
            (owner is not null && !string.Equals(owner, frontierId, StringComparison.OrdinalIgnoreCase))
            || (galaxy is not null && galaxy != (isLive ? "live" : "legacy"))
        )
        {
            throw new InvalidDataException(
                "The exploration history catalog belongs to a different commander or galaxy."
            );
        }
    }

    private static SqliteCommand Command(
        SqliteConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters
    )
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return command;
    }

    private static void Execute(
        SqliteConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters
    )
    {
        using SqliteCommand command = Command(connection, sql, parameters);
        command.ExecuteNonQuery();
    }

    private static void SetMetadata(SqliteConnection connection, string key, string value) =>
        Execute(
            connection,
            "INSERT INTO metadata(key,value) VALUES(@key,@value) ON CONFLICT(key) DO UPDATE SET value=excluded.value;",
            ("@key", key),
            ("@value", value)
        );

    private static string? ReadMetadata(SqliteConnection connection, string key)
    {
        using SqliteCommand command = Command(connection, "SELECT value FROM metadata WHERE key=@key;", ("@key", key));
        return command.ExecuteScalar() as string;
    }

    private static ExplorationHistorySummary ReadSummary(SqliteConnection connection)
    {
        long[] values = new long[3];
        int[] counts = new int[3];
        using (
            SqliteCommand command = Command(
                connection,
                "SELECT status, COALESCE(SUM(estimate),0), COUNT(*) FROM batches GROUP BY status;"
            )
        )
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                int status = reader.GetInt32(0);
                values[status] = reader.GetInt64(1);
                counts[status] = reader.GetInt32(2);
            }
        }
        using SqliteCommand earnings = Command(
            connection,
            "SELECT COALESCE(SUM(earnings),0) FROM settlements WHERE kind='sale';"
        );
        using SqliteCommand sources = Command(connection, "SELECT COUNT(*) FROM sources;");
        using SqliteCommand losses = Command(connection, "SELECT COUNT(*) FROM settlements WHERE kind='loss';");
        bool hasDataEvidence =
            counts.Sum() > 0
            || Convert.ToInt64(losses.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
        return new ExplorationHistorySummary
        {
            UnsoldValue = values[0],
            SoldValue = values[1],
            LostValue = values[2],
            UnsoldSystems = counts[0],
            SoldSystems = counts[1],
            LostSystems = counts[2],
            SaleEarnings = Convert.ToInt64(earnings.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture),
            SourceFiles = Convert.ToInt32(sources.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture),
            HasCommanderHistory = ReadMetadata(connection, "commander_seen") == "1" && hasDataEvidence,
            IsComplete = ReadMetadata(connection, "complete") == "1",
            Notice = ReadMetadata(connection, "notice"),
        };
    }
}
