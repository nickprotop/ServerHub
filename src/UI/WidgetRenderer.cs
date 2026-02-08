// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using Spectre.Console;
using ServerHub.Models;
using ServerHub.Utils;

namespace ServerHub.UI;

/// <summary>
/// Renders widget data into ConsoleEx UI controls
/// Creates markup controls with headers, status indicators, and progress bars
/// </summary>
public class WidgetRenderer
{
    /// <summary>
    /// Creates a panel control for a widget with rounded borders
    /// </summary>
    /// <param name="widgetId">Widget identifier</param>
    /// <param name="widgetData">Widget data to render</param>
    /// <param name="isPinned">Whether this is a pinned widget (compact tile)</param>
    /// <param name="backgroundColor">Background color for the widget</param>
    /// <param name="borderColor">Border color for the widget panel</param>
    /// <param name="onClickCallback">Optional callback when widget is clicked</param>
    /// <param name="onDoubleClickCallback">Optional callback when widget is double-clicked</param>
    /// <param name="maxLines">Maximum lines to display (null = no limit)</param>
    /// <param name="showTruncationIndicator">Whether to show indicator when truncated</param>
    /// <returns>Control to display the widget</returns>
    public IWindowControl CreateWidgetPanel(string widgetId, WidgetData widgetData, bool isPinned, Color? backgroundColor = null, Color? borderColor = null, Action<string>? onClickCallback = null, Action<string>? onDoubleClickCallback = null, int? maxLines = null, bool showTruncationIndicator = true)
    {
        string content;
        string title;

        try
        {
            var lines = BuildWidgetContent(widgetData, isPinned, maxLines, showTruncationIndicator);
            content = string.Join("\n", lines);
            title = widgetData.Title;
        }
        catch (Exception ex)
        {
            // Fallback: Show error in widget
            content = string.Join("\n", new[]
            {
                "",
                "[red]⚠ Widget Error[/]",
                "",
                $"[grey50]{ex.GetType().Name}[/]",
                $"[grey50]{ex.Message}[/]"
            });
            title = widgetData.Title ?? "Error";
        }

        var bgColor = backgroundColor ?? Color.Grey11;
        var borderCol = borderColor ?? Color.Grey35;

        // Use PanelControl with rounded borders for btop-style aesthetic
        var panelControl = PanelControl.Create()
            .WithContent(content)
            .Rounded()
            .WithBorderColor(borderCol)
            .WithHeader(title)
            .HeaderLeft()  // Left-aligned title
            .WithPadding(1, 0, 1, 0)
            .WithBackgroundColor(bgColor)
            .WithName($"widget_{widgetId}")
            .StretchHorizontal()
            .FillVertical()
            .WithMargin(0, 0, 0, 0)  // No margins - panels touch each other
            .Build();

        // Wire up click and double-click callbacks if provided
        if (panelControl is SharpConsoleUI.Controls.IMouseAwareControl mouseAware)
        {
            if (onClickCallback != null)
            {
                mouseAware.MouseClick += (sender, e) => onClickCallback(widgetId);
            }

            if (onDoubleClickCallback != null)
            {
                mouseAware.MouseDoubleClick += (sender, e) => onDoubleClickCallback(widgetId);
            }
        }

        return panelControl;
    }

    /// <summary>
    /// Builds the content lines for a widget
    /// </summary>
    private List<string> BuildWidgetContent(WidgetData widgetData, bool isPinned, int? maxLines = null, bool showTruncationIndicator = true)
    {
        var lines = new List<string>();

        if (isPinned)
        {
            // Pinned widget: just show first row (title is in panel header now)
            if (widgetData.Rows.Count > 0)
            {
                lines.Add(FormatRow(widgetData.Rows[0]));
            }
        }
        else
        {
            // Regular widget: all rows (title is in panel header now)
            if (widgetData.HasError)
            {
                lines.Add($"[red]Error:[/] {widgetData.Error}");
            }
            else
            {
                foreach (var row in widgetData.Rows)
                {
                    try
                    {
                        lines.Add(FormatRow(row));
                    }
                    catch (Exception ex)
                    {
                        // If a row fails, show error and continue with other rows
                        lines.Add($"[red]⚠ Row Error:[/] [grey50]{ex.Message}[/]");
                    }
                }
            }

            // Always expand lines containing \n (from graphs, progress bars, etc.)
            var expandedLines = new List<string>();
            foreach (var line in lines)
            {
                if (line.Contains('\n'))
                {
                    expandedLines.AddRange(line.Split('\n'));
                }
                else
                {
                    expandedLines.Add(line);
                }
            }

            // Apply truncation if maxLines specified
            if (maxLines.HasValue)
            {
                // Check if we need to truncate based on actual line count
                if (expandedLines.Count > maxLines.Value)
                {
                    // Truncate to maxLines
                    lines = expandedLines.Take(maxLines.Value).ToList();
                }
                else
                {
                    // No truncation needed
                    lines = expandedLines;
                }
            }
            else
            {
                // No max lines - use all expanded lines
                lines = expandedLines;
            }

            // Always add blank + info line at the bottom
            lines.Add("");
            var infoLine = $"[grey70]Updated: {widgetData.Timestamp:HH:mm:ss}[/]";

            // Show action count if available
            if (widgetData.HasActions)
            {
                var actionCount = widgetData.Actions.Count;
                var actionText = actionCount == 1 ? "action" : "actions";
                infoLine += $"  [grey70]•[/]  [cyan1]{actionCount} {actionText}[/]";
            }

            lines.Add(infoLine);
        }

        return lines;
    }

    /// <summary>
    /// Formats a single widget row with status indicators and progress bars
    /// </summary>
    private string FormatRow(WidgetRow row)
    {
        var content = row.Content;

        // Handle divider (replaces entire row)
        if (row.Divider != null)
        {
            return CreateDivider(row.Divider);
        }

        // Handle table (replaces entire row)
        if (row.Table != null)
        {
            return CreateTable(row.Table);
        }

        // Add status indicator
        if (row.Status != null)
        {
            var statusIndicator = row.Status.State switch
            {
                StatusState.Ok => "[green]●[/]",
                StatusState.Info => "[cyan1]●[/]",
                StatusState.Warn => "[yellow]●[/]",
                StatusState.Error => "[red]●[/]",
                _ => "[grey]●[/]"
            };
            content = $"{statusIndicator} {content}";
        }

        // Add sparkline inline
        if (row.Sparkline != null)
        {
            var sparkline = CreateSparkline(row.Sparkline);
            content = $"{content} {sparkline}";
        }

        // Add mini progress inline
        if (row.MiniProgress != null)
        {
            var miniBar = CreateMiniProgressBar(row.MiniProgress);
            content = $"{content} {miniBar}";
        }

        // Add progress bar on new line (existing)
        if (row.Progress != null)
        {
            var progressBar = CreateInlineProgressBar(row.Progress.Value, row.Progress.Gradient);
            content = $"{content}\n{progressBar}";
        }

        // Add graph on new lines
        if (row.Graph != null)
        {
            var graph = CreateGraph(row.Graph);
            content = $"{content}\n{graph}";
        }

        // Add line graph on new lines
        if (row.LineGraph != null)
        {
            var lineGraph = CreateLineGraph(row.LineGraph);
            content = $"{content}\n{lineGraph}";
        }

        // Add history graph on new lines
        if (row.HistoryGraph != null)
        {
            var historyGraph = CreateHistoryGraph(row.HistoryGraph);
            content = $"{content}\n{historyGraph}";
        }

        // Add history sparkline inline
        if (row.HistorySparkline != null)
        {
            var historySparkline = CreateHistorySparkline(row.HistorySparkline);
            content = $"{content} {historySparkline}";
        }

        // Add history line graph on new lines
        if (row.HistoryLineGraph != null)
        {
            var historyLineGraph = CreateHistoryLineGraph(row.HistoryLineGraph);
            content = $"{content}\n{historyLineGraph}";
        }

        return content;
    }

    /// <summary>
    /// Creates an inline progress bar using Unicode blocks
    /// This is just RenderMiniProgress with width=30 and prepended newline
    /// </summary>
    private string CreateInlineProgressBar(int percentage, string? gradient = null)
    {
        // Progress is just miniprogress with width=30 and leading spacing
        var bar = InlineElementRenderer.RenderMiniProgress(percentage, 30, gradient);
        return $"  {bar}";
    }

    /// <summary>
    /// Creates a braille sparkline from data points
    /// </summary>
    private string CreateSparkline(WidgetSparkline sparkline)
    {
        return InlineElementRenderer.RenderSparkline(sparkline.Values, sparkline.Color, sparkline.Width);
    }

    /// <summary>
    /// Creates a compact inline progress bar
    /// </summary>
    private string CreateMiniProgressBar(WidgetMiniProgress miniProgress)
    {
        return InlineElementRenderer.RenderMiniProgress(miniProgress.Value, miniProgress.Width, miniProgress.Gradient);
    }

    /// <summary>
    /// Creates a horizontal divider line
    /// </summary>
    private string CreateDivider(WidgetDivider divider)
    {
        const int dividerWidth = 60;
        var line = new string(divider.Character[0], dividerWidth);
        var color = divider.Color ?? "grey70";
        return $"[{color}]{line}[/]";
    }

    /// <summary>
    /// Creates a multi-column table layout with support for multi-line cells.
    /// Cells containing newlines are rendered across multiple output lines,
    /// with proper alignment and padding.
    /// </summary>
    private string CreateTable(WidgetTable table)
    {
        if (table.Headers.Count == 0)
            return "";

        var columnCount = table.Headers.Count;

        // Step 1: Split all cells into lines and calculate column widths
        // For multi-line cells, we need to check each line's width
        var columnWidths = new int[columnCount];

        // Initialize with header widths
        for (int i = 0; i < columnCount; i++)
        {
            columnWidths[i] = table.Headers[i].Length;
        }

        // Pre-split all cells into lines for efficiency
        var splitRows = new List<string[][]>();
        foreach (var row in table.Rows)
        {
            var splitRow = new string[columnCount][];
            for (int col = 0; col < columnCount; col++)
            {
                var cellValue = col < row.Count ? row[col] : "";
                var cellLines = cellValue.Split('\n');
                splitRow[col] = cellLines;

                // Update column width based on each line
                foreach (var line in cellLines)
                {
                    var plainText = Markup.Remove(line);
                    columnWidths[col] = Math.Max(columnWidths[col], plainText.Length);
                }
            }
            splitRows.Add(splitRow);
        }

        // Add padding to column widths
        for (int i = 0; i < columnCount; i++)
        {
            columnWidths[i] += 2;
        }

        // Step 2: Build output lines
        var outputLines = new List<string>();

        // Header row
        var headerRow = new StringBuilder();
        for (int i = 0; i < columnCount; i++)
        {
            headerRow.Append($"[bold cyan1]{table.Headers[i].PadRight(columnWidths[i])}[/]");
        }
        outputLines.Add(headerRow.ToString().TrimEnd());

        // Separator
        var separator = new StringBuilder();
        for (int i = 0; i < columnCount; i++)
        {
            separator.Append(new string('─', columnWidths[i]));
        }
        outputLines.Add($"[grey70]{separator}[/]");

        // Step 3: Render each data row (potentially multiple output lines)
        for (int rowIdx = 0; rowIdx < splitRows.Count; rowIdx++)
        {
            var splitRow = splitRows[rowIdx];

            // Calculate row height (max lines across all cells)
            int rowHeight = 1;
            for (int col = 0; col < columnCount; col++)
            {
                rowHeight = Math.Max(rowHeight, splitRow[col].Length);
            }

            // Render each line of this row
            for (int lineIdx = 0; lineIdx < rowHeight; lineIdx++)
            {
                var lineBuilder = new StringBuilder();
                for (int col = 0; col < columnCount; col++)
                {
                    var cellLines = splitRow[col];
                    string cellLine;

                    if (lineIdx < cellLines.Length)
                    {
                        // Cell has content for this line
                        cellLine = cellLines[lineIdx];
                    }
                    else
                    {
                        // Cell is shorter - use empty padding
                        cellLine = "";
                    }

                    var plainText = Markup.Remove(cellLine);
                    var padding = columnWidths[col] - plainText.Length;

                    lineBuilder.Append(cellLine);
                    if (padding > 0)
                    {
                        lineBuilder.Append(new string(' ', padding));
                    }
                }
                outputLines.Add(lineBuilder.ToString().TrimEnd());
            }
        }

        return string.Join("\n", outputLines);
    }

    /// <summary>
    /// Creates a multi-line braille chart (vertical bar chart)
    /// </summary>
    private string CreateGraph(WidgetGraph graph)
    {
        return InlineElementRenderer.RenderGraph(graph.Values, 4, graph.Color, graph.Label, true, graph.MinValue, graph.MaxValue, graph.Width);
    }

    /// <summary>
    /// Creates a line graph with smooth connected lines
    /// </summary>
    private string CreateLineGraph(WidgetLineGraph lineGraph)
    {
        return InlineElementRenderer.RenderLineGraph(
            lineGraph.Values,
            lineGraph.Width,
            lineGraph.Height,
            lineGraph.Style,
            lineGraph.Color,
            lineGraph.Gradient,
            lineGraph.Label,
            lineGraph.MinValue,
            lineGraph.MaxValue);
    }

    /// <summary>
    /// Creates a history graph from stored time series data
    /// </summary>
    private string CreateHistoryGraph(WidgetHistoryGraph historyGraph)
    {
        if (historyGraph.Values.Count == 0)
        {
            return "[grey50]No data[/]";
        }

        return InlineElementRenderer.RenderGraph(
            historyGraph.Values,
            4, // height
            historyGraph.Color,
            historyGraph.Label,
            true, // showBackground
            historyGraph.MinValue,
            historyGraph.MaxValue,
            historyGraph.Width);
    }

    /// <summary>
    /// Creates a history sparkline from stored time series data
    /// </summary>
    private string CreateHistorySparkline(WidgetHistorySparkline historySparkline)
    {
        if (historySparkline.Values.Count == 0)
        {
            return "[grey50]--[/]";
        }

        return InlineElementRenderer.RenderSparkline(
            historySparkline.Values,
            historySparkline.Color,
            historySparkline.Width);
    }

    /// <summary>
    /// Creates a history line graph from stored time series data
    /// </summary>
    private string CreateHistoryLineGraph(WidgetHistoryLineGraph historyLineGraph)
    {
        if (historyLineGraph.Values.Count == 0)
        {
            return "[grey50]No data[/]";
        }

        return InlineElementRenderer.RenderLineGraph(
            historyLineGraph.Values,
            historyLineGraph.Width,
            historyLineGraph.Height,
            historyLineGraph.Style,
            historyLineGraph.Color,
            historyLineGraph.Gradient,
            historyLineGraph.Label,
            historyLineGraph.MinValue,
            historyLineGraph.MaxValue);
    }

    /// <summary>
    /// Updates an existing widget control with new data
    /// </summary>
    public void UpdateWidgetPanel(IWindowControl control, WidgetData widgetData, int? maxLines = null, bool showTruncationIndicator = true)
    {
        if (control is PanelControl panel)
        {
            try
            {
                // Determine if pinned based on control name
                var isPinned = control.Name?.Contains("_pinned") ?? false;
                var lines = BuildWidgetContent(widgetData, isPinned, maxLines, showTruncationIndicator);
                var content = string.Join("\n", lines);

                panel.SetContent(content);
                panel.Header = widgetData.Title;
            }
            catch (Exception ex)
            {
                // Fallback: Show error in panel
                try
                {
                    var errorContent = string.Join("\n", new[]
                    {
                        "",
                        "[red]⚠ Update Error[/]",
                        "",
                        $"[grey50]{ex.GetType().Name}[/]",
                        $"[grey50]{ex.Message}[/]"
                    });
                    panel.SetContent(errorContent);
                }
                catch
                {
                    // Ultimate fallback: plain text
                    panel.SetContent("Update error. See logs.");
                }
            }
        }
    }
}
