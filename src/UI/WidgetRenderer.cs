// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using Spectre.Console;
using ServerHub.Models;

namespace ServerHub.UI;

/// <summary>
/// Renders widget data into ConsoleEx UI controls
/// Creates markup controls with headers, status indicators, and progress bars
/// </summary>
public class WidgetRenderer
{
    /// <summary>
    /// Creates a markup control for a widget
    /// </summary>
    /// <param name="widgetId">Widget identifier</param>
    /// <param name="widgetData">Widget data to render</param>
    /// <param name="isPinned">Whether this is a pinned widget (compact tile)</param>
    /// <param name="backgroundColor">Background color for the widget</param>
    /// <param name="onClickCallback">Optional callback when widget is clicked</param>
    /// <param name="onDoubleClickCallback">Optional callback when widget is double-clicked</param>
    /// <param name="maxLines">Maximum lines to display (null = no limit)</param>
    /// <param name="showTruncationIndicator">Whether to show indicator when truncated</param>
    /// <returns>Control to display the widget</returns>
    public IWindowControl CreateWidgetPanel(string widgetId, WidgetData widgetData, bool isPinned, Color? backgroundColor = null, Action<string>? onClickCallback = null, Action<string>? onDoubleClickCallback = null, int? maxLines = null, bool showTruncationIndicator = true)
    {
        var lines = BuildWidgetContent(widgetData, isPinned, maxLines, showTruncationIndicator);

        var bgColor = backgroundColor ?? Color.Grey15;

        var builder = Controls.Markup()
            .WithName($"widget_{widgetId}")
            .WithBackgroundColor(bgColor)
            .WithMargin(1, 0, 1, 0);

        foreach (var line in lines)
        {
            builder.AddLine(line);
        }

        var markupControl = builder.Build();

        // Wire up click and double-click callbacks if provided
        if (markupControl is SharpConsoleUI.Controls.IMouseAwareControl mouseAware)
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

        return markupControl;
    }

    /// <summary>
    /// Builds the content lines for a widget
    /// </summary>
    private List<string> BuildWidgetContent(WidgetData widgetData, bool isPinned, int? maxLines = null, bool showTruncationIndicator = true)
    {
        var lines = new List<string>();

        if (isPinned)
        {
            // Pinned widget: single line with title and first row (no truncation)
            var content = widgetData.Rows.Count > 0
                ? $"[bold cyan1]{widgetData.Title}[/] {FormatRow(widgetData.Rows[0])}"
                : $"[bold cyan1]{widgetData.Title}[/]";
            lines.Add(content);
        }
        else
        {
            // Regular widget: title header + all rows
            lines.Add($"[bold cyan1]{widgetData.Title}[/]");
            lines.Add(""); // Empty line for spacing

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

            // Apply truncation if maxLines specified
            bool wasTruncated = false;
            if (maxLines.HasValue)
            {
                // Split lines containing \n (from progress bars) into separate elements
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

                // Now check if we need to truncate based on actual line count
                if (expandedLines.Count > maxLines.Value)
                {
                    wasTruncated = true;
                    // Truncate to maxLines
                    lines = expandedLines.Take(maxLines.Value).ToList();
                }
                else
                {
                    // No truncation needed, but use expanded lines for consistency
                    lines = expandedLines;
                }
            }

            // Always add blank + info line at the bottom
            lines.Add("");
            var infoLine = $"[grey70]Updated: {widgetData.Timestamp:HH:mm:ss}[/]";
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

        // Add progress bar if present
        if (row.Progress != null)
        {
            var progressBar = CreateInlineProgressBar(row.Progress.Value);
            content = $"{content}\n{progressBar}";
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
    /// Updates an existing widget control with new data
    /// </summary>
    public void UpdateWidgetPanel(IWindowControl control, WidgetData widgetData, int? maxLines = null, bool showTruncationIndicator = true)
    {
        if (control is MarkupControl markup)
        {
            // Determine if pinned based on control name
            var isPinned = control.Name?.Contains("_pinned") ?? false;
            var lines = BuildWidgetContent(widgetData, isPinned, maxLines, showTruncationIndicator);

            markup.SetContent(lines);
        }
    }
}
