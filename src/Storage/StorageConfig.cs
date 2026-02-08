// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using YamlDotNet.Serialization;

namespace ServerHub.Storage;

/// <summary>
/// Configuration for the widget data storage system
/// Manages SQLite database settings, retention policies, and cleanup intervals
/// </summary>
public class StorageConfig
{
    /// <summary>
    /// Whether storage is enabled globally
    /// Default: true (storage enabled by default)
    /// </summary>
    [YamlMember(Alias = "enabled", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path to the SQLite database file
    /// Supports ~ expansion for home directory
    /// Default: ~/.config/serverhub/serverhub.db
    /// </summary>
    [YamlMember(Alias = "database_path")]
    public string DatabasePath { get; set; } = "~/.config/serverhub/serverhub.db";

    /// <summary>
    /// Number of days to retain historical widget data
    /// Older data is automatically deleted during cleanup
    /// Default: 30 days
    /// </summary>
    [YamlMember(Alias = "retention_days")]
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Interval in hours between automatic database cleanup operations
    /// Cleanup removes old data based on retention_days
    /// Default: 1 hour
    /// </summary>
    [YamlMember(Alias = "cleanup_interval_hours")]
    public int CleanupIntervalHours { get; set; } = 1;

    /// <summary>
    /// Maximum database size in megabytes
    /// System will warn or stop storing if exceeded
    /// Default: 500 MB
    /// </summary>
    [YamlMember(Alias = "max_database_size_mb")]
    public int MaxDatabaseSizeMb { get; set; } = 500;

    /// <summary>
    /// Whether to enable SQLite auto-vacuum
    /// Auto-vacuum reclaims space from deleted data automatically
    /// Default: true
    /// </summary>
    [YamlMember(Alias = "auto_vacuum")]
    public bool AutoVacuum { get; set; } = true;

    /// <summary>
    /// Validates the configuration values
    /// Throws ConfigurationException if any values are invalid
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DatabasePath))
        {
            throw new Exceptions.ConfigurationException(
                "Storage database_path cannot be empty",
                "Set storage.database_path in config.yaml (e.g., ~/.config/serverhub/serverhub.db)");
        }

        if (RetentionDays < 1)
        {
            throw new Exceptions.ConfigurationException(
                $"Storage retention_days must be >= 1 (got {RetentionDays})",
                $"Set storage.retention_days to 1 or higher in config.yaml");
        }

        if (CleanupIntervalHours < 1)
        {
            throw new Exceptions.ConfigurationException(
                $"Storage cleanup_interval_hours must be >= 1 (got {CleanupIntervalHours})",
                $"Set storage.cleanup_interval_hours to 1 or higher in config.yaml");
        }

        if (MaxDatabaseSizeMb < 1)
        {
            throw new Exceptions.ConfigurationException(
                $"Storage max_database_size_mb must be >= 1 (got {MaxDatabaseSizeMb})",
                $"Set storage.max_database_size_mb to 1 or higher in config.yaml");
        }
    }

    /// <summary>
    /// Expands ~ in DatabasePath to the user's home directory
    /// Returns the expanded absolute path
    /// </summary>
    public string GetExpandedDatabasePath()
    {
        if (DatabasePath.StartsWith("~/"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, DatabasePath[2..]);
        }

        return DatabasePath;
    }
}
