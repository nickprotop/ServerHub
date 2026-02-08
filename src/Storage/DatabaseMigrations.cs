using Microsoft.Data.Sqlite;

namespace ServerHub.Storage;

/// <summary>
/// Manages database schema versioning and migrations.
/// </summary>
public class DatabaseMigrations
{
    private const int CurrentVersion = 1;

    /// <summary>
    /// Applies all necessary migrations to bring the database to the current version.
    /// </summary>
    /// <param name="connection">The SQLite database connection.</param>
    public static void ApplyMigrations(SqliteConnection connection)
    {
        var currentVersion = GetSchemaVersion(connection);

        if (currentVersion == 0)
        {
            // Fresh database - create initial schema
            ApplyMigration_V1(connection);
            SetSchemaVersion(connection, 1);
        }
        else if (currentVersion < CurrentVersion)
        {
            // Future migrations would go here
            // Example:
            // if (currentVersion < 2)
            // {
            //     ApplyMigration_V2(connection);
            //     SetSchemaVersion(connection, 2);
            // }
        }
        else if (currentVersion > CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {currentVersion} is newer than application version {CurrentVersion}. " +
                "Please update ServerHub to the latest version.");
        }
    }

    /// <summary>
    /// Gets the current schema version from the database.
    /// </summary>
    /// <param name="connection">The SQLite database connection.</param>
    /// <returns>The schema version, or 0 if the database is new.</returns>
    private static int GetSchemaVersion(SqliteConnection connection)
    {
        // Check if schema_version table exists
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = @"
            SELECT name FROM sqlite_master
            WHERE type='table' AND name='schema_version';
        ";
        var exists = checkCmd.ExecuteScalar() != null;

        if (!exists)
            return 0;

        // Get current version
        using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var result = versionCmd.ExecuteScalar();
        return result != null ? Convert.ToInt32(result) : 0;
    }

    /// <summary>
    /// Sets the schema version in the database.
    /// </summary>
    /// <param name="connection">The SQLite database connection.</param>
    /// <param name="version">The version number to set.</param>
    private static void SetSchemaVersion(SqliteConnection connection, int version)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO schema_version (version) VALUES (@version);
        ";
        cmd.Parameters.AddWithValue("@version", version);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Applies the initial database schema (version 1).
    /// </summary>
    /// <param name="connection">The SQLite database connection.</param>
    private static void ApplyMigration_V1(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            -- Schema version tracking
            CREATE TABLE schema_version (
                version INTEGER PRIMARY KEY
            );

            -- Widget data storage
            CREATE TABLE widget_data (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                widget_id TEXT NOT NULL,
                measurement TEXT NOT NULL,
                tags TEXT,
                timestamp INTEGER NOT NULL,
                field_name TEXT NOT NULL,
                field_value REAL,
                field_text TEXT,
                field_json TEXT,
                UNIQUE(widget_id, measurement, tags, timestamp, field_name)
            );

            -- Indexes for efficient queries
            CREATE INDEX idx_widget_data_lookup
                ON widget_data(widget_id, measurement, timestamp DESC);

            CREATE INDEX idx_widget_data_field
                ON widget_data(widget_id, measurement, field_name, timestamp DESC);

            CREATE INDEX idx_widget_data_cleanup
                ON widget_data(timestamp);
        ";
        cmd.ExecuteNonQuery();
    }
}
