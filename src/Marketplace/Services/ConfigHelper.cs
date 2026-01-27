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
    public static bool AddWidgetToConfig(
        string configPath,
        string widgetId,
        string widgetPath,
        string sha256,
        int refreshInterval)
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

            // Check if widget already exists
            if (config.Widgets.ContainsKey(widgetId))
            {
                return false; // Already exists
            }

            // Add widget configuration
            config.Widgets[widgetId] = new WidgetConfig
            {
                Path = widgetPath,
                Refresh = refreshInterval,
                Sha256 = sha256
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
    /// Checks if a widget already exists in the config
    /// </summary>
    public static bool WidgetExistsInConfig(string configPath, string widgetId)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                return false;
            }

            var configManager = new ConfigManager();
            var config = configManager.LoadConfig(configPath);
            return config.Widgets.ContainsKey(widgetId);
        }
        catch
        {
            return false;
        }
    }
}
