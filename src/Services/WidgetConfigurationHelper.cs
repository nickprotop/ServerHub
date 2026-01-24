// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using ServerHub.Models;
using ServerHub.Utils;

namespace ServerHub.Services;

/// <summary>
/// Helper class for widget configuration operations including discovery,
/// validation, creation, and configuration updates.
/// Centralizes widget-related logic used by both UI and CLI.
/// </summary>
public static class WidgetConfigurationHelper
{
    // === DISCOVERY METHODS ===

    /// <summary>
    /// Discovers all available widgets from configured paths.
    /// Returns both configured widgets (from config) and available widgets (from filesystem).
    /// Used by UI widget configuration dialog.
    /// </summary>
    public static List<DiscoveredWidget> DiscoverAllWidgets(ServerHubConfig config)
    {
        var discovered = new List<DiscoveredWidget>();

        // Track configured (path, location) pairs to avoid showing them as available
        var configuredPathLocations = new HashSet<(string path, WidgetLocation? location)>(
            new PathLocationComparer()
        );

        // First, add all configured widgets in layout order
        var orderedWidgets = new List<string>();
        if (config.Layout?.Order != null)
        {
            orderedWidgets.AddRange(config.Layout.Order);
        }

        // Add any widgets not in the order list
        foreach (var widgetId in config.Widgets.Keys)
        {
            if (!orderedWidgets.Contains(widgetId))
            {
                orderedWidgets.Add(widgetId);
            }
        }

        // Process configured widgets
        foreach (var widgetId in orderedWidgets)
        {
            if (!config.Widgets.TryGetValue(widgetId, out var widgetConfig))
                continue;

            // Resolve with actual location detection for Auto widgets
            var (fullPath, actualLocation) = WidgetPaths.ResolveWidgetPathWithLocation(
                widgetConfig.Path,
                widgetConfig.Location);

            var status = fullPath != null && File.Exists(fullPath)
                ? WidgetConfigStatus.Configured
                : WidgetConfigStatus.Missing;

            // For Auto location, show where it resolved to in the widget list
            var displayId = widgetId;
            if (widgetConfig.Location == null && actualLocation != null)
            {
                var locationText = FormatLocationDisplay(widgetConfig.Location, actualLocation);
                displayId = $"{widgetId} ({locationText})";
            }

            discovered.Add(new DiscoveredWidget
            {
                DisplayId = displayId,
                RelativePath = widgetConfig.Path,
                FullPath = fullPath,
                Status = status,
                Config = widgetConfig,
                ActualLocation = actualLocation
            });

            // Track using ACTUAL location for Auto widgets (prevents duplicates)
            // If configured location is explicit, use that; otherwise use actual resolved location
            var trackingLocation = widgetConfig.Location ?? actualLocation;
            configuredPathLocations.Add((widgetConfig.Path, trackingLocation));
        }

        // Discover available widgets from bundled directory
        var bundledPath = WidgetPaths.GetBundledWidgetsDirectory();
        if (Directory.Exists(bundledPath))
        {
            foreach (var file in Directory.GetFiles(bundledPath))
            {
                if (!IsExecutable(file)) continue;

                var fileName = Path.GetFileName(file);
                var fullPath = Path.GetFullPath(file);

                // Skip if this path+bundled combination is already configured
                // OR if the path is configured with Auto (null) location
                if (configuredPathLocations.Contains((fileName, WidgetLocation.Bundled)) ||
                    configuredPathLocations.Contains((fileName, null)))
                    continue;

                var widgetId = Path.GetFileNameWithoutExtension(file);

                discovered.Add(new DiscoveredWidget
                {
                    DisplayId = $"{widgetId} (bundled)",
                    RelativePath = fileName,
                    FullPath = fullPath,
                    Status = WidgetConfigStatus.Available,
                    Config = new WidgetConfig
                    {
                        Path = fileName,
                        Location = WidgetLocation.Bundled
                    },
                    ActualLocation = WidgetLocation.Bundled
                });
            }
        }

        // Discover available widgets from custom directories
        foreach (var searchPath in WidgetPaths.GetSearchPaths())
        {
            if (!Directory.Exists(searchPath)) continue;

            // Skip bundled directory (already processed above)
            if (searchPath == bundledPath) continue;

            foreach (var file in Directory.GetFiles(searchPath))
            {
                if (!IsExecutable(file)) continue;

                var fileName = Path.GetFileName(file);
                var fullPath = Path.GetFullPath(file);

                // Skip if this path+custom combination is already configured
                // OR if the path is configured with Auto (null) location
                if (configuredPathLocations.Contains((fileName, WidgetLocation.Custom)) ||
                    configuredPathLocations.Contains((fileName, null)))
                    continue;

                var widgetId = Path.GetFileNameWithoutExtension(file);

                discovered.Add(new DiscoveredWidget
                {
                    DisplayId = $"{widgetId} (custom)",
                    RelativePath = fileName,
                    FullPath = fullPath,
                    Status = WidgetConfigStatus.Available,
                    Config = new WidgetConfig
                    {
                        Path = fileName,
                        Location = WidgetLocation.Custom
                    },
                    ActualLocation = WidgetLocation.Custom
                });
            }
        }

        return discovered;
    }

    /// <summary>
    /// Discovers only unconfigured widgets from specified path.
    /// Used by CLI --discover command.
    /// </summary>
    public static List<DiscoveredWidget> DiscoverUnconfiguredWidgets(
        string searchPath,
        ServerHubConfig? existingConfig = null)
    {
        var discovered = new List<DiscoveredWidget>();

        // Build set of already configured full paths
        var configuredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existingConfig != null)
        {
            foreach (var widget in existingConfig.Widgets.Values)
            {
                var resolved = WidgetPaths.ResolveWidgetPath(widget.Path, widget.Location);
                if (resolved != null)
                {
                    configuredPaths.Add(Path.GetFullPath(resolved));
                }
            }
        }

        // Find all executables in search path
        if (!Directory.Exists(searchPath))
            return discovered;

        foreach (var file in Directory.GetFiles(searchPath))
        {
            if (!IsExecutable(file)) continue;

            var fullPath = Path.GetFullPath(file);
            if (configuredPaths.Contains(fullPath))
                continue;

            var fileName = Path.GetFileName(file);
            var checksum = CalculateChecksum(file);

            discovered.Add(new DiscoveredWidget
            {
                DisplayId = Path.GetFileNameWithoutExtension(file),
                RelativePath = fileName,
                FullPath = fullPath,
                Status = WidgetConfigStatus.Available,
                Config = new WidgetConfig
                {
                    Path = fileName,
                    Sha256 = checksum,
                    Refresh = 5
                },
                ActualLocation = null
            });
        }

        return discovered;
    }

    // === WIDGET CONFIG CREATION ===

    /// <summary>
    /// Creates a new WidgetConfig from a widget file.
    /// Automatically calculates checksum and determines location.
    /// </summary>
    public static WidgetConfig CreateWidgetConfig(
        string relativePath,
        WidgetLocation? location,
        bool includeChecksum = true)
    {
        var config = new WidgetConfig
        {
            Path = relativePath,
            Location = location,
            Refresh = 5
        };

        if (includeChecksum)
        {
            var fullPath = WidgetPaths.ResolveWidgetPath(relativePath, location);
            if (fullPath != null && File.Exists(fullPath))
            {
                config.Sha256 = CalculateChecksum(fullPath);
            }
        }

        return config;
    }

    // === VALIDATION METHODS ===

    /// <summary>
    /// Validates that a widget exists and is executable.
    /// Returns validation result with details.
    /// </summary>
    public static WidgetValidationResult ValidateWidget(
        string widgetId,
        WidgetConfig config)
    {
        var fullPath = WidgetPaths.ResolveWidgetPath(config.Path, config.Location);

        if (fullPath == null || !File.Exists(fullPath))
        {
            return new WidgetValidationResult
            {
                WidgetId = widgetId,
                IsValid = false,
                ErrorMessage = "Widget file not found",
                Status = WidgetConfigStatus.Missing,
                FullPath = fullPath
            };
        }

        if (!IsExecutable(fullPath))
        {
            return new WidgetValidationResult
            {
                WidgetId = widgetId,
                IsValid = false,
                ErrorMessage = "Widget file is not executable",
                Status = WidgetConfigStatus.Missing,
                FullPath = fullPath
            };
        }

        return new WidgetValidationResult
        {
            WidgetId = widgetId,
            IsValid = true,
            Status = WidgetConfigStatus.Configured,
            FullPath = fullPath
        };
    }

    /// <summary>
    /// Checks if a file is executable (cross-platform).
    /// </summary>
    public static bool IsExecutable(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || (file.Attributes & FileAttributes.Directory) != 0)
                return false;

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return (file.UnixFileMode & UnixFileMode.UserExecute) != 0;
            }

            // Windows: check extension
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".sh" or ".bash" or ".exe" or ".cmd" or ".bat";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates all widgets in a configuration.
    /// Returns list of validation results.
    /// </summary>
    public static List<WidgetValidationResult> ValidateAllWidgets(ServerHubConfig config)
    {
        var results = new List<WidgetValidationResult>();

        foreach (var (widgetId, widgetConfig) in config.Widgets)
        {
            results.Add(ValidateWidget(widgetId, widgetConfig));
        }

        return results;
    }

    // === CONFIG UPDATE METHODS ===

    /// <summary>
    /// Adds a widget to configuration.
    /// Optionally adds to layout.order.
    /// </summary>
    public static void AddWidget(
        ServerHubConfig config,
        string widgetId,
        WidgetConfig widgetConfig,
        bool addToLayout = true)
    {
        config.Widgets[widgetId] = widgetConfig;

        if (addToLayout && config.Layout?.Order != null && !config.Layout.Order.Contains(widgetId))
        {
            config.Layout.Order.Add(widgetId);
        }
    }

    /// <summary>
    /// Removes a widget from configuration and layout.
    /// </summary>
    public static void RemoveWidget(
        ServerHubConfig config,
        string widgetId)
    {
        config.Widgets.Remove(widgetId);
        config.Layout?.Order?.Remove(widgetId);
    }

    /// <summary>
    /// Updates checksum for a widget in configuration.
    /// If newChecksum is null, calculates it from the widget file.
    /// </summary>
    public static void UpdateWidgetChecksum(
        WidgetConfig widgetConfig,
        string? newChecksum = null)
    {
        if (newChecksum != null)
        {
            widgetConfig.Sha256 = newChecksum;
        }
        else
        {
            var fullPath = WidgetPaths.ResolveWidgetPath(widgetConfig.Path, widgetConfig.Location);
            if (fullPath != null && File.Exists(fullPath))
            {
                widgetConfig.Sha256 = CalculateChecksum(fullPath);
            }
        }
    }

    // === CHECKSUM HELPERS ===

    /// <summary>
    /// Calculates SHA256 checksum for a widget file.
    /// Convenience wrapper around ScriptValidator.CalculateChecksum.
    /// </summary>
    public static string CalculateChecksum(string path)
        => ScriptValidator.CalculateChecksum(path);

    // === DISPLAY HELPERS ===

    /// <summary>
    /// Formats location for display (e.g., "auto: bundled", "auto: custom").
    /// </summary>
    public static string FormatLocationDisplay(
        WidgetLocation? configuredLocation,
        WidgetLocation? actualLocation)
    {
        if (configuredLocation == null && actualLocation != null)
        {
            return actualLocation switch
            {
                WidgetLocation.Bundled => "auto: bundled",
                WidgetLocation.Custom => "auto: custom",
                _ => "auto"
            };
        }

        return string.Empty;
    }

    // === PRIVATE HELPERS ===

    /// <summary>
    /// Custom comparer for (path, location) tuples
    /// </summary>
    private class PathLocationComparer : IEqualityComparer<(string path, WidgetLocation? location)>
    {
        public bool Equals((string path, WidgetLocation? location) x, (string path, WidgetLocation? location) y)
        {
            return string.Equals(x.path, y.path, StringComparison.OrdinalIgnoreCase)
                && x.location == y.location;
        }

        public int GetHashCode((string path, WidgetLocation? location) obj)
        {
            return HashCode.Combine(
                obj.path.ToLowerInvariant(),
                obj.location
            );
        }
    }
}
