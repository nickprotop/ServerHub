// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Models;

namespace ServerHub.Utils;

/// <summary>
/// Manages widget search paths with priority ordering
/// Supports custom path, user widgets, and bundled widgets
/// </summary>
public static class WidgetPaths
{
    private static string? _customWidgetsPath;

    /// <summary>
    /// Sets a custom widgets path (highest priority)
    /// Used for development and testing with --widgets-path argument
    /// </summary>
    /// <param name="path">Custom path to search for widgets</param>
    public static void SetCustomWidgetsPath(string? path)
    {
        _customWidgetsPath = path;
    }

    /// <summary>
    /// Gets all widget search paths in priority order
    /// 1. Custom path (if set via --widgets-path)
    /// 2. User custom widgets (~/.config/serverhub/widgets/)
    /// 3. Bundled widgets (~/.local/share/serverhub/widgets/)
    /// </summary>
    /// <returns>Enumerable of search paths</returns>
    public static IEnumerable<string> GetSearchPaths()
    {
        // 0. Custom path from --widgets-path (highest priority)
        if (!string.IsNullOrEmpty(_customWidgetsPath) && Directory.Exists(_customWidgetsPath))
        {
            yield return _customWidgetsPath;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 1. User custom widgets
        var userWidgetsPath = Path.Combine(home, ".config", "serverhub", "widgets");
        if (Directory.Exists(userWidgetsPath))
        {
            yield return userWidgetsPath;
        }

        // 2. Bundled widgets (installed with application)
        var bundledWidgetsPath = Path.Combine(home, ".local", "share", "serverhub", "widgets");
        if (Directory.Exists(bundledWidgetsPath))
        {
            yield return bundledWidgetsPath;
        }
    }

    /// <summary>
    /// Gets only bundled widget search paths
    /// </summary>
    /// <returns>Enumerable of bundled search paths</returns>
    private static IEnumerable<string> GetBundledSearchPaths()
    {
        var bundledPath = GetBundledWidgetsDirectory();
        if (Directory.Exists(bundledPath))
        {
            yield return bundledPath;
        }
    }

    /// <summary>
    /// Gets only custom widget search paths (custom path + user widgets)
    /// </summary>
    /// <returns>Enumerable of custom search paths</returns>
    private static IEnumerable<string> GetCustomSearchPaths()
    {
        // Custom path from --widgets-path (highest priority)
        if (!string.IsNullOrEmpty(_customWidgetsPath) && Directory.Exists(_customWidgetsPath))
        {
            yield return _customWidgetsPath;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userWidgetsPath = Path.Combine(home, ".config", "serverhub", "widgets");
        if (Directory.Exists(userWidgetsPath))
        {
            yield return userWidgetsPath;
        }
    }

    /// <summary>
    /// Resolves a widget path by searching all search paths in priority order
    /// </summary>
    /// <param name="relativePath">Relative path to the widget script</param>
    /// <returns>Full path to the widget script, or null if not found</returns>
    public static string? ResolveWidgetPath(string relativePath)
    {
        foreach (var searchPath in GetSearchPaths())
        {
            var fullPath = Path.Combine(searchPath, relativePath);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a widget path with optional location constraint
    /// </summary>
    /// <param name="relativePath">Relative path to the widget script</param>
    /// <param name="location">Optional location constraint (bundled/custom/auto)</param>
    /// <returns>Full path to the widget script, or null if not found</returns>
    public static string? ResolveWidgetPath(string relativePath, WidgetLocation? location)
    {
        var searchPaths = location switch
        {
            WidgetLocation.Bundled => GetBundledSearchPaths(),
            WidgetLocation.Custom => GetCustomSearchPaths(),
            _ => GetSearchPaths() // Auto/null = all paths
        };

        foreach (var searchPath in searchPaths)
        {
            var fullPath = Path.Combine(searchPath, relativePath);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves widget path and returns the actual location where it was found.
    /// For Auto/null location, determines whether widget was found in custom or bundled directory.
    /// </summary>
    /// <param name="relativePath">Relative path to the widget</param>
    /// <param name="configuredLocation">Location constraint from configuration (null = Auto)</param>
    /// <returns>Tuple of (resolved path, actual location). Path is null if not found.</returns>
    public static (string? path, WidgetLocation? actualLocation) ResolveWidgetPathWithLocation(
        string relativePath,
        WidgetLocation? configuredLocation)
    {
        // If explicit location specified, honor it and return that location
        if (configuredLocation == WidgetLocation.Bundled)
        {
            var path = ResolveWidgetPath(relativePath, WidgetLocation.Bundled);
            return (path, path != null ? WidgetLocation.Bundled : null);
        }

        if (configuredLocation == WidgetLocation.Custom)
        {
            var path = ResolveWidgetPath(relativePath, WidgetLocation.Custom);
            return (path, path != null ? WidgetLocation.Custom : null);
        }

        // Auto location: determine actual location based on where widget is found
        var bundledPath = GetBundledWidgetsDirectory();
        var customPaths = GetCustomSearchPaths().ToList();

        // Search custom paths first (higher priority)
        foreach (var searchPath in customPaths)
        {
            var fullPath = Path.Combine(searchPath, relativePath);
            if (File.Exists(fullPath))
            {
                return (fullPath, WidgetLocation.Custom);
            }
        }

        // Search bundled path last (lower priority)
        var bundledFullPath = Path.Combine(bundledPath, relativePath);
        if (File.Exists(bundledFullPath))
        {
            return (bundledFullPath, WidgetLocation.Bundled);
        }

        // Not found
        return (null, null);
    }

    /// <summary>
    /// Gets the user configuration directory
    /// </summary>
    /// <returns>Path to ~/.config/serverhub/</returns>
    public static string GetUserConfigDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "serverhub");
    }

    /// <summary>
    /// Gets the user widgets directory
    /// </summary>
    /// <returns>Path to ~/.config/serverhub/widgets/</returns>
    public static string GetUserWidgetsDirectory()
    {
        return Path.Combine(GetUserConfigDirectory(), "widgets");
    }

    /// <summary>
    /// Gets the user custom widgets directory (~/.config/serverhub/widgets)
    /// Alias for GetUserWidgetsDirectory() for semantic clarity in init-config scenarios
    /// </summary>
    /// <returns>Path to ~/.config/serverhub/widgets/</returns>
    public static string GetUserCustomWidgetsDirectory()
    {
        return GetUserWidgetsDirectory();
    }

    /// <summary>
    /// Gets the bundled widgets directory
    /// </summary>
    /// <returns>Path to ~/.local/share/serverhub/widgets/</returns>
    public static string GetBundledWidgetsDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "serverhub", "widgets");
    }

    /// <summary>
    /// Ensures required directories exist
    /// </summary>
    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(GetUserConfigDirectory());
        Directory.CreateDirectory(GetUserWidgetsDirectory());
        Directory.CreateDirectory(GetBundledWidgetsDirectory());
    }
}
