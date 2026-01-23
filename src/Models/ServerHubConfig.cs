// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using YamlDotNet.Serialization;

namespace ServerHub.Models;

/// <summary>
/// Root configuration for ServerHub
/// Loaded from YAML configuration file
/// </summary>
public class ServerHubConfig
{
    /// <summary>
    /// Collection of configured widgets
    /// Key: Widget ID (user-defined name)
    /// Value: Widget configuration
    /// </summary>
    [YamlMember(Alias = "widgets")]
    public Dictionary<string, WidgetConfig> Widgets { get; set; } = new();

    /// <summary>
    /// Layout configuration (responsive or explicit grid)
    /// </summary>
    [YamlMember(Alias = "layout")]
    public LayoutConfig? Layout { get; set; }

    /// <summary>
    /// Global refresh interval (default for widgets without explicit refresh)
    /// </summary>
    [YamlMember(Alias = "default_refresh")]
    public int DefaultRefresh { get; set; } = 5;

    /// <summary>
    /// Global maximum lines per widget (default: 20)
    /// Individual widgets can override with max_lines
    /// </summary>
    [YamlMember(Alias = "max_lines_per_widget")]
    public int MaxLinesPerWidget { get; set; } = 20;

    /// <summary>
    /// Whether to show truncation indicator when content is clipped
    /// </summary>
    [YamlMember(Alias = "show_truncation_indicator")]
    public bool ShowTruncationIndicator { get; set; } = true;

    /// <summary>
    /// Terminal width breakpoints for responsive layout
    /// </summary>
    [YamlMember(Alias = "breakpoints")]
    public BreakpointConfig? Breakpoints { get; set; }
}

/// <summary>
/// Specifies where to search for a widget
/// </summary>
public enum WidgetLocation
{
    /// <summary>
    /// Auto-resolve using priority order: custom → user → bundled (default)
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Search only bundled widgets directory (~/.local/share/serverhub/widgets)
    /// </summary>
    Bundled = 1,

    /// <summary>
    /// Search only custom widgets directories (--widgets-path and ~/.config/serverhub/widgets)
    /// </summary>
    Custom = 2
}

/// <summary>
/// Configuration for a single widget
/// </summary>
public class WidgetConfig
{
    /// <summary>
    /// Path to the widget script (relative to widget search paths)
    /// </summary>
    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "";

    /// <summary>
    /// Optional location constraint for widget path resolution
    /// Controls where to search for the widget script
    /// </summary>
    [YamlMember(Alias = "location", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public WidgetLocation? Location { get; set; }

    /// <summary>
    /// Optional SHA256 checksum for security validation
    /// </summary>
    [YamlMember(Alias = "sha256")]
    public string? Sha256 { get; set; }

    /// <summary>
    /// Refresh interval in seconds
    /// </summary>
    [YamlMember(Alias = "refresh")]
    public int Refresh { get; set; } = 5;

    /// <summary>
    /// Whether this widget should be pinned to the top as a tile
    /// </summary>
    [YamlMember(Alias = "pinned")]
    public bool Pinned { get; set; } = false;

    /// <summary>
    /// Maximum lines for this specific widget (overrides global max_lines_per_widget)
    /// </summary>
    [YamlMember(Alias = "max_lines")]
    public int? MaxLines { get; set; }

    /// <summary>
    /// Number of columns this widget should span
    /// </summary>
    [YamlMember(Alias = "column_span")]
    public int? ColumnSpan { get; set; }
}

/// <summary>
/// Layout configuration supporting both simple list and explicit grid layouts
/// </summary>
public class LayoutConfig
{
    /// <summary>
    /// Simple list layout (auto-layout with responsive columns)
    /// Widgets are placed in order, flowing into columns based on terminal width
    /// </summary>
    [YamlMember(Alias = "order")]
    public List<string>? Order { get; set; }

    /// <summary>
    /// Explicit grid layout (advanced, backward compatibility)
    /// Allows precise control over widget placement
    /// </summary>
    [YamlMember(Alias = "rows")]
    public List<LayoutRow>? Rows { get; set; }
}

/// <summary>
/// Explicit row layout (for advanced grid layouts)
/// </summary>
public class LayoutRow
{
    /// <summary>
    /// Widgets in this row (left to right)
    /// </summary>
    [YamlMember(Alias = "widgets")]
    public List<string> Widgets { get; set; } = new();
}

/// <summary>
/// Terminal width breakpoints for responsive layout
/// </summary>
public class BreakpointConfig
{
    /// <summary>
    /// Minimum width for 2 column layout (default: 100)
    /// Below this threshold: 1 column (only priority 1 widgets shown)
    /// </summary>
    [YamlMember(Alias = "double")]
    public int Double { get; set; } = 100;

    /// <summary>
    /// Minimum width for 3 column layout (default: 160)
    /// </summary>
    [YamlMember(Alias = "triple")]
    public int Triple { get; set; } = 160;

    /// <summary>
    /// Minimum width for 4 column layout (default: 220)
    /// </summary>
    [YamlMember(Alias = "quad")]
    public int Quad { get; set; } = 220;
}
