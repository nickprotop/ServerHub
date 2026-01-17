// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;
using ServerHub.Models;

namespace ServerHub.Services;

/// <summary>
/// Parses widget script output according to the ServerHub protocol
///
/// Protocol:
/// - title: Widget title
/// - refresh: Refresh interval in seconds
/// - row: Display row with optional inline elements
/// - action: Interactive action (Label:script-path arguments)
///
/// Inline elements in rows:
/// - [status:STATE] - Status indicator (ok/warn/error)
/// - [progress:NN] or [progress:NN:style] - Progress bar (0-100)
/// </summary>
public class WidgetProtocolParser
{
    private static readonly Regex StatusRegex = new(@"\[status:(ok|warn|error)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ProgressRegex = new(@"\[progress:(\d+)(?::(inline|chart))?\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses widget script output into structured WidgetData
    /// </summary>
    /// <param name="output">Raw script output</param>
    /// <returns>Parsed widget data</returns>
    public WidgetData Parse(string output)
    {
        var data = new WidgetData();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine))
            {
                continue;
            }

            // Parse protocol elements
            if (trimmedLine.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
            {
                data.Title = trimmedLine.Substring(6).Trim();
            }
            else if (trimmedLine.StartsWith("refresh:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(trimmedLine.Substring(8).Trim(), out var refresh))
                {
                    data.RefreshInterval = refresh;
                }
            }
            else if (trimmedLine.StartsWith("row:", StringComparison.OrdinalIgnoreCase))
            {
                var rowContent = trimmedLine.Substring(4).Trim();
                var row = ParseRow(rowContent);
                data.Rows.Add(row);
            }
            else if (trimmedLine.StartsWith("action:", StringComparison.OrdinalIgnoreCase))
            {
                var actionContent = trimmedLine.Substring(7).Trim();
                var action = ParseAction(actionContent);
                if (action != null)
                {
                    data.Actions.Add(action);
                }
            }
        }

        return data;
    }

    /// <summary>
    /// Parses a row with inline elements (status, progress)
    /// </summary>
    private WidgetRow ParseRow(string content)
    {
        var row = new WidgetRow { Content = content };

        // Parse status indicator
        var statusMatch = StatusRegex.Match(content);
        if (statusMatch.Success)
        {
            var state = statusMatch.Groups[1].Value.ToLowerInvariant() switch
            {
                "ok" => StatusState.Ok,
                "warn" => StatusState.Warn,
                "error" => StatusState.Error,
                _ => StatusState.Ok
            };

            row.Status = new WidgetStatus { State = state };

            // Remove the status tag from display content
            row.Content = StatusRegex.Replace(row.Content, "").Trim();
        }

        // Parse progress bar
        var progressMatch = ProgressRegex.Match(content);
        if (progressMatch.Success)
        {
            var value = int.Parse(progressMatch.Groups[1].Value);
            var style = progressMatch.Groups[2].Success
                ? progressMatch.Groups[2].Value.ToLowerInvariant() switch
                {
                    "chart" => ProgressStyle.Chart,
                    "inline" => ProgressStyle.Inline,
                    _ => ProgressStyle.Inline
                }
                : ProgressStyle.Inline;

            row.Progress = new WidgetProgress
            {
                Value = Math.Clamp(value, 0, 100),
                Style = style
            };

            // Remove the progress tag from display content
            row.Content = ProgressRegex.Replace(row.Content, "").Trim();
        }

        return row;
    }

    /// <summary>
    /// Parses an action definition
    /// Format: Label:script-path arguments
    /// </summary>
    private WidgetAction? ParseAction(string content)
    {
        var colonIndex = content.IndexOf(':');
        if (colonIndex < 0)
        {
            return null;
        }

        var label = content.Substring(0, colonIndex).Trim();
        var scriptPart = content.Substring(colonIndex + 1).Trim();

        // Split script path and arguments
        var parts = scriptPart.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var scriptPath = parts.Length > 0 ? parts[0] : "";
        var arguments = parts.Length > 1 ? parts[1] : "";

        if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(scriptPath))
        {
            return null;
        }

        return new WidgetAction
        {
            Label = label,
            ScriptPath = scriptPath,
            Arguments = arguments
        };
    }

    /// <summary>
    /// Creates an error widget data for display
    /// </summary>
    public static WidgetData CreateErrorWidget(string title, string errorMessage)
    {
        return new WidgetData
        {
            Title = title,
            Error = errorMessage,
            Rows = new List<WidgetRow>
            {
                new() { Content = $"[red]Error:[/] {errorMessage}" }
            }
        };
    }
}
