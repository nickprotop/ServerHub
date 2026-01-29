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
        var lines = BuildWidgetContent(widgetData, isPinned, maxLines, showTruncationIndicator);
        var content = string.Join("\n", lines);

        var bgColor = backgroundColor ?? Color.Grey11;
        var borderCol = borderColor ?? Color.Grey35;

        // Use PanelControl with rounded borders for btop-style aesthetic
        var panelControl = PanelControl.Create()
            .WithContent(content)
            .Rounded()
            .WithBorderColor(borderCol)
            .WithHeader(widgetData.Title)
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
                    lines.Add(FormatRow(row));
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
            bool wasTruncated = false;
            if (maxLines.HasValue)
            {
                // Check if we need to truncate based on actual line count
                if (expandedLines.Count > maxLines.Value)
                {
                    wasTruncated = true;
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

            if (wasTruncated && showTruncationIndicator)
            {
                infoLine += "  [grey70]•[/]  [cyan1]⏎ Press Enter[/] [grey70]or[/] [cyan1]Double-Click[/] [grey70]to expand[/]";
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
            var progressBar = CreateInlineProgressBar(row.Progress.Value);
            content = $"{content}\n{progressBar}";
        }

        // Add graph on new lines
        if (row.Graph != null)
        {
            var graph = CreateGraph(row.Graph);
            content = $"{content}\n{graph}";
        }

        return content;
    }

    /// <summary>
    /// Creates an inline progress bar using Unicode blocks
    /// Color changes based on threshold: green (<70%), yellow (70-89%), red (>=90%)
    /// </summary>
    private string CreateInlineProgressBar(int percentage)
    {
        const int barWidth = 30;
        var filledWidth = (int)(barWidth * percentage / 100.0);
        var emptyWidth = barWidth - filledWidth;

        var filled = new string('█', filledWidth);
        var empty = new string('░', emptyWidth);

        // Dynamic color based on threshold
        var color = percentage switch
        {
            >= 90 => "red",
            >= 70 => "yellow",
            _ => "green"
        };

        return $"  [{color}]{filled}[/][grey35]{empty}[/] [grey70]{percentage}%[/]";
    }

    /// <summary>
    /// Creates a braille sparkline from data points
    /// </summary>
    private string CreateSparkline(WidgetSparkline sparkline)
    {
        return InlineElementRenderer.RenderSparkline(sparkline.Values, sparkline.Color);
    }

    /// <summary>
    /// Creates a compact inline progress bar
    /// </summary>
    private string CreateMiniProgressBar(WidgetMiniProgress miniProgress)
    {
        return InlineElementRenderer.RenderMiniProgress(miniProgress.Value, miniProgress.Width);
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
    /// Creates a multi-column table layout
    /// </summary>
    private string CreateTable(WidgetTable table)
    {
        if (table.Headers.Count == 0)
            return "";

        var columnWidths = new int[table.Headers.Count];
        for (int i = 0; i < table.Headers.Count; i++)
        {
            columnWidths[i] = table.Headers[i].Length;
            foreach (var row in table.Rows)
            {
                if (i < row.Count)
                {
                    var plainText = Markup.Remove(row[i]);
                    columnWidths[i] = Math.Max(columnWidths[i], plainText.Length);
                }
            }
            columnWidths[i] += 2;
        }

        var lines = new List<string>();
        var headerRow = new StringBuilder();
        for (int i = 0; i < table.Headers.Count; i++)
        {
            headerRow.Append($"[bold cyan1]{table.Headers[i].PadRight(columnWidths[i])}[/]");
        }
        lines.Add(headerRow.ToString().TrimEnd());

        var separator = new StringBuilder();
        for (int i = 0; i < table.Headers.Count; i++)
        {
            separator.Append(new string('─', columnWidths[i]));
        }
        lines.Add($"[grey70]{separator}[/]");

        foreach (var row in table.Rows)
        {
            var rowBuilder = new StringBuilder();
            for (int i = 0; i < table.Headers.Count; i++)
            {
                var cellValue = i < row.Count ? row[i] : "";
                var plainText = Markup.Remove(cellValue);
                var padding = columnWidths[i] - plainText.Length;
                rowBuilder.Append(cellValue);
                if (padding > 0)
                    rowBuilder.Append(new string(' ', padding));
            }
            lines.Add(rowBuilder.ToString().TrimEnd());
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Creates a multi-line braille chart (vertical bar chart)
    /// </summary>
    private string CreateGraph(WidgetGraph graph)
    {
        return InlineElementRenderer.RenderGraph(graph.Values, 4, graph.Color, graph.Label);
    }

    /// <summary>
    /// Updates an existing widget control with new data
    /// </summary>
    public void UpdateWidgetPanel(IWindowControl control, WidgetData widgetData, int? maxLines = null, bool showTruncationIndicator = true)
    {
        if (control is PanelControl panel)
        {
            // Determine if pinned based on control name
            var isPinned = control.Name?.Contains("_pinned") ?? false;
            var lines = BuildWidgetContent(widgetData, isPinned, maxLines, showTruncationIndicator);
            var content = string.Join("\n", lines);

            panel.SetContent(content);
            panel.Header = widgetData.Title;
        }
    }
}
