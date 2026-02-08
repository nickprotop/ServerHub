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

        // Limit values to width (take last N values if too many)
        var valuesToRender = values.Count > width
            ? values.Skip(values.Count - width).ToList()
            : values;

        var paddingNeeded = width - valuesToRender.Count;

        var min = valuesToRender.Min();
        var max = valuesToRender.Max();
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
            for (int i = 0; i < valuesToRender.Count; i++)
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
            for (int i = 0; i < valuesToRender.Count; i++)
            {
                var value = valuesToRender[i];
                var normalized = (value - min) / range;
                var level = (int)(normalized * (BlockLevels.Length - 1));
                var ch = BlockLevels[Math.Clamp(level, 0, BlockLevels.Length - 1)];

                // Apply gradient color based on position (left to right)
                var position = valuesToRender.Count > 1 ? (double)i / (valuesToRender.Count - 1) : 0.5;
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
            for (int i = 0; i < valuesToRender.Count; i++)
            {
                var value = valuesToRender[i];
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

    // ============================================================
    // Line Graph Rendering
    // ============================================================

    /// <summary>
    /// Renders a line graph with smooth connected lines
    /// </summary>
    /// <param name="values">Data points to plot</param>
    /// <param name="width">Graph width in characters (default: 60)</param>
    /// <param name="height">Graph height in lines (default: 8)</param>
    /// <param name="style">Rendering style: "braille" (high-res) or "ascii" (simple)</param>
    /// <param name="color">Solid color name or null to use gradient</param>
    /// <param name="gradient">Gradient specification (e.g., "blue→red", "warm", "cool")</param>
    /// <param name="label">Optional label/title</param>
    /// <param name="minValue">Optional fixed minimum value (null = auto-scale)</param>
    /// <param name="maxValue">Optional fixed maximum value (null = auto-scale)</param>
    /// <returns>Markup string with multi-line line graph</returns>
    public static string RenderLineGraph(IReadOnlyList<double> values, int width = 60, int height = 8,
        string style = "braille", string? color = null, string? gradient = null, string? label = null,
        double? minValue = null, double? maxValue = null)
    {
        if (values.Count == 0)
            return "";

        // Route to appropriate renderer
        return style.ToLowerInvariant() switch
        {
            "ascii" => RenderLineGraphASCII(values, width, height, color, gradient, label, minValue, maxValue),
            _ => RenderLineGraphBraille(values, width, height, color, gradient, label, minValue, maxValue)
        };
    }

    /// <summary>
    /// Renders a line graph using Braille characters for high-resolution display (2×4 pixels per character)
    /// </summary>
    private static string RenderLineGraphBraille(IReadOnlyList<double> values, int width, int height,
        string? color, string? gradient, string? label, double? minValue, double? maxValue)
    {
        if (values.Count == 0)
            return "";

        // Calculate scale
        var min = minValue ?? values.Min();
        var max = maxValue ?? values.Max();
        var range = max - min;

        // Braille canvas: 2×4 pixels per character
        var pixelWidth = width * 2;
        var pixelHeight = height * 4;

        // Initialize pixel grid (false = empty, true = filled)
        var pixels = new bool[pixelHeight, pixelWidth];

        // Handle flat data
        if (range == 0)
        {
            var midY = pixelHeight / 2;
            for (int x = 0; x < Math.Min(values.Count, pixelWidth); x++)
            {
                pixels[midY, x] = true;
            }
        }
        else
        {
            // Plot line using Bresenham's algorithm
            for (int i = 0; i < values.Count - 1; i++)
            {
                // Map data points to pixel coordinates
                var x1 = (int)(i * (pixelWidth - 1.0) / Math.Max(values.Count - 1, 1));
                var y1 = pixelHeight - 1 - (int)((values[i] - min) / range * (pixelHeight - 1));
                var x2 = (int)((i + 1) * (pixelWidth - 1.0) / Math.Max(values.Count - 1, 1));
                var y2 = pixelHeight - 1 - (int)((values[i + 1] - min) / range * (pixelHeight - 1));

                // Clamp coordinates
                x1 = Math.Clamp(x1, 0, pixelWidth - 1);
                y1 = Math.Clamp(y1, 0, pixelHeight - 1);
                x2 = Math.Clamp(x2, 0, pixelWidth - 1);
                y2 = Math.Clamp(y2, 0, pixelHeight - 1);

                // Draw line segment
                DrawLinePixels(pixels, x1, y1, x2, y2);
            }
        }

        // Resolve color/gradient
        var colorSpec = ResolveColorOrGradient(color, gradient);
        var gradientStops = ParseGradient(colorSpec);
        var isGradientMode = gradientStops != null;

        // Convert pixel grid to Braille characters
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(label))
        {
            lines.Add($"[grey70]{label}[/]");
        }

        for (int row = 0; row < height; row++)
        {
            var line = new StringBuilder();
            for (int col = 0; col < width; col++)
            {
                var brailleChar = GetBrailleChar(pixels, row, col);

                if (brailleChar == '⠀') // Empty Braille (U+2800)
                {
                    line.Append($"[grey19 on grey19]⠀[/]");
                }
                else
                {
                    // Calculate position for gradient (0.0 to 1.0)
                    var position = width > 1 ? (double)col / (width - 1) : 0.5;

                    if (isGradientMode)
                    {
                        var charColor = InterpolateGradientColor(gradientStops!, position);
                        line.Append($"[{charColor} on grey19]{brailleChar}[/]");
                    }
                    else
                    {
                        var effectiveColor = colorSpec ?? "cyan1";
                        line.Append($"[{effectiveColor} on grey19]{brailleChar}[/]");
                    }
                }
            }
            lines.Add(line.ToString());
        }

        // Add Y-axis labels and baseline
        lines.Add(GetYAxisLabel(min, max));
        lines.Add($"[grey50]{new string('┈', width)}[/]");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Renders a line graph using ASCII box drawing characters for simple display
    /// </summary>
    private static string RenderLineGraphASCII(IReadOnlyList<double> values, int width, int height,
        string? color, string? gradient, string? label, double? minValue, double? maxValue)
    {
        if (values.Count == 0)
            return "";

        // Calculate scale
        var min = minValue ?? values.Min();
        var max = maxValue ?? values.Max();
        var range = max - min;

        // Initialize character grid
        var grid = new char[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                grid[y, x] = ' ';
            }
        }

        // Handle flat data
        if (range == 0)
        {
            var midY = height / 2;
            for (int x = 0; x < Math.Min(values.Count, width); x++)
            {
                grid[midY, x] = '─';
            }
        }
        else
        {
            // Plot line using ASCII characters
            for (int i = 0; i < values.Count - 1; i++)
            {
                var x1 = (int)(i * (width - 1.0) / Math.Max(values.Count - 1, 1));
                var y1 = height - 1 - (int)((values[i] - min) / range * (height - 1));
                var x2 = (int)((i + 1) * (width - 1.0) / Math.Max(values.Count - 1, 1));
                var y2 = height - 1 - (int)((values[i + 1] - min) / range * (height - 1));

                // Clamp coordinates
                x1 = Math.Clamp(x1, 0, width - 1);
                y1 = Math.Clamp(y1, 0, height - 1);
                x2 = Math.Clamp(x2, 0, width - 1);
                y2 = Math.Clamp(y2, 0, height - 1);

                // Draw line segment with ASCII characters
                DrawLineASCII(grid, x1, y1, x2, y2);
            }
        }

        // Resolve color/gradient
        var colorSpec = ResolveColorOrGradient(color, gradient);
        var gradientStops = ParseGradient(colorSpec);
        var isGradientMode = gradientStops != null;

        // Convert grid to markup
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(label))
        {
            lines.Add($"[grey70]{label}[/]");
        }

        for (int y = 0; y < height; y++)
        {
            var line = new StringBuilder();
            for (int x = 0; x < width; x++)
            {
                var ch = grid[y, x];

                if (ch == ' ')
                {
                    line.Append($"[grey19 on grey19] [/]");
                }
                else
                {
                    var position = width > 1 ? (double)x / (width - 1) : 0.5;

                    if (isGradientMode)
                    {
                        var charColor = InterpolateGradientColor(gradientStops!, position);
                        line.Append($"[{charColor} on grey19]{ch}[/]");
                    }
                    else
                    {
                        var effectiveColor = colorSpec ?? "cyan1";
                        line.Append($"[{effectiveColor} on grey19]{ch}[/]");
                    }
                }
            }
            lines.Add(line.ToString());
        }

        // Add Y-axis labels and baseline
        lines.Add(GetYAxisLabel(min, max));
        lines.Add($"[grey50]{new string('┈', width)}[/]");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Draws a line in a pixel grid using Bresenham's line algorithm
    /// </summary>
    private static void DrawLinePixels(bool[,] pixels, int x0, int y0, int x1, int y1)
    {
        int height = pixels.GetLength(0);
        int width = pixels.GetLength(1);

        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            // Set pixel if within bounds
            if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
            {
                pixels[y0, x0] = true;
            }

            if (x0 == x1 && y0 == y1)
                break;

            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    /// <summary>
    /// Draws a line in a character grid using ASCII box drawing characters
    /// </summary>
    private static void DrawLineASCII(char[,] grid, int x0, int y0, int x1, int y1)
    {
        int height = grid.GetLength(0);
        int width = grid.GetLength(1);

        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
            {
                // Determine character based on line direction
                if (dx == 0)
                {
                    grid[y0, x0] = '│'; // Vertical
                }
                else if (dy == 0)
                {
                    grid[y0, x0] = '─'; // Horizontal
                }
                else if ((sx > 0 && sy > 0) || (sx < 0 && sy < 0))
                {
                    grid[y0, x0] = '╲'; // Diagonal down-right or up-left
                }
                else
                {
                    grid[y0, x0] = '╱'; // Diagonal up-right or down-left
                }
            }

            if (x0 == x1 && y0 == y1)
                break;

            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    /// <summary>
    /// Converts a 2×4 pixel block to a Braille character
    /// Braille pattern dots are numbered:
    ///   1 4
    ///   2 5
    ///   3 6
    ///   7 8
    /// Unicode Braille starts at U+2800
    /// </summary>
    private static char GetBrailleChar(bool[,] pixels, int row, int col)
    {
        int pixelHeight = pixels.GetLength(0);
        int pixelWidth = pixels.GetLength(1);

        // Braille base character (all dots off)
        int brailleValue = 0x2800;

        // Map 2×4 pixel block to Braille dots
        int baseY = row * 4;
        int baseX = col * 2;

        // Braille dot bit positions
        int[] dotBits = { 0, 1, 2, 6, 3, 4, 5, 7 }; // Mapping to Unicode Braille pattern

        for (int py = 0; py < 4; py++)
        {
            for (int px = 0; px < 2; px++)
            {
                int y = baseY + py;
                int x = baseX + px;

                if (y < pixelHeight && x < pixelWidth && pixels[y, x])
                {
                    int dotIndex = py * 2 + px;
                    brailleValue |= (1 << dotBits[dotIndex]);
                }
            }
        }

        return (char)brailleValue;
    }

    /// <summary>
    /// Gets a pixel value from the grid (for bounds checking)
    /// </summary>
    private static bool GetPixel(bool[,] pixels, int y, int x)
    {
        int height = pixels.GetLength(0);
        int width = pixels.GetLength(1);

        if (y >= 0 && y < height && x >= 0 && x < width)
        {
            return pixels[y, x];
        }
        return false;
    }

    /// <summary>
    /// Generates Y-axis label showing min and max values
    /// </summary>
    private static string GetYAxisLabel(double min, double max)
    {
        return $"[grey50]Min: {min:F1} Max: {max:F1}[/]";
    }

    /// <summary>
    /// Resolves color or gradient specification, prioritizing gradient over color
    /// </summary>
    private static string? ResolveColorOrGradient(string? color, string? gradient)
    {
        return gradient ?? color;
    }
}
