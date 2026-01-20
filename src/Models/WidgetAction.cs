// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace ServerHub.Models;

/// <summary>
/// Represents an interactive action that a widget can expose
/// Actions are shell commands that can be executed from the widget expansion dialog
/// </summary>
public class WidgetAction
{
    /// <summary>
    /// Display label for the action (shown in action list)
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Shell command to execute
    /// </summary>
    public string Command { get; set; } = "";

    /// <summary>
    /// Action flags (danger, refresh, etc.)
    /// </summary>
    public HashSet<string> Flags { get; set; } = new();

    /// <summary>
    /// Whether this action is marked as dangerous
    /// </summary>
    public bool IsDanger => Flags.Contains("danger", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this action requires sudo/root privileges
    /// </summary>
    public bool RequiresSudo => Flags.Contains("sudo", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether to refresh widget after successful execution
    /// </summary>
    public bool RefreshAfterSuccess => Flags.Contains("refresh", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Custom timeout in seconds (null = not specified, use default)
    /// </summary>
    public int? Timeout { get; set; }

    /// <summary>
    /// Whether this action has no timeout limit (timeout=0)
    /// </summary>
    public bool HasNoTimeout => Timeout == 0;

    /// <summary>
    /// Effective timeout to use (returns Timeout if specified, otherwise default 60 seconds)
    /// </summary>
    public int EffectiveTimeout => Timeout ?? 60;
}
