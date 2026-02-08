// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;
using ServerHub.Models;
using ServerHub.Storage;
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
    private readonly List<string> _validationErrors = new();

    // Storage context for datafetch/history elements (set during Parse call)
    private StorageService? _currentStorageService;
    private string? _currentWidgetId;

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

    // Line graph with optional color, label, min-max range, width, height, and style
    // [line:VALUES:COLOR:LABEL:MIN-MAX:WIDTH:HEIGHT:STYLE]
    // Allow empty fields between colons: [line:1,2,3::::10] should work
    // Allow invalid numbers in values (ParseDataPoints converts them to 0): [line:1,abc,3] should work
    private static readonly Regex LineGraphRegex = new(
        @"\[line:([^\]:]+)(?::([^\]:]*)(?::([^\]:]*)(?::([^\]:]*)(?::(\d*)(?::(\d*)(?::(braille|ascii))?)?)?)?)?)?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Datafetch: [datafetch:KEY:AGGREGATION:TIMERANGE]
    // Examples: [datafetch:cpu_usage.value], [datafetch:cpu_usage.value:avg:1h], [datafetch:cpu_usage.value::last_10]
    private static readonly Regex DatafetchRegex = new(
        @"\[datafetch:([^:\]]+)(?::([^:\]]+))?(?::([^:\]]+))?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // History graph: [history_graph:KEY:TIMERANGE:COLOR:LABEL:MIN-MAX:WIDTH]
    // Examples: [history_graph:cpu_usage.value:1h], [history_graph:cpu_usage.value:24h:cool:CPU:0-100:40]
    private static readonly Regex HistoryGraphRegex = new(
        @"\[history_graph:([^:\]]+):([^:\]]+)(?::([^:\]]+))?(?::([^:\]]+))?(?::([^:\]]+))?(?::(\d+))?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // History sparkline: [history_sparkline:KEY:TIMERANGE:COLOR:WIDTH]
    // Examples: [history_sparkline:cpu_usage.value:30s], [history_sparkline:cpu_usage.value:1h:cool:30]
    private static readonly Regex HistorySparklineRegex = new(
        @"\[history_sparkline:([^:\]]+):([^:\]]+)(?::([^:\]]+))?(?::(\d+))?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // History line: [history_line:KEY:TIMERANGE:COLOR:LABEL:MIN-MAX:WIDTH:HEIGHT:STYLE]
    // Examples: [history_line:cpu_usage.value:1h:cyan:CPU:0-100:60:8:braille]
    private static readonly Regex HistoryLineRegex = new(
        @"\[history_line:([^:\]]+):([^:\]]+)(?::([^\]:]*))?(?::([^\]:]*))?(?::([^\]:]*))?(?::(\d*))?(?::(\d*))?(?::(braille|ascii))?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Gets the validation errors from the last parse operation
    /// </summary>
    public List<string> GetValidationErrors()
    {
        return new List<string>(_validationErrors);
    }

    /// <summary>
    /// Parses widget script output into structured WidgetData
    /// </summary>
    /// <param name="output">Raw script output</param>
    /// <param name="storageService">Optional storage service for datafetch/history elements</param>
    /// <param name="widgetId">Optional widget ID for scoped storage queries</param>
    /// <returns>Parsed widget data</returns>
    public WidgetData Parse(string output, StorageService? storageService = null, string? widgetId = null)
    {
        _validationErrors.Clear();
        var data = new WidgetData();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Store storage context for use in ParseRow
        _currentStorageService = storageService;
        _currentWidgetId = widgetId;

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
                try
                {
                    data.Title = ContentSanitizer.Sanitize(trimmedLine.Substring(6).Trim());
                }
                catch
                {
                    // Fallback: Use raw title without sanitization (risky but better than crashing)
                    data.Title = trimmedLine.Substring(6).Trim();
                }
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
            else if (trimmedLine.StartsWith("datastore:", StringComparison.OrdinalIgnoreCase))
            {
                // Finalize table if we hit a datastore directive
                if (currentTable != null)
                {
                    data.Rows.Insert(tableStartIndex, new WidgetRow { Table = currentTable });
                    currentTable = null;
                    tableStartIndex = -1;
                }

                var datastoreContent = trimmedLine.Substring(10).Trim();
                var directive = ParseDatastoreDirective(datastoreContent);
                if (directive != null)
                {
                    data.DatastoreDirectives.Add(directive);
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
                        .Select(v => this.ProcessCellContent(v.Trim()))
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
    /// Supports both static elements (sparkline, miniprogress) and storage-based elements (history_sparkline).
    /// </summary>
    private string ProcessCellContent(string cellContent)
    {
        var content = cellContent;

        // Process history_sparkline directives (requires storage)
        content = HistorySparklineRegex.Replace(content, match =>
        {
            var key = match.Groups[1].Value.Trim();
            var timeRange = match.Groups[2].Value.Trim();
            var color = match.Groups[3].Success && !string.IsNullOrWhiteSpace(match.Groups[3].Value)
                ? match.Groups[3].Value.Trim()
                : "grey70";
            var width = match.Groups[4].Success && int.TryParse(match.Groups[4].Value, out var w)
                ? Math.Clamp(w, 5, 200)
                : 15;

            // Fetch time series from storage
            var values = FetchTimeSeries(key, timeRange);

            if (values.Count > 0)
            {
                return InlineElementRenderer.RenderSparkline(values, color, width);
            }
            else
            {
                // No data available - return placeholder
                return "[grey50]--[/]";
            }
        });

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
        try
        {
            content = ContentSanitizer.Sanitize(content);
        }
        catch
        {
            // Fallback: Return unsanitized content (risky but better than losing data)
            // ContentSanitizer has its own fallback, so this should rarely happen
        }

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

        // Parse line graph
        var lineGraphMatch = LineGraphRegex.Match(row.Content);
        if (lineGraphMatch.Success)
        {
            // Parse values (required)
            var values = ParseDataPoints(lineGraphMatch.Groups[1].Value);

            // Skip if no valid values
            if (values.Count == 0)
            {
                row.Content = LineGraphRegex.Replace(row.Content, "");
                return row;
            }

            // Parse color/gradient (group 2)
            string? color = null;
            string? gradient = null;
            if (lineGraphMatch.Groups[2].Success && !string.IsNullOrWhiteSpace(lineGraphMatch.Groups[2].Value))
            {
                var colorOrGradient = lineGraphMatch.Groups[2].Value;
                // Check if it's a gradient (contains arrow or is a preset name like "cool", "warm")
                if (colorOrGradient.Contains("→") ||
                    colorOrGradient.Contains("->") ||
                    colorOrGradient.Equals("cool", StringComparison.OrdinalIgnoreCase) ||
                    colorOrGradient.Equals("warm", StringComparison.OrdinalIgnoreCase) ||
                    colorOrGradient.Equals("rainbow", StringComparison.OrdinalIgnoreCase))
                {
                    gradient = colorOrGradient;
                }
                else
                {
                    color = colorOrGradient;
                }
            }

            // Parse label (group 3)
            var label = lineGraphMatch.Groups[3].Success && !string.IsNullOrWhiteSpace(lineGraphMatch.Groups[3].Value)
                ? lineGraphMatch.Groups[3].Value
                : null;

            // Parse optional min-max range (group 4, format: "0-100" or "0,100")
            double? minValue = null;
            double? maxValue = null;
            if (lineGraphMatch.Groups[4].Success && !string.IsNullOrWhiteSpace(lineGraphMatch.Groups[4].Value))
            {
                var rangeStr = lineGraphMatch.Groups[4].Value;
                var parts = rangeStr.Split(new[] { '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    if (double.TryParse(parts[0].Trim(), out var min))
                        minValue = min;
                    if (double.TryParse(parts[1].Trim(), out var max))
                        maxValue = max;
                }
            }

            // Parse width (group 5)
            var width = lineGraphMatch.Groups[5].Success && !string.IsNullOrWhiteSpace(lineGraphMatch.Groups[5].Value) && int.TryParse(lineGraphMatch.Groups[5].Value, out var w)
                ? Math.Max(20, Math.Min(200, w))  // Clamp to reasonable range
                : 60;

            // Parse height (group 6)
            var height = lineGraphMatch.Groups[6].Success && !string.IsNullOrWhiteSpace(lineGraphMatch.Groups[6].Value) && int.TryParse(lineGraphMatch.Groups[6].Value, out var h)
                ? Math.Max(4, Math.Min(40, h))  // Clamp to reasonable range
                : 8;

            // Parse style (group 7)
            var style = lineGraphMatch.Groups[7].Success && !string.IsNullOrWhiteSpace(lineGraphMatch.Groups[7].Value)
                ? lineGraphMatch.Groups[7].Value.ToLowerInvariant()
                : "braille";

            row.LineGraph = new WidgetLineGraph
            {
                Values = values,
                Color = color,
                Gradient = gradient,
                Label = label,
                MinValue = minValue,
                MaxValue = maxValue,
                Width = width,
                Height = height,
                Style = style
            };
            row.Content = LineGraphRegex.Replace(row.Content, "");
        }

        // Parse datafetch (Phase 2: Retrieval)
        var datafetchMatch = DatafetchRegex.Match(row.Content);
        if (datafetchMatch.Success)
        {
            var key = datafetchMatch.Groups[1].Value.Trim();
            var aggregation = datafetchMatch.Groups[2].Success && !string.IsNullOrWhiteSpace(datafetchMatch.Groups[2].Value)
                ? datafetchMatch.Groups[2].Value.Trim().ToLowerInvariant()
                : "latest";
            var timeRange = datafetchMatch.Groups[3].Success && !string.IsNullOrWhiteSpace(datafetchMatch.Groups[3].Value)
                ? datafetchMatch.Groups[3].Value.Trim()
                : null;

            // Fetch data from storage if available
            var fetchedValue = FetchStorageValue(key, aggregation, timeRange);

            row.Datafetch = new WidgetDatafetch
            {
                Key = key,
                Aggregation = aggregation,
                TimeRange = timeRange,
                Value = fetchedValue
            };
            row.Content = DatafetchRegex.Replace(row.Content, fetchedValue ?? "[grey50]--[/]");
        }

        // Parse history_graph (Phase 3: History Helpers)
        var historyGraphMatch = HistoryGraphRegex.Match(row.Content);
        if (historyGraphMatch.Success)
        {
            var key = historyGraphMatch.Groups[1].Value.Trim();
            var timeRange = historyGraphMatch.Groups[2].Value.Trim();
            var color = historyGraphMatch.Groups[3].Success && !string.IsNullOrWhiteSpace(historyGraphMatch.Groups[3].Value)
                ? historyGraphMatch.Groups[3].Value.Trim()
                : null;
            var label = historyGraphMatch.Groups[4].Success && !string.IsNullOrWhiteSpace(historyGraphMatch.Groups[4].Value)
                ? historyGraphMatch.Groups[4].Value.Trim()
                : null;

            // Parse min-max range
            double? minValue = null;
            double? maxValue = null;
            if (historyGraphMatch.Groups[5].Success && !string.IsNullOrWhiteSpace(historyGraphMatch.Groups[5].Value))
            {
                var rangeStr = historyGraphMatch.Groups[5].Value;
                var parts = rangeStr.Split(new[] { '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    if (double.TryParse(parts[0].Trim(), out var min))
                        minValue = min;
                    if (double.TryParse(parts[1].Trim(), out var max))
                        maxValue = max;
                }
            }

            var width = historyGraphMatch.Groups[6].Success && int.TryParse(historyGraphMatch.Groups[6].Value, out var w)
                ? Math.Clamp(w, 10, 200)
                : 30;

            // Fetch time series from storage
            var values = FetchTimeSeries(key, timeRange);

            row.HistoryGraph = new WidgetHistoryGraph
            {
                Values = values,
                Color = color,
                Label = label,
                MinValue = minValue,
                MaxValue = maxValue,
                Width = width
            };
            row.Content = HistoryGraphRegex.Replace(row.Content, "");
        }

        // Parse history_sparkline
        var historySparklineMatch = HistorySparklineRegex.Match(row.Content);
        if (historySparklineMatch.Success)
        {
            var key = historySparklineMatch.Groups[1].Value.Trim();
            var timeRange = historySparklineMatch.Groups[2].Value.Trim();
            var color = historySparklineMatch.Groups[3].Success && !string.IsNullOrWhiteSpace(historySparklineMatch.Groups[3].Value)
                ? historySparklineMatch.Groups[3].Value.Trim()
                : null;
            var width = historySparklineMatch.Groups[4].Success && int.TryParse(historySparklineMatch.Groups[4].Value, out var w)
                ? Math.Clamp(w, 5, 200)
                : 30;

            // Fetch time series from storage
            var values = FetchTimeSeries(key, timeRange);

            row.HistorySparkline = new WidgetHistorySparkline
            {
                Values = values,
                Color = color,
                Width = width
            };
            row.Content = HistorySparklineRegex.Replace(row.Content, "");
        }

        // Parse history_line
        var historyLineMatch = HistoryLineRegex.Match(row.Content);
        if (historyLineMatch.Success)
        {
            var key = historyLineMatch.Groups[1].Value.Trim();
            var timeRange = historyLineMatch.Groups[2].Value.Trim();

            // Parse color/gradient (group 3)
            string? color = null;
            string? gradient = null;
            if (historyLineMatch.Groups[3].Success && !string.IsNullOrWhiteSpace(historyLineMatch.Groups[3].Value))
            {
                var colorOrGradient = historyLineMatch.Groups[3].Value.Trim();
                if (colorOrGradient.Contains("→") ||
                    colorOrGradient.Contains("->") ||
                    colorOrGradient.Equals("cool", StringComparison.OrdinalIgnoreCase) ||
                    colorOrGradient.Equals("warm", StringComparison.OrdinalIgnoreCase) ||
                    colorOrGradient.Equals("spectrum", StringComparison.OrdinalIgnoreCase) ||
                    colorOrGradient.Equals("grayscale", StringComparison.OrdinalIgnoreCase))
                {
                    gradient = colorOrGradient;
                }
                else
                {
                    color = colorOrGradient;
                }
            }

            var label = historyLineMatch.Groups[4].Success && !string.IsNullOrWhiteSpace(historyLineMatch.Groups[4].Value)
                ? historyLineMatch.Groups[4].Value.Trim()
                : null;

            // Parse min-max range
            double? minValue = null;
            double? maxValue = null;
            if (historyLineMatch.Groups[5].Success && !string.IsNullOrWhiteSpace(historyLineMatch.Groups[5].Value))
            {
                var rangeStr = historyLineMatch.Groups[5].Value;
                var parts = rangeStr.Split(new[] { '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    if (double.TryParse(parts[0].Trim(), out var min))
                        minValue = min;
                    if (double.TryParse(parts[1].Trim(), out var max))
                        maxValue = max;
                }
            }

            var width = historyLineMatch.Groups[6].Success && !string.IsNullOrWhiteSpace(historyLineMatch.Groups[6].Value) && int.TryParse(historyLineMatch.Groups[6].Value, out var w)
                ? Math.Clamp(w, 20, 200)
                : 60;

            var height = historyLineMatch.Groups[7].Success && !string.IsNullOrWhiteSpace(historyLineMatch.Groups[7].Value) && int.TryParse(historyLineMatch.Groups[7].Value, out var h)
                ? Math.Clamp(h, 4, 40)
                : 8;

            var style = historyLineMatch.Groups[8].Success && !string.IsNullOrWhiteSpace(historyLineMatch.Groups[8].Value)
                ? historyLineMatch.Groups[8].Value.ToLowerInvariant()
                : "braille";

            // Fetch time series from storage
            var values = FetchTimeSeries(key, timeRange);

            row.HistoryLineGraph = new WidgetHistoryLineGraph
            {
                Values = values,
                Color = color,
                Gradient = gradient,
                Label = label,
                MinValue = minValue,
                MaxValue = maxValue,
                Width = width,
                Height = height,
                Style = style
            };
            row.Content = HistoryLineRegex.Replace(row.Content, "");
        }

        // Sanitize final content (strip ANSI codes, escape invalid brackets)
        // This protects against system data containing brackets like [kworker/0:1]
        try
        {
            row.Content = ContentSanitizer.Sanitize(row.Content);
        }
        catch
        {
            // Fallback: Keep unsanitized content (risky but better than losing data)
            // ContentSanitizer has its own fallback, so this should rarely happen
        }

        return row;
    }

    /// <summary>
    /// Fetches a single value from storage for datafetch element
    /// </summary>
    private string? FetchStorageValue(string key, string aggregation, string? timeRange)
    {
        if (_currentStorageService == null || _currentWidgetId == null)
        {
            Logger.Debug($"Datafetch skipped: storage={_currentStorageService != null}, widgetId={_currentWidgetId != null}", "Storage");
            return null;
        }

        try
        {
            // Parse key: "measurement.field" or "measurement.field,tag1=val1,tag2=val2"
            // First split on comma to separate query from tags
            var queryAndTags = key.Split(',');
            var query = queryAndTags[0]; // "measurement.field"

            // Parse tags if present
            Dictionary<string, string>? tags = null;
            if (queryAndTags.Length > 1)
            {
                tags = new Dictionary<string, string>();
                for (int i = 1; i < queryAndTags.Length; i++)
                {
                    var tagParts = queryAndTags[i].Split('=');
                    if (tagParts.Length == 2)
                    {
                        tags[tagParts[0].Trim()] = tagParts[1].Trim();
                    }
                }
            }

            // Parse query into measurement and field
            var parts = query.Split('.');
            var measurement = parts[0];
            var fieldName = parts.Length > 1 ? parts[1] : "value";

            var repository = _currentStorageService.GetRepository(_currentWidgetId);

            if (aggregation == "latest")
            {
                var dataPoint = repository.GetLatest(measurement, fieldName, tags);
                if (dataPoint?.FieldValue != null)
                {
                    return FormatValue(dataPoint.FieldValue.Value);
                }
            }
            else if (timeRange != null)
            {
                // Aggregation requires time range
                var aggregatedData = repository.GetAggregated(measurement, fieldName, timeRange, tags);
                if (aggregatedData != null)
                {
                    var value = aggregation switch
                    {
                        "avg" => aggregatedData.Average,
                        "max" => aggregatedData.Max,
                        "min" => aggregatedData.Min,
                        "sum" => aggregatedData.Sum,
                        "count" => (double?)aggregatedData.Count,
                        _ => null
                    };

                    if (value != null)
                    {
                        return FormatValue(value.Value);
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Logger.Debug($"Datafetch error for key '{key}': {ex.Message}", "Storage");
            return null;
        }
    }

    /// <summary>
    /// Fetches time series data from storage for history elements
    /// </summary>
    private List<double> FetchTimeSeries(string key, string timeRange)
    {
        if (_currentStorageService == null || _currentWidgetId == null)
        {
            Logger.Debug($"History fetch skipped: storage={_currentStorageService != null}, widgetId={_currentWidgetId != null}", "Storage");
            return new List<double>();
        }

        try
        {
            // Parse key: "measurement.field" or "measurement.field,tag1=val1,tag2=val2"
            // First split on comma to separate query from tags
            var queryAndTags = key.Split(',');
            var query = queryAndTags[0]; // "measurement.field"

            // Parse tags if present
            Dictionary<string, string>? tags = null;
            if (queryAndTags.Length > 1)
            {
                tags = new Dictionary<string, string>();
                for (int i = 1; i < queryAndTags.Length; i++)
                {
                    var tagParts = queryAndTags[i].Split('=');
                    if (tagParts.Length == 2)
                    {
                        tags[tagParts[0].Trim()] = tagParts[1].Trim();
                    }
                }
            }

            // Parse query into measurement and field
            var parts = query.Split('.');
            var measurement = parts[0];
            var fieldName = parts.Length > 1 ? parts[1] : "value";

            var repository = _currentStorageService.GetRepository(_currentWidgetId);
            var dataPoints = repository.GetTimeSeries(measurement, fieldName, timeRange, tags);

            // Extract numeric values
            var values = dataPoints
                .Where(dp => dp.FieldValue.HasValue)
                .Select(dp => dp.FieldValue!.Value)
                .ToList();

            Logger.Debug($"History fetch for '{key}' ({timeRange}): {values.Count} data points", "Storage");
            return values;
        }
        catch (Exception ex)
        {
            Logger.Debug($"History fetch error for key '{key}': {ex.Message}", "Storage");
            return new List<double>();
        }
    }

    /// <summary>
    /// Formats a numeric value for display
    /// </summary>
    private string FormatValue(double value)
    {
        // Format with 1 decimal place for values < 100, no decimals for >= 100
        if (Math.Abs(value) < 100)
        {
            return value.ToString("0.0");
        }
        return value.ToString("0");
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
    /// Parses a datastore directive following InfluxDB line protocol format
    /// Format: measurement[,tag=val,tag=val] field=val[,field=val] [timestamp]
    ///
    /// Examples:
    /// - cpu_usage value=75.5
    /// - cpu_usage,core=0,host=srv01 value=75.5,temp=65
    /// - disk_io,device=sda reads=1500,writes=2300
    /// - metric,tag=x value=100 1707348000
    /// </summary>
    private DatastoreDirective? ParseDatastoreDirective(string content)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                _validationErrors.Add("Datastore directive is empty");
                return null;
            }

            // Split into parts: [measurement,tags] [fields] [timestamp]
            var parts = content.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                _validationErrors.Add($"Datastore directive missing fields: {content}");
                return null;
            }

            var directive = new DatastoreDirective();

            // Parse measurement and optional tags (part 0)
            var measurementPart = parts[0];
            var measurementComponents = measurementPart.Split(',');

            // First component is always the measurement
            directive.Measurement = measurementComponents[0].Trim();
            if (string.IsNullOrEmpty(directive.Measurement))
            {
                _validationErrors.Add($"Datastore directive has empty measurement: {content}");
                return null;
            }

            // Parse tags (remaining components after measurement)
            for (int i = 1; i < measurementComponents.Length; i++)
            {
                var tag = measurementComponents[i].Trim();
                if (string.IsNullOrEmpty(tag))
                    continue;

                var tagParts = tag.Split('=', 2);
                if (tagParts.Length == 2)
                {
                    var key = tagParts[0].Trim();
                    var value = tagParts[1].Trim();
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                    {
                        directive.Tags[key] = value;
                    }
                }
                else
                {
                    _validationErrors.Add($"Malformed tag in datastore directive: {tag}");
                }
            }

            // Parse fields (part 1)
            var fieldsPart = parts[1];
            var fieldComponents = fieldsPart.Split(',');

            foreach (var field in fieldComponents)
            {
                var fieldTrimmed = field.Trim();
                if (string.IsNullOrEmpty(fieldTrimmed))
                    continue;

                var fieldParts = fieldTrimmed.Split('=', 2);
                if (fieldParts.Length == 2)
                {
                    var key = fieldParts[0].Trim();
                    var valueStr = fieldParts[1].Trim();

                    if (string.IsNullOrEmpty(key))
                    {
                        _validationErrors.Add($"Malformed field in datastore directive: {field}");
                        continue;
                    }

                    // Parse field value (int, float, bool, or string)
                    object value = ParseFieldValue(valueStr);
                    directive.Fields[key] = value;
                }
                else
                {
                    _validationErrors.Add($"Malformed field in datastore directive: {field}");
                }
            }

            // Validate that we have at least one field
            if (directive.Fields.Count == 0)
            {
                _validationErrors.Add($"Datastore directive has no valid fields: {content}");
                return null;
            }

            // Parse optional timestamp (part 2)
            if (parts.Length >= 3)
            {
                if (long.TryParse(parts[2], out var timestamp))
                {
                    directive.Timestamp = timestamp;
                }
                else
                {
                    _validationErrors.Add($"Invalid timestamp in datastore directive: {parts[2]}");
                }
            }

            return directive;
        }
        catch (Exception ex)
        {
            _validationErrors.Add($"Failed to parse datastore directive: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses a field value, detecting type (int, float, bool, or string)
    /// Supports quoted strings, integers, floats, and booleans
    /// </summary>
    private static object ParseFieldValue(string valueStr)
    {
        // Handle quoted strings
        if (valueStr.StartsWith("\"") && valueStr.EndsWith("\"") && valueStr.Length >= 2)
        {
            return valueStr.Substring(1, valueStr.Length - 2);
        }

        // Handle booleans
        if (bool.TryParse(valueStr, out var boolValue))
        {
            return boolValue;
        }

        // Handle integers
        if (long.TryParse(valueStr, out var intValue))
        {
            return intValue;
        }

        // Handle floats
        if (double.TryParse(valueStr, out var floatValue))
        {
            return floatValue;
        }

        // Default to string
        return valueStr;
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
