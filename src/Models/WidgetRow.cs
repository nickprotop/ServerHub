// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace ServerHub.Models;

/// <summary>
/// Represents a single row of data in a widget
/// Contains raw markup and parsed inline elements (status indicators, progress bars)
/// </summary>
public class WidgetRow
{
    /// <summary>
    /// Raw text content with markup (Spectre.Console markup syntax)
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Parsed status indicator (if present)
    /// Format: [status:STATE] where STATE is ok|info|warn|error
    /// </summary>
    public WidgetStatus? Status { get; set; }

    /// <summary>
    /// Parsed progress bar (if present)
    /// Format: [progress:NN] or [progress:NN:style]
    /// </summary>
    public WidgetProgress? Progress { get; set; }

    /// <summary>
    /// Parsed sparkline (if present)
    /// Format: [sparkline:VALUES] or [sparkline:VALUES:COLOR]
    /// </summary>
    public WidgetSparkline? Sparkline { get; set; }

    /// <summary>
    /// Parsed mini progress bar (if present)
    /// Format: [miniprogress:VALUE] or [miniprogress:VALUE:WIDTH]
    /// </summary>
    public WidgetMiniProgress? MiniProgress { get; set; }

    /// <summary>
    /// Parsed table (if present)
    /// Format: [table:HEADERS] followed by [tablerow:VALUES]
    /// </summary>
    public WidgetTable? Table { get; set; }

    /// <summary>
    /// Parsed divider (if present)
    /// Format: [divider] or [divider:CHAR] or [divider:CHAR:COLOR]
    /// </summary>
    public WidgetDivider? Divider { get; set; }

    /// <summary>
    /// Parsed multi-line graph (if present)
    /// Format: [graph:VALUES] or [graph:VALUES:COLOR] or [graph:VALUES:COLOR:LABEL]
    /// </summary>
    public WidgetGraph? Graph { get; set; }
}

/// <summary>
/// Status indicator for a widget row
/// </summary>
public class WidgetStatus
{
    public StatusState State { get; set; }
}

public enum StatusState
{
    Ok,      // Green indicator
    Info,    // Blue/Cyan indicator
    Warn,    // Yellow indicator
    Error    // Red indicator
}

/// <summary>
/// Progress bar for a widget row
/// </summary>
public class WidgetProgress
{
    /// <summary>
    /// Progress value (0-100)
    /// </summary>
    public int Value { get; set; }

    /// <summary>
    /// Display style (inline blocks, chart, etc.)
    /// </summary>
    public ProgressStyle Style { get; set; } = ProgressStyle.Inline;

    /// <summary>
    /// Optional gradient name or custom gradient (e.g., "cool", "warm", "blue→red")
    /// </summary>
    public string? Gradient { get; set; }
}

public enum ProgressStyle
{
    Inline,  // Unicode blocks (default)
    Chart,   // Spectre BarChart
}

/// <summary>
/// Braille sparkline for inline trend visualization
/// </summary>
public class WidgetSparkline
{
    public List<double> Values { get; set; } = new();
    public string? Color { get; set; }
    public int Width { get; set; } = 30;
}

/// <summary>
/// Compact inline progress bar
/// </summary>
public class WidgetMiniProgress
{
    public int Value { get; set; }  // 0-100
    public int Width { get; set; } = 10;

    /// <summary>
    /// Optional gradient name or custom gradient (e.g., "cool", "warm", "blue→red")
    /// </summary>
    public string? Gradient { get; set; }
}

/// <summary>
/// Multi-column table layout
/// </summary>
public class WidgetTable
{
    public List<string> Headers { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
}

/// <summary>
/// Horizontal divider line
/// </summary>
public class WidgetDivider
{
    public string Character { get; set; } = "─";
    public string? Color { get; set; }
}

/// <summary>
/// Multi-line braille chart
/// </summary>
public class WidgetGraph
{
    public List<double> Values { get; set; } = new();
    public string? Color { get; set; }
    public string? Label { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public int Width { get; set; } = 30;
}
