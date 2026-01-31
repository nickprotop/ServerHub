// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;
using ServerHub.Models;
using ServerHub.Utils;

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
/// - [status:STATE] - Status indicator (ok/info/warn/error)
/// - [progress:NN] or [progress:NN:style] - Progress bar (0-100)
/// </summary>
public class WidgetProtocolParser
{
    private static readonly Regex StatusRegex = new(@"\[status:(ok|info|warn|error)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Updated to support gradient parameter: [progress:VALUE:GRADIENT:STYLE] or [progress:VALUE:GRADIENT] or [progress:VALUE:STYLE]
    private static readonly Regex ProgressRegex = new(@"\[progress:(\d+)(?::([^:\]]+))?(?::(inline|chart))?\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Updated to support gradient and width: [sparkline:VALUES:COLOR/GRADIENT:WIDTH]
    private static readonly Regex SparklineRegex = new(
        @"\[sparkline:([\d\.,\s]+)(?::([^\]:]+))?(?::(\d+))?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Updated to support gradient parameter: [miniprogress:VALUE:WIDTH:GRADIENT]
    private static readonly Regex MiniProgressRegex = new(
        @"\[miniprogress:(\d+)(?::(\d+))?(?::([^\]]+))?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TableHeaderRegex = new(
        @"\[table:(.+)\]$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TableRowRegex = new(
        @"\[tablerow:(.+)\]$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DividerRegex = new(
        @"\[divider(?::([^:]+))?(?::(\w+))?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Updated to support gradient with arrow character: [graph:VALUES:COLOR/GRADIENT:LABEL:MIN-MAX:WIDTH]
    private static readonly Regex GraphRegex = new(
        @"\[graph:([\d\.,\s]+)(?::([^\]:]+))?(?::([^\]:]+))?(?::([^\]:]+))?(?::(\d+))?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses widget script output into structured WidgetData
    /// </summary>
    /// <param name="output">Raw script output</param>
    /// <returns>Parsed widget data</returns>
    public WidgetData Parse(string output)
    {
        var data = new WidgetData();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        WidgetTable? currentTable = null;
        var tableStartIndex = -1;

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
                data.Title = ContentSanitizer.Sanitize(trimmedLine.Substring(6).Trim());
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
                // Finalize table if we hit a row directive
                if (currentTable != null)
                {
                    data.Rows.Insert(tableStartIndex, new WidgetRow { Table = currentTable });
                    currentTable = null;
                    tableStartIndex = -1;
                }

                var rowContent = trimmedLine.Substring(4).Trim();
                var row = ParseRow(rowContent);
                data.Rows.Add(row);
            }
            else if (trimmedLine.StartsWith("action:", StringComparison.OrdinalIgnoreCase))
            {
                // Finalize table if we hit an action directive
                if (currentTable != null)
                {
                    data.Rows.Insert(tableStartIndex, new WidgetRow { Table = currentTable });
                    currentTable = null;
                    tableStartIndex = -1;
                }

                var actionContent = trimmedLine.Substring(7).Trim();
                var action = ParseAction(actionContent);
                if (action != null)
                {
                    data.Actions.Add(action);
                }
            }
            else
            {
                // Check for table header
                var tableHeaderMatch = TableHeaderRegex.Match(trimmedLine);
                if (tableHeaderMatch.Success)
                {
                    // Finalize any existing table first
                    if (currentTable != null)
                    {
                        data.Rows.Insert(tableStartIndex, new WidgetRow { Table = currentTable });
                    }

                    currentTable = new WidgetTable
                    {
                        Headers = tableHeaderMatch.Groups[1].Value.Split('|').Select(h => h.Trim()).ToList()
                    };
                    tableStartIndex = data.Rows.Count;
                    continue;
                }

                // Check for table row
                var tableRowMatch = TableRowRegex.Match(trimmedLine);
                if (tableRowMatch.Success && currentTable != null)
                {
                    var rowValues = tableRowMatch.Groups[1].Value.Split('|')
                        .Select(v => ProcessCellContent(v.Trim()))
                        .ToList();
                    currentTable.Rows.Add(rowValues);
                    continue;
                }

                // Finalize table if we hit a non-table line
                if (currentTable != null)
                {
                    data.Rows.Insert(tableStartIndex, new WidgetRow { Table = currentTable });
                    currentTable = null;
                    tableStartIndex = -1;
                }
            }
        }

        // Finalize any remaining table
        if (currentTable != null && tableStartIndex >= 0)
        {
            data.Rows.Insert(tableStartIndex, new WidgetRow { Table = currentTable });
        }

        return data;
    }

    /// <summary>
    /// Parses comma-separated data points into a list of doubles
    /// </summary>
    private static List<double> ParseDataPoints(string values)
    {
        return values.Split(',')
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => double.TryParse(v, out var val) ? val : 0.0)
            .ToList();
    }

    /// <summary>
    /// Processes inline directives in table cell content and replaces them with rendered markup.
    /// Also sanitizes content to strip ANSI codes and escape invalid brackets.
    /// </summary>
    private static string ProcessCellContent(string cellContent)
    {
        var content = cellContent;

        // Process sparkline directives (before sanitization to preserve tag format)
        content = SparklineRegex.Replace(content, match =>
        {
            var values = ParseDataPoints(match.Groups[1].Value);
            var color = match.Groups[2].Success ? match.Groups[2].Value : "grey70";
            var width = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 30;
            return InlineElementRenderer.RenderSparkline(values, color, width);
        });

        // Process mini progress directives
        content = MiniProgressRegex.Replace(content, match =>
        {
            var value = int.Parse(match.Groups[1].Value);
            var width = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 10;
            var gradient = match.Groups[3].Success ? match.Groups[3].Value : null;
            return InlineElementRenderer.RenderMiniProgress(value, width, gradient);
        });

        // Sanitize remaining content (strip ANSI, escape invalid brackets)
        // This protects against system data containing brackets like [kworker/0:1]
        content = ContentSanitizer.Sanitize(content);

        return content;
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
                "info" => StatusState.Info,
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

            // Group 2 can be either gradient or style - detect by content
            string? gradient = null;
            var style = ProgressStyle.Inline;

            if (progressMatch.Groups[2].Success)
            {
                var secondParam = progressMatch.Groups[2].Value;
                // If it's "inline" or "chart", it's a style; otherwise it's a gradient
                if (secondParam.Equals("inline", StringComparison.OrdinalIgnoreCase))
                    style = ProgressStyle.Inline;
                else if (secondParam.Equals("chart", StringComparison.OrdinalIgnoreCase))
                    style = ProgressStyle.Chart;
                else
                    gradient = secondParam;
            }

            // Group 3 is always style if present
            if (progressMatch.Groups[3].Success)
            {
                style = progressMatch.Groups[3].Value.ToLowerInvariant() switch
                {
                    "chart" => ProgressStyle.Chart,
                    "inline" => ProgressStyle.Inline,
                    _ => ProgressStyle.Inline
                };
            }

            row.Progress = new WidgetProgress
            {
                Value = Math.Clamp(value, 0, 100),
                Style = style,
                Gradient = gradient
            };

            // Remove the progress tag from display content
            row.Content = ProgressRegex.Replace(row.Content, "").Trim();
        }

        // Parse sparkline
        var sparklineMatch = SparklineRegex.Match(row.Content);
        if (sparklineMatch.Success)
        {
            row.Sparkline = new WidgetSparkline
            {
                Values = ParseDataPoints(sparklineMatch.Groups[1].Value),
                Color = sparklineMatch.Groups[2].Success ? sparklineMatch.Groups[2].Value : null,
                Width = sparklineMatch.Groups[3].Success ? int.Parse(sparklineMatch.Groups[3].Value) : 30
            };
            row.Content = SparklineRegex.Replace(row.Content, "");
        }

        // Parse mini progress
        var miniProgressMatch = MiniProgressRegex.Match(row.Content);
        if (miniProgressMatch.Success)
        {
            var value = int.Parse(miniProgressMatch.Groups[1].Value);
            var width = miniProgressMatch.Groups[2].Success ? int.Parse(miniProgressMatch.Groups[2].Value) : 10;
            var gradient = miniProgressMatch.Groups[3].Success ? miniProgressMatch.Groups[3].Value : null;
            row.MiniProgress = new WidgetMiniProgress
            {
                Value = Math.Clamp(value, 0, 100),
                Width = Math.Clamp(width, 3, 20),
                Gradient = gradient
            };
            row.Content = MiniProgressRegex.Replace(row.Content, "");
        }

        // Parse divider
        var dividerMatch = DividerRegex.Match(row.Content);
        if (dividerMatch.Success)
        {
            row.Divider = new WidgetDivider
            {
                Character = dividerMatch.Groups[1].Success ? dividerMatch.Groups[1].Value : "─",
                Color = dividerMatch.Groups[2].Success ? dividerMatch.Groups[2].Value : null
            };
            row.Content = DividerRegex.Replace(row.Content, "");
        }

        // Parse graph
        var graphMatch = GraphRegex.Match(row.Content);
        if (graphMatch.Success)
        {
            // Parse optional min-max range (format: "0-100" or "0,100")
            double? minValue = null;
            double? maxValue = null;
            if (graphMatch.Groups[4].Success)
            {
                var rangeStr = graphMatch.Groups[4].Value;
                var parts = rangeStr.Split(new[] { '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    if (double.TryParse(parts[0].Trim(), out var min))
                        minValue = min;
                    if (double.TryParse(parts[1].Trim(), out var max))
                        maxValue = max;
                }
            }

            row.Graph = new WidgetGraph
            {
                Values = ParseDataPoints(graphMatch.Groups[1].Value),
                Color = graphMatch.Groups[2].Success ? graphMatch.Groups[2].Value : null,
                Label = graphMatch.Groups[3].Success ? graphMatch.Groups[3].Value : null,
                MinValue = minValue,
                MaxValue = maxValue,
                Width = graphMatch.Groups[5].Success ? int.Parse(graphMatch.Groups[5].Value) : 30
            };
            row.Content = GraphRegex.Replace(row.Content, "");
        }

        // Sanitize final content (strip ANSI codes, escape invalid brackets)
        // This protects against system data containing brackets like [kworker/0:1]
        row.Content = ContentSanitizer.Sanitize(row.Content);

        return row;
    }

    /// <summary>
    /// Parses an action definition
    /// Format: [flags] Label:command
    /// Flags: danger, refresh, sudo, timeout=N (comma-separated)
    /// Example: [danger,refresh] Restart all:docker restart $(docker ps -q)
    /// Example: [sudo,timeout=120] Long task:./slow-script.sh
    /// Example: [timeout=0] Infinite task:tail -f /var/log/syslog
    /// </summary>
    private WidgetAction? ParseAction(string content)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int? timeout = null;
        var workingContent = content.Trim();

        // Parse flags: [flag1,flag2,timeout=N]
        var flagMatch = Regex.Match(workingContent, @"^\[([^\]]+)\]\s*");
        if (flagMatch.Success)
        {
            var flagString = flagMatch.Groups[1].Value;
            foreach (var flag in flagString.Split(','))
            {
                var trimmedFlag = flag.Trim();
                if (string.IsNullOrEmpty(trimmedFlag))
                {
                    continue;
                }

                // Check for timeout=N pattern
                var timeoutMatch = Regex.Match(trimmedFlag, @"^timeout=(\d+)$", RegexOptions.IgnoreCase);
                if (timeoutMatch.Success)
                {
                    if (int.TryParse(timeoutMatch.Groups[1].Value, out var timeoutValue))
                    {
                        timeout = timeoutValue;
                    }
                    // Don't add timeout=N to flags - it's a value, not a boolean flag
                }
                else
                {
                    flags.Add(trimmedFlag);
                }
            }
            workingContent = workingContent.Substring(flagMatch.Length).Trim();
        }

        // Parse label:command
        var colonIndex = workingContent.IndexOf(':');
        if (colonIndex < 0)
        {
            return null;
        }

        var label = workingContent.Substring(0, colonIndex).Trim();
        var command = workingContent.Substring(colonIndex + 1).Trim();

        if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(command))
        {
            return null;
        }

        return new WidgetAction
        {
            Label = label,
            Command = command,
            Flags = flags,
            Timeout = timeout
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
