using Microsoft.Data.Sqlite;
using ServerHub.Utils;
using System.Timers;

namespace ServerHub.Storage;

/// <summary>
/// Main storage service managing SQLite database connection lifecycle.
/// Implements singleton pattern to ensure a single database connection.
/// Includes automatic cleanup and monitoring.
/// </summary>
public class StorageService : IDisposable
{
    private static StorageService? _instance;
    private static readonly object _lock = new();
    private SqliteConnection? _connection;
    private readonly string _databasePath;
    private readonly StorageConfig _config;
    private System.Timers.Timer? _cleanupTimer;
    private DateTime _lastCleanup = DateTime.MinValue;
    private DateTime _lastVacuum = DateTime.MinValue;
    private DateTime _lastSizeCheck = DateTime.MinValue;
    private bool _disposed;

    /// <summary>
    /// Gets the singleton instance of the storage service.
    /// </summary>
    public static StorageService Instance
    {
        get
        {
            lock (_lock)
            {
                if (_instance == null)
                    throw new InvalidOperationException("StorageService has not been initialized. Call Initialize() first.");
                return _instance;
            }
        }
    }

    /// <summary>
    /// Private constructor for singleton pattern.
    /// </summary>
    /// <param name="databasePath">Path to the SQLite database file.</param>
    /// <param name="config">Storage configuration.</param>
    private StorageService(string databasePath, StorageConfig config)
    {
        _databasePath = databasePath;
        _config = config;
    }

    /// <summary>
    /// Initializes the storage service with the specified configuration.
    /// </summary>
    /// <param name="config">Storage configuration. If null, uses default configuration.</param>
    /// <returns>The singleton instance.</returns>
    public static StorageService Initialize(StorageConfig? config = null)
    {
        lock (_lock)
        {
            if (_instance != null)
            {
                Logger.Debug("StorageService already initialized", "Storage");
                return _instance;
            }

            // Use default config if not specified
            config ??= new StorageConfig();

            // Expand path and ensure directory exists
            var databasePath = ExpandPath(config.DatabasePath);
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            _instance = new StorageService(databasePath, config);
            _instance.Connect();
            _instance.StartCleanupTimer();

            Logger.Info($"Storage initialized: {databasePath}", "Storage");
            return _instance;
        }
    }

    /// <summary>
    /// Gets the default database path: ~/.config/serverhub/serverhub.db
    /// </summary>
    private static string GetDefaultDatabasePath()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configDir = Path.Combine(homeDir, ".config", "serverhub");
        return Path.Combine(configDir, "serverhub.db");
    }

    /// <summary>
    /// Opens the database connection and applies migrations.
    /// </summary>
    private void Connect()
    {
        if (_connection != null)
            return;

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        // Enable Write-Ahead Logging for better concurrency
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }

        // Apply database migrations
        DatabaseMigrations.ApplyMigrations(_connection);
    }

    /// <summary>
    /// Gets a repository instance scoped to a specific widget.
    /// </summary>
    /// <param name="widgetId">The widget identifier.</param>
    /// <returns>A new repository instance for the widget.</returns>
    public WidgetDataRepository GetRepository(string widgetId)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database connection is not open");
        if (string.IsNullOrWhiteSpace(widgetId))
            throw new ArgumentException("Widget ID cannot be empty", nameof(widgetId));

        return new WidgetDataRepository(_connection, widgetId);
    }

    /// <summary>
    /// Executes a custom SQL command (for maintenance/advanced operations).
    /// </summary>
    /// <param name="sql">The SQL command to execute.</param>
    /// <returns>The number of rows affected.</returns>
    public int ExecuteNonQuery(string sql)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database connection is not open");

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Performs database optimization (VACUUM and ANALYZE).
    /// Should be called periodically during maintenance.
    /// </summary>
    public void Optimize()
    {
        if (_connection == null)
            throw new InvalidOperationException("Database connection is not open");

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "VACUUM;";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "ANALYZE;";
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Gets the database file size in bytes.
    /// </summary>
    public long GetDatabaseSize()
    {
        if (!File.Exists(_databasePath))
            return 0;
        return new FileInfo(_databasePath).Length;
    }

    /// <summary>
    /// Gets database statistics.
    /// </summary>
    public DatabaseStats GetStats()
    {
        if (_connection == null)
            throw new InvalidOperationException("Database connection is not open");

        var stats = new DatabaseStats
        {
            DatabasePath = _databasePath,
            DatabaseSizeMB = GetDatabaseSize() / (1024.0 * 1024.0),
            LastCleanup = _lastCleanup,
            LastVacuum = _lastVacuum
        };

        // Get record count
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM widget_data;";
            stats.TotalRecords = Convert.ToInt64(cmd.ExecuteScalar());
        }

        // Get widget count
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(DISTINCT widget_id) FROM widget_data;";
            stats.WidgetCount = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Get oldest timestamp
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT MIN(timestamp) FROM widget_data;";
            var result = cmd.ExecuteScalar();
            if (result != null && result != DBNull.Value)
            {
                stats.OldestRecord = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(result)).DateTime;
            }
        }

        // Get newest timestamp
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT MAX(timestamp) FROM widget_data;";
            var result = cmd.ExecuteScalar();
            if (result != null && result != DBNull.Value)
            {
                stats.NewestRecord = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(result)).DateTime;
            }
        }

        return stats;
    }

    /// <summary>
    /// Starts the automatic cleanup timer.
    /// </summary>
    private void StartCleanupTimer()
    {
        if (_config.CleanupIntervalHours <= 0)
        {
            Logger.Info("Automatic cleanup disabled (cleanup_interval_hours <= 0)", "Storage");
            return;
        }

        var intervalMs = _config.CleanupIntervalHours * 60 * 60 * 1000;
        _cleanupTimer = new System.Timers.Timer(intervalMs);
        _cleanupTimer.Elapsed += OnCleanupTimer;
        _cleanupTimer.AutoReset = true;
        _cleanupTimer.Start();

        Logger.Info($"Cleanup timer started: every {_config.CleanupIntervalHours}h, retention {_config.RetentionDays}d", "Storage");
    }

    /// <summary>
    /// Cleanup timer callback.
    /// </summary>
    private void OnCleanupTimer(object? sender, ElapsedEventArgs e)
    {
        try
        {
            RunCleanup();
        }
        catch (Exception ex)
        {
            Logger.Error($"Cleanup timer error: {ex.Message}", ex, "Storage");
        }
    }

    /// <summary>
    /// Runs database cleanup: deletes old data and optionally runs VACUUM.
    /// </summary>
    /// <returns>Number of records deleted.</returns>
    public int RunCleanup()
    {
        if (_connection == null)
            throw new InvalidOperationException("Database connection is not open");

        Logger.Info($"Running cleanup: retention {_config.RetentionDays} days", "Storage");

        // Check database size before cleanup
        CheckDatabaseSize();

        var totalDeleted = 0;
        var cutoffTimestamp = DateTimeOffset.UtcNow.AddDays(-_config.RetentionDays).ToUnixTimeSeconds();

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = @"
                DELETE FROM widget_data
                WHERE timestamp < @cutoff_timestamp;
            ";
            cmd.Parameters.AddWithValue("@cutoff_timestamp", cutoffTimestamp);
            totalDeleted = cmd.ExecuteNonQuery();
        }

        _lastCleanup = DateTime.UtcNow;

        if (totalDeleted > 0)
        {
            Logger.Info($"Cleanup completed: deleted {totalDeleted} records older than {_config.RetentionDays} days", "Storage");

            // Run VACUUM if enabled
            if (_config.AutoVacuum)
            {
                var sizeBefore = GetDatabaseSize();
                Logger.Info("Running VACUUM to reclaim space...", "Storage");
                Optimize();
                var sizeAfter = GetDatabaseSize();
                var reclaimedMB = (sizeBefore - sizeAfter) / (1024.0 * 1024.0);
                Logger.Info($"VACUUM completed: reclaimed {reclaimedMB:F2} MB", "Storage");
                _lastVacuum = DateTime.UtcNow;
            }
        }
        else
        {
            Logger.Debug($"Cleanup completed: no records older than {_config.RetentionDays} days", "Storage");
        }

        // Check size after cleanup
        CheckDatabaseSize();

        return totalDeleted;
    }

    /// <summary>
    /// Checks database size and logs warnings if exceeds configured maximum.
    /// </summary>
    private void CheckDatabaseSize()
    {
        // Throttle size checks to once per hour
        if ((DateTime.UtcNow - _lastSizeCheck).TotalHours < 1)
            return;

        _lastSizeCheck = DateTime.UtcNow;

        var sizeMB = GetDatabaseSize() / (1024.0 * 1024.0);
        if (sizeMB > _config.MaxDatabaseSizeMb)
        {
            Logger.Warning($"Database size ({sizeMB:F2} MB) exceeds configured maximum ({_config.MaxDatabaseSizeMb} MB). Consider reducing retention_days or running cleanup.", "Storage");
        }
        else
        {
            Logger.Debug($"Database size: {sizeMB:F2} MB (max: {_config.MaxDatabaseSizeMb} MB)", "Storage");
        }
    }

    /// <summary>
    /// Expands ~ and environment variables in a path.
    /// </summary>
    private static string ExpandPath(string path)
    {
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(home, path.Substring(2));
        }
        return Environment.ExpandEnvironmentVariables(path);
    }

    /// <summary>
    /// Shuts down the storage service and closes the database connection.
    /// </summary>
    public static void Shutdown()
    {
        lock (_lock)
        {
            _instance?.Dispose();
            _instance = null;
        }
    }

    /// <summary>
    /// Disposes the database connection and cleanup timer.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _cleanupTimer?.Stop();
        _cleanupTimer?.Dispose();
        _cleanupTimer = null;

        _connection?.Dispose();
        _connection = null;
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Database statistics.
/// </summary>
public class DatabaseStats
{
    public string DatabasePath { get; set; } = "";
    public double DatabaseSizeMB { get; set; }
    public long TotalRecords { get; set; }
    public int WidgetCount { get; set; }
    public DateTime? OldestRecord { get; set; }
    public DateTime? NewestRecord { get; set; }
    public DateTime LastCleanup { get; set; }
    public DateTime LastVacuum { get; set; }
}
