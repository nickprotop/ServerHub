// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Models;

namespace ServerHub.Services;

/// <summary>
/// Calculates responsive widget layout based on terminal dimensions
/// Supports breakpoint-based column layout, explicit row layouts, and column spanning
/// </summary>
public class LayoutEngine
{
    /// <summary>
    /// Represents a widget's calculated position and dimensions
    /// </summary>
    public record WidgetPlacement(
        string WidgetId,
        int Column,
        int Row,
        int ColumnSpan,
        bool IsPinned
    );

    /// <summary>
    /// Calculates widget placements based on terminal dimensions and configuration
    /// </summary>
    /// <param name="config">ServerHub configuration</param>
    /// <param name="terminalWidth">Terminal width in columns</param>
    /// <param name="terminalHeight">Terminal height in rows</param>
    /// <returns>List of widget placements</returns>
    public List<WidgetPlacement> CalculateLayout(
        ServerHubConfig config,
        int terminalWidth,
        int terminalHeight)
    {
        // Determine column count based on terminal width
        int columnCount = GetColumnCount(config, terminalWidth);

        // Check if using explicit row layout
        if (config.Layout?.Rows != null && config.Layout.Rows.Count > 0)
        {
            return CalculateExplicitRowLayout(config, columnCount);
        }

        // Use automatic flow layout with column_span support
        return CalculateFlowLayout(config, columnCount);
    }

    /// <summary>
    /// Calculates explicit row-based layout from layout.rows configuration
    /// </summary>
    private List<WidgetPlacement> CalculateExplicitRowLayout(ServerHubConfig config, int columnCount)
    {
        var placements = new List<WidgetPlacement>();
        int currentRow = 0;

        // First, place pinned widgets (only enabled ones)
        var pinnedWidgets = config.Widgets
            .Where(w => w.Value.Pinned && w.Value.Enabled)
            .Select(w => w.Key)
            .ToList();

        foreach (var widgetId in pinnedWidgets)
        {
            if (config.Widgets.TryGetValue(widgetId, out _))
            {
                placements.Add(new WidgetPlacement(
                    WidgetId: widgetId,
                    Column: 0,
                    Row: currentRow++,
                    ColumnSpan: columnCount,
                    IsPinned: true
                ));
            }
        }

        // Then place widgets according to explicit rows
        foreach (var layoutRow in config.Layout!.Rows!)
        {
            int currentColumn = 0;

            foreach (var widgetId in layoutRow.Widgets)
            {
                if (config.Widgets.TryGetValue(widgetId, out var widgetConfig))
                {
                    // Skip pinned widgets (already placed)
                    if (widgetConfig.Pinned)
                        continue;

                    // Skip disabled widgets
                    if (!widgetConfig.Enabled)
                        continue;

                    // Calculate column span for this widget
                    int widgetColumnSpan = CalculateColumnSpan(widgetConfig, columnCount, layoutRow.Widgets.Count);

                    placements.Add(new WidgetPlacement(
                        WidgetId: widgetId,
                        Column: currentColumn,
                        Row: currentRow,
                        ColumnSpan: widgetColumnSpan,
                        IsPinned: false
                    ));

                    currentColumn += widgetColumnSpan;

                    // If we've filled the row, break
                    if (currentColumn >= columnCount)
                        break;
                }
            }

            currentRow++;
        }

        return placements;
    }

    /// <summary>
    /// Calculates automatic flow layout with column_span support
    /// </summary>
    private List<WidgetPlacement> CalculateFlowLayout(ServerHubConfig config, int columnCount)
    {
        var placements = new List<WidgetPlacement>();
        int currentRow = 0;

        // Get widget order
        var widgetOrder = GetWidgetOrder(config);

        // Separate pinned and regular widgets
        var pinnedWidgets = new List<(string id, WidgetConfig config)>();
        var regularWidgets = new List<(string id, WidgetConfig config)>();

        foreach (var widgetId in widgetOrder)
        {
            if (config.Widgets.TryGetValue(widgetId, out var widgetConfig))
            {
                if (widgetConfig.Pinned)
                {
                    pinnedWidgets.Add((widgetId, widgetConfig));
                }
                else
                {
                    regularWidgets.Add((widgetId, widgetConfig));
                }
            }
        }

        // Place pinned widgets first (they appear as top tiles)
        foreach (var (widgetId, _) in pinnedWidgets)
        {
            placements.Add(new WidgetPlacement(
                WidgetId: widgetId,
                Column: 0,
                Row: currentRow++,
                ColumnSpan: columnCount,
                IsPinned: true
            ));
        }

        // Place regular widgets in a flowing grid
        int currentColumn = 0;
        foreach (var (widgetId, widgetConfig) in regularWidgets)
        {
            // Calculate column span for this widget
            int widgetColumnSpan = CalculateColumnSpan(widgetConfig, columnCount, 0);

            // If widget doesn't fit in current row, move to next row
            if (currentColumn + widgetColumnSpan > columnCount)
            {
                currentColumn = 0;
                currentRow++;
            }

            placements.Add(new WidgetPlacement(
                WidgetId: widgetId,
                Column: currentColumn,
                Row: currentRow,
                ColumnSpan: widgetColumnSpan,
                IsPinned: false
            ));

            currentColumn += widgetColumnSpan;

            // Move to next row if we've filled this one
            if (currentColumn >= columnCount)
            {
                currentColumn = 0;
                currentRow++;
            }
        }

        return placements;
    }

    /// <summary>
    /// Calculates the column span for a widget based on configuration
    /// </summary>
    private int CalculateColumnSpan(WidgetConfig widgetConfig, int columnCount, int widgetsInRow)
    {
        // Priority 1: Explicit column_span
        if (widgetConfig.ColumnSpan.HasValue)
        {
            return Math.Min(widgetConfig.ColumnSpan.Value, columnCount);
        }

        // Priority 2: If in explicit row with only one widget, take full row
        if (widgetsInRow == 1)
        {
            return columnCount;
        }

        // Default: single column
        return 1;
    }

    /// <summary>
    /// Gets the column count based on terminal width and breakpoints
    /// </summary>
    private int GetColumnCount(ServerHubConfig config, int terminalWidth)
    {
        var breakpoints = config.Breakpoints ?? new BreakpointConfig();

        if (terminalWidth < breakpoints.Double)
            return 1;
        else if (terminalWidth < breakpoints.Triple)
            return 2;
        else if (terminalWidth < breakpoints.Quad)
            return 3;
        else
            return 4;
    }

    /// <summary>
    /// Gets the widget order from configuration
    /// Uses layout.order if available, otherwise returns all widgets
    /// Filters out disabled widgets and duplicates
    /// </summary>
    private List<string> GetWidgetOrder(ServerHubConfig config)
    {
        List<string> allWidgets;

        if (config.Layout?.Order != null && config.Layout.Order.Count > 0)
        {
            allWidgets = config.Layout.Order;
        }
        else
        {
            allWidgets = config.Widgets.Keys.ToList();
        }

        // Filter out disabled widgets and remove duplicates (preserve first occurrence)
        var seenWidgets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return allWidgets
            .Where(widgetId =>
            {
                if (!config.Widgets.TryGetValue(widgetId, out var widgetConfig) || !widgetConfig.Enabled)
                    return false;

                // Skip if we've already seen this widget (handles duplicates in layout.order)
                if (seenWidgets.Contains(widgetId))
                    return false;

                seenWidgets.Add(widgetId);
                return true;
            })
            .ToList();
    }

}
