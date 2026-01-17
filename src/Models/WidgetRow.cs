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
    /// Format: [status:STATE] where STATE is ok|warn|error
    /// </summary>
    public WidgetStatus? Status { get; set; }

    /// <summary>
    /// Parsed progress bar (if present)
    /// Format: [progress:NN] or [progress:NN:style]
    /// </summary>
    public WidgetProgress? Progress { get; set; }
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
}

public enum ProgressStyle
{
    Inline,  // Unicode blocks (default)
    Chart,   // Spectre BarChart
}
