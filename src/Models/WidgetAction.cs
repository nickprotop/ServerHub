// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace ServerHub.Models;

/// <summary>
/// Represents an interactive action that a widget can expose
/// Actions are triggered when a user selects a row and presses Enter
/// </summary>
public class WidgetAction
{
    /// <summary>
    /// Display label for the action (shown in action menu)
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Path to the script that implements this action
    /// </summary>
    public string ScriptPath { get; set; } = "";

    /// <summary>
    /// Arguments to pass to the script
    /// </summary>
    public string Arguments { get; set; } = "";

    /// <summary>
    /// Whether this action should be validated for security
    /// </summary>
    public bool RequiresValidation { get; set; } = true;
}
