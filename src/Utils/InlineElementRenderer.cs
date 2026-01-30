// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using Spectre.Console;

namespace ServerHub.Utils;

/// <summary>
/// Shared rendering utilities for inline widget elements (sparklines, progress bars, graphs).
/// Used by both WidgetProtocolParser (table cells) and WidgetRenderer (row content).
/// </summary>
public static class InlineElementRenderer
{
    /// <summary>
    /// Bottom-to-top Unicode block characters for vertical bar representation (9 levels)
    /// Provides smoother visualization compared to braille characters
    /// </summary>
    public static readonly char[] BlockLevels = { ' ', '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█' };

    /// <summary>
    /// Renders a sparkline as markup text with background
    /// </summary>
    /// <param name="values">Data points to visualize</param>
    /// <param name="color">Spectre.Console color name, gradient name, or custom gradient (default: grey70)</param>
    /// <param name="width">Fixed width in columns (default: 30, pads with background if needed)</param>
    /// <returns>Markup string with block character sparkline</returns>
    public static string RenderSparkline(IReadOnlyList<double> values, string? color = null, int width = 30)
    {
        if (values.Count == 0)
            return "";

        // Use fixed width, pad remaining space with background
        var actualWidth = Math.Max(values.Count, width);
        var paddingNeeded = actualWidth - values.Count;

        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        var effectiveColor = color ?? "grey70";

        // Parse gradient if specified
        var gradientStops = ParseGradient(effectiveColor);
        var isGradientMode = gradientStops != null;

        var result = new StringBuilder();

        if (range == 0)
        {
            var flatColor = isGradientMode
                ? InterpolateGradientColor(gradientStops!, 0.5) // Use middle color for flat data
                : effectiveColor;

            // Render data columns
            for (int i = 0; i < values.Count; i++)
            {
                result.Append($"[{flatColor} on grey19]▄[/]");
            }

            // Pad with background
            for (int i = 0; i < paddingNeeded; i++)
            {
                result.Append("[grey19 on grey19] [/]");
            }
            return result.ToString();
        }

        if (isGradientMode)
        {
            // Gradient mode - horizontal gradient (left to right for time progression)
            for (int i = 0; i < values.Count; i++)
            {
                var value = values[i];
                var normalized = (value - min) / range;
                var level = (int)(normalized * (BlockLevels.Length - 1));
                var ch = BlockLevels[Math.Clamp(level, 0, BlockLevels.Length - 1)];

                // Apply gradient color based on position (left to right)
                var position = values.Count > 1 ? (double)i / (values.Count - 1) : 0.5;
                var charColor = InterpolateGradientColor(gradientStops!, position);

                result.Append($"[{charColor} on grey19]{ch}[/]");
            }

            // Pad with background
            for (int i = 0; i < paddingNeeded; i++)
            {
                result.Append("[grey19 on grey19] [/]");
            }
            return result.ToString();
        }
        else
        {
            // Solid color mode - render data columns
            for (int i = 0; i < values.Count; i++)
            {
                var value = values[i];
                var normalized = (value - min) / range;
                var level = (int)(normalized * (BlockLevels.Length - 1));
                var ch = BlockLevels[Math.Clamp(level, 0, BlockLevels.Length - 1)];
                result.Append($"[{effectiveColor} on grey19]{ch}[/]");
            }

            // Pad with background
            for (int i = 0; i < paddingNeeded; i++)
            {
                result.Append("[grey19 on grey19] [/]");
            }
            return result.ToString();
        }
    }

    /// <summary>
    /// Renders a mini progress bar as markup text
    /// </summary>
    /// <param name="percentage">Progress value 0-100</param>
    /// <param name="width">Character width of the bar (default: 10)</param>
    /// <param name="gradient">Optional gradient name or custom gradient (default: threshold colors)</param>
    /// <returns>Markup string with progress bar</returns>
    public static string RenderMiniProgress(int percentage, int width = 10, string? gradient = null)
    {
        percentage = Math.Clamp(percentage, 0, 100);
        width = Math.Clamp(width, 3, 50);  // Increased to support inline progress (30 chars)

        var filledWidth = (int)(width * percentage / 100.0);
        var emptyWidth = width - filledWidth;

        // Parse gradient if specified
        var gradientStops = ParseGradient(gradient);
        var isGradientMode = gradientStops != null;

        if (isGradientMode && filledWidth > 0)
        {
            // Gradient mode - per-character coloring based on percentage (not just filled blocks)
            var result = new StringBuilder();
            for (int i = 0; i < filledWidth; i++)
            {
                // Position based on percentage of total bar, not filled portion
                // This makes low percentages show start of gradient, high percentages show end
                var position = (double)i / width;
                var charColor = InterpolateGradientColor(gradientStops!, position);
                result.Append($"[{charColor} on grey19]█[/]");
            }
            // Subtle background with spaces (use grey19 on grey19 for empty space)
            for (int i = 0; i < emptyWidth; i++)
            {
                result.Append($"[grey19 on grey19] [/]");
            }
            result.Append($" {percentage}%");
            return result.ToString();
        }
        else
        {
            // Solid color mode - backward compatible (threshold colors) with background
            var result = new StringBuilder();
            var color = percentage switch
            {
                >= 90 => "red",
                >= 70 => "yellow",
                _ => "green"
            };
            for (int i = 0; i < filledWidth; i++)
            {
                result.Append($"[{color} on grey19]█[/]");
            }
            // Subtle background with spaces (use grey19 on grey19 for empty space)
            for (int i = 0; i < emptyWidth; i++)
            {
                result.Append($"[grey19 on grey19] [/]");
            }
            result.Append($" {percentage}%");
            return result.ToString();
        }
    }

    /// <summary>
    /// Renders a multi-line block graph (vertical bar chart)
    /// </summary>
    /// <param name="values">Data points to visualize</param>
    /// <param name="height">Number of rows (default: 4)</param>
    /// <param name="color">Spectre.Console color name, gradient name, or custom gradient (default: cyan1)</param>
    /// <param name="label">Optional label text</param>
    /// <param name="showBackground">Show grey35 background (default: true)</param>
    /// <param name="minValue">Optional fixed minimum value for scale (default: auto from data)</param>
    /// <param name="maxValue">Optional fixed maximum value for scale (default: auto from data)</param>
    /// <param name="width">Fixed width in columns (default: 30, pads with background if needed)</param>
    /// <returns>Markup string with multi-line graph</returns>
    public static string RenderGraph(IReadOnlyList<double> values, int height = 4, string? color = null, string? label = null, bool showBackground = true, double? minValue = null, double? maxValue = null, int width = 30)
    {
        if (values.Count == 0)
            return "";

        // Use fixed width, pad remaining space with background
        var paddingNeeded = Math.Max(0, width - values.Count);

        const int levelsPerRow = 9; // Matches BlockLevels.Length
        var totalLevels = height * levelsPerRow;
        var effectiveColor = color ?? "cyan1";

        // Use provided min/max or calculate from data
        var min = minValue ?? values.Min();
        var max = maxValue ?? values.Max();
        var range = max - min;

        // Parse gradient if specified
        var gradientStops = ParseGradient(effectiveColor);
        var isGradientMode = gradientStops != null;

        var lines = new List<string>();
        if (!string.IsNullOrEmpty(label))
        {
            lines.Add($"[grey70]{label}[/]");
        }

        // Handle flat data (all values the same)
        if (range == 0)
        {
            var flatColor = isGradientMode
                ? InterpolateGradientColor(gradientStops!, 0.5) // Use middle color for flat data
                : effectiveColor;

            var bgMarkup = showBackground ? " on grey19" : "";

            if (min == 0)
            {
                // All zeros - empty graph - apply markup per-character
                for (int row = 0; row < height; row++)
                {
                    var line = new StringBuilder();
                    for (int i = 0; i < values.Count; i++)
                    {
                        line.Append($"[{flatColor}{bgMarkup}] [/]");
                    }
                    // Pad remaining width with background
                    for (int i = 0; i < paddingNeeded; i++)
                    {
                        line.Append("[grey19 on grey19] [/]");
                    }
                    lines.Add(line.ToString());
                }
            }
            else
            {
                // Non-zero flat values - show as middle height - apply markup per-character
                for (int row = height - 1; row >= 0; row--)
                {
                    char ch = row < height / 2 ? '█' : ' ';
                    var line = new StringBuilder();
                    for (int i = 0; i < values.Count; i++)
                    {
                        line.Append($"[{flatColor}{bgMarkup}]{ch}[/]");
                    }
                    // Pad remaining width with background
                    for (int i = 0; i < paddingNeeded; i++)
                    {
                        line.Append("[grey19 on grey19] [/]");
                    }
                    lines.Add(line.ToString());
                }
            }
        }
        else if (isGradientMode)
        {
            // Gradient mode - vertical gradient within each column (bottom to top)
            var bgMarkup = showBackground ? " on grey19" : "";

            for (int row = height - 1; row >= 0; row--)
            {
                var line = new StringBuilder();
                var rowBottomLevel = row * levelsPerRow;
                var rowTopLevel = (row + 1) * levelsPerRow;

                for (int i = 0; i < values.Count; i++)
                {
                    var value = values[i];
                    var normalizedLevel = (value - min) / range * totalLevels;

                    char charToRender;
                    string charColor;

                    if (normalizedLevel <= rowBottomLevel)
                    {
                        // Value is below this row - empty (use lowest gradient color)
                        charToRender = BlockLevels[0];
                        charColor = InterpolateGradientColor(gradientStops!, 0.0);
                    }
                    else if (normalizedLevel >= rowTopLevel)
                    {
                        // Value is above this row - fully filled
                        // Color based on row height (vertical gradient)
                        var rowHeightNormalized = (double)(row + 1) / height;
                        charColor = InterpolateGradientColor(gradientStops!, rowHeightNormalized);
                        charToRender = BlockLevels[levelsPerRow - 1];
                    }
                    else
                    {
                        // Value is within this row - partial fill
                        // Color based on row height (vertical gradient)
                        var levelWithinRow = (int)(normalizedLevel - rowBottomLevel);
                        charToRender = BlockLevels[Math.Clamp(levelWithinRow, 0, levelsPerRow - 1)];

                        // Interpolate color based on the specific level within the row for smooth transition
                        var exactLevel = normalizedLevel;
                        var rowHeightNormalized = exactLevel / totalLevels;
                        charColor = InterpolateGradientColor(gradientStops!, rowHeightNormalized);
                    }

                    // Apply per-character gradient markup with optional background (same trick as sparkline)
                    line.Append($"[{charColor}{bgMarkup}]{charToRender}[/]");
                }

                // Pad remaining width with background
                for (int i = 0; i < paddingNeeded; i++)
                {
                    line.Append("[grey19 on grey19] [/]");
                }

                lines.Add(line.ToString());
            }
        }
        else
        {
            // Solid color mode - backward compatible
            var bgMarkup = showBackground ? " on grey19" : "";

            for (int row = height - 1; row >= 0; row--)
            {
                var line = new StringBuilder();
                var rowBottomLevel = row * levelsPerRow;
                var rowTopLevel = (row + 1) * levelsPerRow;

                foreach (var value in values)
                {
                    var normalizedLevel = (value - min) / range * totalLevels;

                    char charToRender;
                    if (normalizedLevel <= rowBottomLevel)
                    {
                        // Value is below this row - empty
                        charToRender = BlockLevels[0];
                    }
                    else if (normalizedLevel >= rowTopLevel)
                    {
                        // Value is above this row - fully filled
                        charToRender = BlockLevels[levelsPerRow - 1];
                    }
                    else
                    {
                        // Value is within this row - partial fill
                        var levelWithinRow = (int)(normalizedLevel - rowBottomLevel);
                        charToRender = BlockLevels[Math.Clamp(levelWithinRow, 0, levelsPerRow - 1)];
                    }

                    // Apply markup per-character (same trick as sparkline)
                    line.Append($"[{effectiveColor}{bgMarkup}]{charToRender}[/]");
                }

                // Pad remaining width with background
                for (int i = 0; i < paddingNeeded; i++)
                {
                    line.Append("[grey19 on grey19] [/]");
                }

                lines.Add(line.ToString());
            }
        }

        // Add baseline (x-axis) with full width
        lines.Add($"[grey50]{new string('┈', width)}[/]");

        return string.Join("\n", lines);
    }

    // ============================================================
    // Gradient System
    // ============================================================

    /// <summary>
    /// Predefined gradients for common visualization themes
    /// </summary>
    private static readonly Dictionary<string, List<string>> PredefinedGradients = new(StringComparer.OrdinalIgnoreCase)
    {
        { "cool", new List<string> { "blue", "cyan1" } },
        { "warm", new List<string> { "yellow", "orange1", "red" } },
        { "spectrum", new List<string> { "blue", "green", "yellow", "red" } },
        { "grayscale", new List<string> { "grey11", "grey100" } }
    };

    /// <summary>
    /// Parses a color specification into gradient stops.
    /// Returns null for single solid colors (backward compatibility).
    /// </summary>
    /// <param name="colorSpec">Color specification (solid color, predefined gradient, or custom gradient with →)</param>
    /// <returns>List of gradient stops, or null for solid color</returns>
    private static List<Color>? ParseGradient(string? colorSpec)
    {
        if (string.IsNullOrWhiteSpace(colorSpec))
            return null;

        // Check if it's a custom gradient (contains arrow)
        if (colorSpec.Contains('→'))
        {
            var stops = colorSpec.Split('→')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(ParseSpectreColor)
                .ToList();

            return stops.Count >= 2 ? stops : null;
        }

        // Check if it's a predefined gradient
        if (PredefinedGradients.TryGetValue(colorSpec, out var predefinedStops))
        {
            return predefinedStops.Select(ParseSpectreColor).ToList();
        }

        // Single solid color - return null for backward compatibility
        return null;
    }

    /// <summary>
    /// Interpolates a color from gradient stops based on normalized value (0.0-1.0)
    /// </summary>
    /// <param name="gradientStops">List of color stops</param>
    /// <param name="normalizedValue">Value from 0.0 to 1.0</param>
    /// <returns>Hex color string (#RRGGBB)</returns>
    private static string InterpolateGradientColor(List<Color> gradientStops, double normalizedValue)
    {
        if (gradientStops.Count == 0)
            return "#00FFFF"; // Fallback to cyan

        if (gradientStops.Count == 1)
            return ColorToHex(gradientStops[0]);

        // Clamp value
        normalizedValue = Math.Clamp(normalizedValue, 0.0, 1.0);

        // Calculate which segment we're in
        var segmentCount = gradientStops.Count - 1;
        var segmentSize = 1.0 / segmentCount;
        var segmentIndex = (int)(normalizedValue / segmentSize);

        // Handle edge case where value is exactly 1.0
        if (segmentIndex >= segmentCount)
        {
            segmentIndex = segmentCount - 1;
        }

        // Calculate position within the segment (0.0 to 1.0)
        var segmentStart = segmentIndex * segmentSize;
        var segmentPosition = (normalizedValue - segmentStart) / segmentSize;

        // Interpolate between the two stops using manual RGB blending
        var color1 = gradientStops[segmentIndex];
        var color2 = gradientStops[segmentIndex + 1];
        var blended = BlendColors(color1, color2, segmentPosition);

        return ColorToHex(blended);
    }

    /// <summary>
    /// Manually blends two colors using RGB interpolation
    /// </summary>
    private static Color BlendColors(Color color1, Color color2, double position)
    {
        var r = (byte)(color1.R + (color2.R - color1.R) * position);
        var g = (byte)(color1.G + (color2.G - color1.G) * position);
        var b = (byte)(color1.B + (color2.B - color1.B) * position);
        return new Color(r, g, b);
    }

    /// <summary>
    /// Converts a Spectre.Console Color to hex string
    /// </summary>
    private static string ColorToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    /// <summary>
    /// Parses a color name into a Spectre.Console Color struct
    /// </summary>
    /// <param name="colorName">Color name (e.g., "red", "blue", "cyan1")</param>
    /// <returns>Spectre.Console Color</returns>
    private static Color ParseSpectreColor(string colorName)
    {
        try
        {
            // Common color name mappings
            var color = colorName.ToLowerInvariant() switch
            {
                "red" => Color.Red,
                "green" => Color.Green,
                "blue" => Color.Blue,
                "yellow" => Color.Yellow,
                "cyan" => Color.Cyan1,
                "cyan1" => Color.Cyan1,
                "magenta" => Color.Magenta1,
                "orange" => Color.Orange1,
                "orange1" => Color.Orange1,
                "purple" => Color.Purple,
                "grey" => Color.Grey,
                "grey11" => Color.Grey11,
                "grey35" => Color.Grey35,
                "grey50" => Color.Grey50,
                "grey70" => Color.Grey70,
                "grey93" => Color.Grey93,
                "grey100" => Color.Grey100,
                "white" => Color.White,
                "black" => Color.Black,
                _ => Color.Cyan1
            };
            return color;
        }
        catch
        {
            // Fallback to cyan1 for invalid colors
            return Color.Cyan1;
        }
    }
}
