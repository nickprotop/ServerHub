// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace ServerHub.Models;

/// <summary>
/// Represents a command in the command palette
/// </summary>
public class PaletteCommand
{
    /// <summary>
    /// Unique identifier for this command
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display label for the command
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Description/subtitle for the command
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Type of command
    /// </summary>
    public CommandType Type { get; set; }

    /// <summary>
    /// Optional icon/emoji
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Source widget ID (for widget actions and navigation)
    /// </summary>
    public string? WidgetId { get; set; }

    /// <summary>
    /// Widget action object (for widget actions)
    /// </summary>
    public WidgetAction? Action { get; set; }

    /// <summary>
    /// Execution callback
    /// </summary>
    public Action? Execute { get; set; }

    /// <summary>
    /// Priority for sorting (higher = appears first)
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Last time this command was used (for recent tracking)
    /// </summary>
    public DateTime? LastUsed { get; set; }
}

/// <summary>
/// Type of command in the palette
/// </summary>
public enum CommandType
{
    /// <summary>
    /// System/dashboard command (refresh, configure, etc.)
    /// </summary>
    System,

    /// <summary>
    /// Navigate to a widget
    /// </summary>
    Navigation,

    /// <summary>
    /// Execute a widget action
    /// </summary>
    WidgetAction,

    /// <summary>
    /// Recently used command
    /// </summary>
    Recent
}
