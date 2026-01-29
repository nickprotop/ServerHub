// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;

namespace ServerHub.Utils;

/// <summary>
/// Shared rendering utilities for inline widget elements (sparklines, progress bars, graphs).
/// Used by both WidgetProtocolParser (table cells) and WidgetRenderer (row content).
/// </summary>
public static class InlineElementRenderer
{
    /// <summary>
    /// Bottom-to-top braille characters for vertical bar representation (8 levels)
    /// </summary>
    public static readonly char[] BrailleLevels = { '⠀', '⣀', '⣄', '⣤', '⣦', '⣶', '⣷', '⣿' };

    /// <summary>
    /// Renders a sparkline as markup text
    /// </summary>
    /// <param name="values">Data points to visualize</param>
    /// <param name="color">Spectre.Console color name (default: grey70)</param>
    /// <returns>Markup string with braille sparkline</returns>
    public static string RenderSparkline(IReadOnlyList<double> values, string? color = null)
    {
        if (values.Count == 0)
            return "";

        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        var effectiveColor = color ?? "grey70";

        if (range == 0)
            return $"[{effectiveColor}]{new string('⣤', values.Count)}[/]"; // Middle height for flat data

        var result = new StringBuilder();
        foreach (var value in values)
        {
            var normalized = (value - min) / range;
            var level = (int)(normalized * (BrailleLevels.Length - 1));
            result.Append(BrailleLevels[Math.Clamp(level, 0, BrailleLevels.Length - 1)]);
        }

        return $"[{effectiveColor}]{result}[/]";
    }

    /// <summary>
    /// Renders a mini progress bar as markup text
    /// </summary>
    /// <param name="percentage">Progress value 0-100</param>
    /// <param name="width">Character width of the bar (default: 10)</param>
    /// <returns>Markup string with progress bar</returns>
    public static string RenderMiniProgress(int percentage, int width = 10)
    {
        percentage = Math.Clamp(percentage, 0, 100);
        width = Math.Clamp(width, 3, 20);

        var filledWidth = (int)(width * percentage / 100.0);
        var emptyWidth = width - filledWidth;

        var filled = new string('█', filledWidth);
        var empty = new string('░', emptyWidth);

        var color = percentage switch
        {
            >= 90 => "red",
            >= 70 => "yellow",
            _ => "green"
        };

        return $"[{color}]{filled}[/][grey35]{empty}[/] {percentage}%";
    }

    /// <summary>
    /// Renders a multi-line braille graph (vertical bar chart)
    /// </summary>
    /// <param name="values">Data points to visualize</param>
    /// <param name="height">Number of rows (default: 4)</param>
    /// <param name="color">Spectre.Console color name (default: cyan1)</param>
    /// <param name="label">Optional label text</param>
    /// <returns>Markup string with multi-line graph</returns>
    public static string RenderGraph(IReadOnlyList<double> values, int height = 4, string? color = null, string? label = null)
    {
        if (values.Count == 0)
            return "";

        const int levelsPerRow = 8; // Matches BrailleLevels.Length
        var totalLevels = height * levelsPerRow;
        var effectiveColor = color ?? "cyan1";

        var min = values.Min();
        var max = values.Max();
        var range = max - min;

        var lines = new List<string>();
        if (!string.IsNullOrEmpty(label))
        {
            lines.Add($"[grey70]{label}[/]");
        }

        // Handle flat data (all values the same)
        if (range == 0)
        {
            if (min == 0)
            {
                // All zeros - empty graph
                for (int row = 0; row < height; row++)
                {
                    lines.Add($"[{effectiveColor}]{new string('⠀', values.Count)}[/]");
                }
            }
            else
            {
                // Non-zero flat values - show as middle height
                for (int row = height - 1; row >= 0; row--)
                {
                    char ch = row < height / 2 ? '⣿' : '⠀';
                    lines.Add($"[{effectiveColor}]{new string(ch, values.Count)}[/]");
                }
            }
        }
        else
        {
            // Normal graph rendering - proper vertical bar chart
            for (int row = height - 1; row >= 0; row--)
            {
                var line = new StringBuilder();
                var rowBottomLevel = row * levelsPerRow;
                var rowTopLevel = (row + 1) * levelsPerRow;

                foreach (var value in values)
                {
                    var normalizedLevel = (value - min) / range * totalLevels;

                    if (normalizedLevel <= rowBottomLevel)
                    {
                        // Value is below this row - empty
                        line.Append(BrailleLevels[0]);
                    }
                    else if (normalizedLevel >= rowTopLevel)
                    {
                        // Value is above this row - fully filled
                        line.Append(BrailleLevels[levelsPerRow - 1]);
                    }
                    else
                    {
                        // Value is within this row - partial fill
                        var levelWithinRow = (int)(normalizedLevel - rowBottomLevel);
                        line.Append(BrailleLevels[Math.Clamp(levelWithinRow, 0, levelsPerRow - 1)]);
                    }
                }

                lines.Add($"[{effectiveColor}]{line}[/]");
            }
        }

        // Add baseline (x-axis)
        lines.Add($"[grey50]{new string('┈', values.Count)}[/]");

        return string.Join("\n", lines);
    }
}
