// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using ServerHub.Models;
using ServerHub.Utils;
using ServerHub.Config;
using ServerHub.Exceptions;

namespace ServerHub.Services;

/// <summary>
/// Manages ServerHub configuration loading and saving
/// </summary>
public class ConfigManager
{
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;
    private string? _lastLoadedPath;

    public ConfigManager()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeConverter(new WidgetLocationTypeConverter())
            .IgnoreUnmatchedProperties()
            .Build();

        _serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeConverter(new WidgetLocationTypeConverter())
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
            .Build();
    }

    /// <summary>
    /// Loads configuration from a YAML file
    /// </summary>
    /// <param name="configPath">Path to the configuration file</param>
    /// <returns>Loaded configuration</returns>
    public ServerHubConfig LoadConfig(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Configuration file not found: {configPath}");
        }

        _lastLoadedPath = configPath;
        var yaml = File.ReadAllText(configPath);
        var config = _deserializer.Deserialize<ServerHubConfig>(yaml);

        // Validate configuration
        ValidateConfig(config);

        return config;
    }

    /// <summary>
    /// Saves configuration to a YAML file
    /// </summary>
    /// <param name="config">Configuration to save</param>
    /// <param name="configPath">Path to save the configuration</param>
    public void SaveConfig(ServerHubConfig config, string configPath)
    {
        var yaml = _serializer.Serialize(config);
        File.WriteAllText(configPath, yaml);
    }

    /// <summary>
    /// Creates a default configuration file using the embedded default config
    /// Generated from config.yaml at build time
    /// </summary>
    /// <param name="configPath">Path where to create the configuration</param>
    public void CreateDefaultConfig(string configPath)
    {
        // Use the auto-generated default config embedded at build time
        File.WriteAllText(configPath, DefaultConfig.YamlContent);
    }

    /// <summary>
    /// Gets the default configuration file path
    /// </summary>
    /// <returns>Path to ~/.config/serverhub/config.yaml</returns>
    public static string GetDefaultConfigPath()
    {
        var configDir = WidgetPaths.GetUserConfigDirectory();
        return Path.Combine(configDir, "config.yaml");
    }

    /// <summary>
    /// Validates configuration for common errors
    /// </summary>
    private void ValidateConfig(ServerHubConfig config)
    {
        // Check 1: At least one widget configured (can be disabled)
        if (config.Widgets.Count == 0)
        {
            // Allow empty config - will show empty dashboard
            // User can press F2 to discover and add widgets
            return;
        }

        // Check 2: Validate widget configurations
        foreach (var (widgetId, widgetConfig) in config.Widgets)
        {
            // Path is required
            if (string.IsNullOrWhiteSpace(widgetConfig.Path))
            {
                throw new MissingWidgetPathException(widgetId)
                {
                    ConfigPath = _lastLoadedPath
                };
            }

            // Refresh must be >= 1
            if (widgetConfig.Refresh < 1)
            {
                throw new InvalidRefreshIntervalException(widgetId, widgetConfig.Refresh)
                {
                    ConfigPath = _lastLoadedPath
                };
            }

            // Validate expanded_refresh if set
            if (widgetConfig.ExpandedRefresh.HasValue && widgetConfig.ExpandedRefresh.Value < 1)
            {
                throw new InvalidRefreshIntervalException(
                    widgetId,
                    widgetConfig.ExpandedRefresh.Value,
                    "expanded_refresh")
                {
                    ConfigPath = _lastLoadedPath
                };
            }
        }

        // Check 3: Validate layout order references
        if (config.Layout?.Order != null)
        {
            var availableWidgets = config.Widgets.Keys.ToList();
            var seenWidgets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var widgetId in config.Layout.Order)
            {
                if (!config.Widgets.ContainsKey(widgetId))
                {
                    throw new InvalidLayoutWidgetException(widgetId, availableWidgets)
                    {
                        ConfigPath = _lastLoadedPath
                    };
                }

                // Detect duplicates in layout order
                if (seenWidgets.Contains(widgetId))
                {
                    Console.WriteLine($"Warning: Widget '{widgetId}' appears multiple times in layout.order");
                }
                else
                {
                    seenWidgets.Add(widgetId);
                }
            }
        }

        // Check 4: Validate storage configuration if present
        if (config.Storage != null)
        {
            config.Storage.Validate();
        }
    }
}
