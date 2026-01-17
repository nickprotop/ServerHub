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
    /// Terminal width breakpoints for responsive layout
    /// </summary>
    [YamlMember(Alias = "breakpoints")]
    public BreakpointConfig? Breakpoints { get; set; }
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
    /// Widget priority for responsive layout
    /// 1 = Critical (always visible)
    /// 2 = Normal (default)
    /// 3 = Low (hidden on narrow terminals)
    /// </summary>
    [YamlMember(Alias = "priority")]
    public int Priority { get; set; } = 2;

    /// <summary>
    /// Minimum height for this widget (in rows)
    /// </summary>
    [YamlMember(Alias = "min_height")]
    public int? MinHeight { get; set; }

    /// <summary>
    /// Maximum height for this widget (in rows)
    /// </summary>
    [YamlMember(Alias = "max_height")]
    public int? MaxHeight { get; set; }

    /// <summary>
    /// Whether this widget should take the full row width
    /// </summary>
    [YamlMember(Alias = "full_row")]
    public bool FullRow { get; set; } = false;

    /// <summary>
    /// Number of columns this widget should span (overrides full_row)
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

    /// <summary>
    /// Height of this row (in terminal rows)
    /// </summary>
    [YamlMember(Alias = "height")]
    public int? Height { get; set; }
}

/// <summary>
/// Terminal width breakpoints for responsive layout
/// </summary>
public class BreakpointConfig
{
    /// <summary>
    /// Minimum width for 1 column layout (default: 0)
    /// </summary>
    [YamlMember(Alias = "single")]
    public int Single { get; set; } = 0;

    /// <summary>
    /// Minimum width for 2 column layout (default: 100)
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
