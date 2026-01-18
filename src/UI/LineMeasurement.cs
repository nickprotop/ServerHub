// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;

namespace ServerHub.UI;

/// <summary>
/// Utilities for measuring and truncating widget content lines
/// </summary>
public static class LineMeasurement
{
    /// <summary>
    /// Counts actual rendered lines including wrapped lines
    /// </summary>
    /// <param name="markupLines">List of markup strings</param>
    /// <param name="maxWidth">Available width in characters</param>
    /// <returns>Total visual line count</returns>
    public static int CountRenderedLines(List<string> markupLines, int maxWidth)
    {
        int totalLines = 0;
        foreach (var line in markupLines)
        {
            // Strip Spectre.Console markup to get actual content length
            var plainText = StripMarkup(line);

            if (string.IsNullOrEmpty(plainText))
            {
                totalLines++; // Empty lines count as 1
            }
            else
            {
                // Calculate wrapped lines
                int wrappedLines = (int)Math.Ceiling((double)plainText.Length / maxWidth);
                totalLines += Math.Max(1, wrappedLines);
            }
        }
        return totalLines;
    }

    /// <summary>
    /// Strips Spectre.Console markup tags to get plain text length
    /// </summary>
    private static string StripMarkup(string markup)
    {
        // Simple regex to remove [tag] and [/] patterns
        // Note: This is approximate - Spectre has complex markup
        return Regex.Replace(
            markup,
            @"\[/?[^\]]*\]",
            ""
        );
    }

    /// <summary>
    /// Truncates lines to fit within max line count
    /// </summary>
    /// <param name="lines">Original lines</param>
    /// <param name="maxLines">Maximum allowed lines of content (indicator and blank line are added on top)</param>
    /// <param name="indicatorLine">Line to add when truncated (e.g., "Press Enter...")</param>
    /// <returns>Truncated lines with optional indicator (blank line + indicator don't count toward maxLines)</returns>
    public static List<string> TruncateLines(
        List<string> lines,
        int maxLines,
        string? indicatorLine = null)
    {
        if (lines.Count <= maxLines)
            return lines;

        if (indicatorLine != null)
        {
            // Take maxLines of content, then add blank line and indicator on top
            var truncated = lines.Take(maxLines).ToList();
            truncated.Add("");  // Blank line above indicator (doesn't count toward maxLines)
            truncated.Add(indicatorLine);  // Indicator (doesn't count toward maxLines)
            return truncated;
        }
        else
        {
            return lines.Take(maxLines).ToList();
        }
    }
}
