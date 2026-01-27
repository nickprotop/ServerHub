// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Marketplace.Models;
using ServerHub.Marketplace.Services;
using ServerHub.Services;
using ServerHub.Config;

namespace ServerHub.Services;

/// <summary>
/// High-level service for managing marketplace operations
/// Orchestrates RegistryClient, WidgetInstaller, DependencyChecker, and MarketplaceCache
/// </summary>
public class MarketplaceManager
{
    private readonly RegistryClient _registryClient;
    private readonly WidgetInstaller _installer;
    private readonly DependencyChecker _dependencyChecker;
    private readonly MarketplaceCache _cache;
    private readonly string _configPath;

    public MarketplaceManager(string installPath, string configPath)
    {
        _registryClient = new RegistryClient();
        _installer = new WidgetInstaller(_registryClient, installPath);
        _dependencyChecker = new DependencyChecker();
        _cache = new MarketplaceCache();
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
    /// Fetches registry index with caching
    /// </summary>
    public async Task<RegistryIndex?> GetRegistryIndexAsync(bool forceRefresh = false)
    {
        const string cacheKey = "registry_index";

        if (!forceRefresh)
        {
            var cached = _cache.Get<RegistryIndex>(cacheKey);
            if (cached != null)
            {
                return cached;
            }
        }

        var index = await _registryClient.FetchRegistryIndexAsync();
        if (index != null)
        {
            _cache.Set(cacheKey, index);
        }

        return index;
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
    public async Task<List<MarketplaceWidgetInfo>> GetAllWidgetsAsync(bool forceRefresh = false)
    {
        var index = await GetRegistryIndexAsync(forceRefresh);
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

            // Check if installed
            if (installedWidgets.TryGetValue(registryWidget.Id, out var installedVersion))
            {
                widgetInfo.InstalledVersion = installedVersion;
                widgetInfo.Status = WidgetStatus.Installed;

                // Check if update available
                if (!string.IsNullOrEmpty(installedVersion) &&
                    CompareVersions(registryWidget.LatestVersion, installedVersion) > 0)
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
    public async Task<List<MarketplaceWidgetInfo>> SearchWidgetsAsync(
        string query,
        bool forceRefresh = false)
    {
        var allWidgets = await GetAllWidgetsAsync(forceRefresh);

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
    /// Gets installed widgets from config
    /// </summary>
    private Dictionary<string, string> GetInstalledWidgets()
    {
        var installed = new Dictionary<string, string>();

        try
        {
            if (!File.Exists(_configPath))
            {
                return installed;
            }

            var configManager = new ConfigManager();
            var config = configManager.LoadConfig(_configPath);

            foreach (var (widgetId, widgetConfig) in config.Widgets)
            {
                // Try to extract version from config or filename
                // For now, we'll just mark as installed without version info
                installed[widgetId] = "installed";
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

    /// <summary>
    /// Clears cache (for F5 refresh)
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }
}
