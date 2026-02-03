using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using ServerHub.Models;
using ServerHub.Services;

namespace ServerHub.Marketplace.Services;

/// <summary>
/// Helper for adding marketplace widgets to config.yaml
/// </summary>
public class ConfigHelper
{
    /// <summary>
    /// Adds a widget to the config file
    /// </summary>
    /// <param name="configPath">Path to config.yaml</param>
    /// <param name="widgetId">Widget ID to use as config key</param>
    /// <param name="widgetPath">Path to widget script</param>
    /// <param name="sha256">SHA256 checksum</param>
    /// <param name="refreshInterval">Refresh interval in seconds</param>
    /// <param name="expandedRefreshInterval">Optional expanded refresh interval</param>
    /// <param name="source">Source: "marketplace", "bundled", or null</param>
    /// <param name="marketplaceId">Original marketplace widget ID (for tracking updates)</param>
    /// <param name="marketplaceVersion">Version from marketplace (for update detection)</param>
    public static bool AddWidgetToConfig(
        string configPath,
        string widgetId,
        string widgetPath,
        string sha256,
        int refreshInterval,
        int? expandedRefreshInterval = null,
        string? source = null,
        string? marketplaceId = null,
        string? marketplaceVersion = null)
    {
        try
        {
            var configManager = new ConfigManager();
            ServerHubConfig config;

            // Load existing config or create new one
            if (File.Exists(configPath))
            {
                config = configManager.LoadConfig(configPath);
            }
            else
            {
                config = new ServerHubConfig
                {
                    Widgets = new Dictionary<string, WidgetConfig>(),
                    Layout = new LayoutConfig { Order = new List<string>() }
                };
            }

            // Check if widget already exists by config key
            if (config.Widgets.ContainsKey(widgetId))
            {
                return false; // Already exists
            }

            // Check if marketplace widget already installed under different config key
            if (!string.IsNullOrEmpty(marketplaceId))
            {
                var existingMarketplaceWidget = config.Widgets
                    .FirstOrDefault(w => w.Value.MarketplaceId == marketplaceId);

                if (existingMarketplaceWidget.Key != null)
                {
                    return false; // Same marketplace widget already installed
                }
            }

            // Add widget configuration
            config.Widgets[widgetId] = new WidgetConfig
            {
                Path = widgetPath,
                Refresh = refreshInterval,
                ExpandedRefresh = expandedRefreshInterval,
                Sha256 = sha256,
                Source = source,
                MarketplaceId = marketplaceId,
                MarketplaceVersion = marketplaceVersion
            };

            // Add to layout order
            if (config.Layout?.Order != null)
            {
                config.Layout.Order.Add(widgetId);
            }

            // Save config
            configManager.SaveConfig(config, configPath);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a widget already exists in the config (by config key or marketplace_id)
    /// </summary>
    public static bool WidgetExistsInConfig(string configPath, string widgetId, string? marketplaceId = null)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                return false;
            }

            var configManager = new ConfigManager();
            var config = configManager.LoadConfig(configPath);

            // Check by config key
            if (config.Widgets.ContainsKey(widgetId))
            {
                return true;
            }

            // Check by marketplace_id if provided
            if (!string.IsNullOrEmpty(marketplaceId))
            {
                return config.Widgets.Any(w => w.Value.MarketplaceId == marketplaceId);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Updates widget version and SHA256 in config after update
    /// </summary>
    /// <param name="configPath">Path to config.yaml</param>
    /// <param name="widgetId">Config key for the widget</param>
    /// <param name="newVersion">New version from marketplace</param>
    /// <param name="newSha256">New SHA256 checksum</param>
    /// <returns>True if update successful</returns>
    public static bool UpdateWidgetVersionInConfig(
        string configPath,
        string widgetId,
        string newVersion,
        string newSha256)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                return false;
            }

            var configManager = new ConfigManager();
            var config = configManager.LoadConfig(configPath);

            // Find widget by config key
            if (!config.Widgets.ContainsKey(widgetId))
            {
                return false;
            }

            // Update version and SHA256
            config.Widgets[widgetId].MarketplaceVersion = newVersion;
            config.Widgets[widgetId].Sha256 = newSha256;

            // Save config
            configManager.SaveConfig(config, configPath);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
