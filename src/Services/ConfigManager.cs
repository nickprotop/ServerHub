// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using ServerHub.Models;
using ServerHub.Utils;

namespace ServerHub.Services;

/// <summary>
/// Manages ServerHub configuration loading and saving
/// </summary>
public class ConfigManager
{
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    public ConfigManager()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        _serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
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
    /// Creates a default configuration file
    /// </summary>
    /// <param name="configPath">Path where to create the configuration</param>
    public void CreateDefaultConfig(string configPath)
    {
        var config = new ServerHubConfig
        {
            DefaultRefresh = 5,
            Widgets = new Dictionary<string, WidgetConfig>
            {
                ["cpu"] = new WidgetConfig
                {
                    Path = "cpu.sh",
                    Refresh = 2,
                    Priority = 1,
                    Pinned = false
                },
                ["memory"] = new WidgetConfig
                {
                    Path = "memory.sh",
                    Refresh = 2,
                    Priority = 1,
                    Pinned = false
                },
                ["disk"] = new WidgetConfig
                {
                    Path = "disk.sh",
                    Refresh = 10,
                    Priority = 2,
                    Pinned = false
                }
            },
            Layout = new LayoutConfig
            {
                Order = new List<string> { "cpu", "memory", "disk" }
            }
        };

        SaveConfig(config, configPath);
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
        if (config.Widgets.Count == 0)
        {
            throw new InvalidOperationException("Configuration must contain at least one widget");
        }

        foreach (var (widgetId, widgetConfig) in config.Widgets)
        {
            if (string.IsNullOrWhiteSpace(widgetConfig.Path))
            {
                throw new InvalidOperationException($"Widget '{widgetId}' has no path specified");
            }

            if (widgetConfig.Refresh < 1)
            {
                throw new InvalidOperationException($"Widget '{widgetId}' has invalid refresh interval: {widgetConfig.Refresh}");
            }

            if (widgetConfig.Priority < 1 || widgetConfig.Priority > 3)
            {
                throw new InvalidOperationException($"Widget '{widgetId}' has invalid priority: {widgetConfig.Priority}. Must be 1-3.");
            }
        }

        // Validate layout order references existing widgets
        if (config.Layout?.Order != null)
        {
            foreach (var widgetId in config.Layout.Order)
            {
                if (!config.Widgets.ContainsKey(widgetId))
                {
                    throw new InvalidOperationException($"Layout references unknown widget: '{widgetId}'");
                }
            }
        }
    }
}
