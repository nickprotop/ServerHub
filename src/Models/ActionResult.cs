// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace ServerHub.Models;

/// <summary>
/// Result of executing a widget action
/// </summary>
public class ActionResult
{
    /// <summary>
    /// Exit code from the command execution
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// Standard output from the command
    /// </summary>
    public string Stdout { get; set; } = "";

    /// <summary>
    /// Standard error from the command
    /// </summary>
    public string Stderr { get; set; } = "";

    /// <summary>
    /// Duration of the execution
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Whether the command executed successfully (exit code 0)
    /// </summary>
    public bool IsSuccess => ExitCode == 0;

    /// <summary>
    /// Whether there is any stdout
    /// </summary>
    public bool HasOutput => !string.IsNullOrEmpty(Stdout);

    /// <summary>
    /// Whether there is any stderr
    /// </summary>
    public bool HasErrors => !string.IsNullOrEmpty(Stderr);
}
