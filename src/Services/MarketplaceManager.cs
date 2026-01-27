// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Marketplace.Models;
using ServerHub.Marketplace.Services;
using ServerHub.Services;
using ServerHub.Config;

namespace ServerHub.Services;

/// <summary>
/// High-level service for managing marketplace operations.
/// Orchestrates RegistryClient, WidgetInstaller, and DependencyChecker.
/// Data is always fetched fresh; in-memory filtering is used for search.
/// </summary>
public class MarketplaceManager
{
    private readonly RegistryClient _registryClient;
    private readonly WidgetInstaller _installer;
    private readonly DependencyChecker _dependencyChecker;
    private readonly string _configPath;

    public MarketplaceManager(string installPath, string configPath)
    {
        _registryClient = new RegistryClient();
        _installer = new WidgetInstaller(_registryClient, installPath);
        _dependencyChecker = new DependencyChecker();
        _configPath = configPath;
    }

    /// <summary>
    /// Combined widget data for UI display
    /// </summary>
    public class MarketplaceWidgetInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public VerificationLevel VerificationLevel { get; set; }
        public WidgetStatus Status { get; set; }
        public string? InstalledVersion { get; set; }
        public bool HasUpdate { get; set; }
        public string ManifestUrl { get; set; } = string.Empty;
    }

    public enum WidgetStatus
    {
        Installed,
        Available,
        UpdateAvailable
    }

    /// <summary>
    /// Fetches registry index (always fresh)
    /// </summary>
    public async Task<RegistryIndex?> GetRegistryIndexAsync()
    {
        return await _registryClient.FetchRegistryIndexAsync();
    }

    /// <summary>
    /// Fetches widget manifest (always fresh - NO caching per plan)
    /// </summary>
    public async Task<WidgetManifest?> GetWidgetManifestAsync(string manifestUrl)
    {
        return await _registryClient.FetchWidgetManifestAsync(manifestUrl);
    }

    /// <summary>
    /// Gets all marketplace widgets with status information
    /// </summary>
    public async Task<List<MarketplaceWidgetInfo>> GetAllWidgetsAsync()
    {
        var index = await GetRegistryIndexAsync();
        if (index == null)
        {
            return new List<MarketplaceWidgetInfo>();
        }

        var result = new List<MarketplaceWidgetInfo>();
        var installedWidgets = GetInstalledWidgets();

        foreach (var registryWidget in index.Widgets)
        {
            var widgetInfo = new MarketplaceWidgetInfo
            {
                Id = registryWidget.Id,
                Name = registryWidget.Name,
                Author = registryWidget.Author,
                Category = registryWidget.Category,
                Description = registryWidget.Description,
                LatestVersion = registryWidget.LatestVersion,
                VerificationLevel = registryWidget.VerificationLevel,
                ManifestUrl = registryWidget.ManifestUrl
            };

            // Check if installed (lookup by marketplace ID)
            if (installedWidgets.TryGetValue(registryWidget.Id, out var installedInfo) &&
                installedInfo.Source == "marketplace")
            {
                widgetInfo.InstalledVersion = installedInfo.MarketplaceVersion;
                widgetInfo.Status = WidgetStatus.Installed;

                // Check if update available
                if (!string.IsNullOrEmpty(installedInfo.MarketplaceVersion) &&
                    CompareVersions(registryWidget.LatestVersion, installedInfo.MarketplaceVersion) > 0)
                {
                    widgetInfo.HasUpdate = true;
                    widgetInfo.Status = WidgetStatus.UpdateAvailable;
                }
            }
            else
            {
                widgetInfo.Status = WidgetStatus.Available;
            }

            result.Add(widgetInfo);
        }

        return result;
    }

    /// <summary>
    /// Searches widgets by query
    /// </summary>
    public async Task<List<MarketplaceWidgetInfo>> SearchWidgetsAsync(string query)
    {
        var allWidgets = await GetAllWidgetsAsync();

        if (string.IsNullOrWhiteSpace(query))
        {
            return allWidgets;
        }

        var lowerQuery = query.ToLower();
        return allWidgets
            .Where(w =>
                w.Name.ToLower().Contains(lowerQuery) ||
                w.Id.ToLower().Contains(lowerQuery) ||
                w.Description.ToLower().Contains(lowerQuery) ||
                w.Category.ToLower().Contains(lowerQuery))
            .ToList();
    }

    /// <summary>
    /// Filters widgets by category
    /// </summary>
    public List<MarketplaceWidgetInfo> FilterByCategory(
        List<MarketplaceWidgetInfo> widgets,
        string? category)
    {
        if (string.IsNullOrEmpty(category) || category.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return widgets;
        }

        return widgets
            .Where(w => w.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Filters widgets by status
    /// </summary>
    public List<MarketplaceWidgetInfo> FilterByStatus(
        List<MarketplaceWidgetInfo> widgets,
        string? statusFilter)
    {
        if (string.IsNullOrEmpty(statusFilter) || statusFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return widgets;
        }

        return statusFilter.ToLower() switch
        {
            "installed" => widgets.Where(w => w.Status == WidgetStatus.Installed).ToList(),
            "available" => widgets.Where(w => w.Status == WidgetStatus.Available).ToList(),
            "updates" => widgets.Where(w => w.HasUpdate).ToList(),
            "verified" => widgets.Where(w => w.VerificationLevel == VerificationLevel.Verified).ToList(),
            "community" => widgets.Where(w => w.VerificationLevel == VerificationLevel.Community).ToList(),
            "unverified" => widgets.Where(w => w.VerificationLevel == VerificationLevel.Unverified).ToList(),
            _ => widgets
        };
    }

    /// <summary>
    /// Checks dependencies for a widget
    /// </summary>
    public List<DependencyChecker.DependencyCheckResult> CheckDependencies(WidgetDependencies? dependencies)
    {
        return _dependencyChecker.CheckDependencies(dependencies);
    }

    /// <summary>
    /// Installs a widget
    /// </summary>
    public async Task<WidgetInstaller.InstallResult> InstallWidgetAsync(
        string widgetId,
        string? version = null,
        bool skipDependencyCheck = false)
    {
        return await _installer.InstallWidgetAsync(widgetId, version, skipDependencyCheck);
    }

    /// <summary>
    /// Info about an installed widget from config
    /// </summary>
    public class InstalledWidgetInfo
    {
        public string ConfigKey { get; set; } = string.Empty;
        public string? MarketplaceId { get; set; }
        public string? MarketplaceVersion { get; set; }
        public string? Source { get; set; }
    }

    /// <summary>
    /// Gets installed widgets from config, keyed by marketplace_id (if set) or config key
    /// </summary>
    private Dictionary<string, InstalledWidgetInfo> GetInstalledWidgets()
    {
        var installed = new Dictionary<string, InstalledWidgetInfo>();

        try
        {
            if (!File.Exists(_configPath))
            {
                return installed;
            }

            var configManager = new ConfigManager();
            var config = configManager.LoadConfig(_configPath);

            foreach (var (configKey, widgetConfig) in config.Widgets)
            {
                var info = new InstalledWidgetInfo
                {
                    ConfigKey = configKey,
                    MarketplaceId = widgetConfig.MarketplaceId,
                    MarketplaceVersion = widgetConfig.MarketplaceVersion,
                    Source = widgetConfig.Source
                };

                // Key by marketplace_id if it's a marketplace widget, otherwise by config key
                var lookupKey = widgetConfig.MarketplaceId ?? configKey;
                installed[lookupKey] = info;
            }
        }
        catch
        {
            // Ignore errors
        }

        return installed;
    }

    /// <summary>
    /// Compares two version strings
    /// </summary>
    private static int CompareVersions(string version1, string version2)
    {
        var v1 = ParseVersion(version1);
        var v2 = ParseVersion(version2);
        return v1.CompareTo(v2);
    }

    private static Version ParseVersion(string version)
    {
        var cleanVersion = version.TrimStart('v');
        return Version.TryParse(cleanVersion, out var v) ? v : new Version(0, 0, 0);
    }

}
