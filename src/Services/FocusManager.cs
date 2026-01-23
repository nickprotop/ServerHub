// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Spectre.Console;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using ServerHub.Models;

namespace ServerHub.Services;

/// <summary>
/// Manages widget focus state and navigation for ServerHub.
/// Implements virtual focus system for MarkupControl widgets that don't support native focus.
/// </summary>
public class FocusManager
{
    public enum ReorderDirection
    {
        Earlier,  // Move left/up in order
        Later     // Move right/down in order
    }

    public enum VisualReorderDirection
    {
        VisualLeft,   // Swap with widget to the left in same row
        VisualRight,  // Swap with widget to the right in same row
        VisualUp,     // Swap with widget in row above
        VisualDown    // Swap with widget in row below
    }

    public enum ResizeDirection
    {
        WidthDecrease,  // Decrease column span
        WidthIncrease,  // Increase column span
        HeightDecrease, // Decrease max lines
        HeightIncrease  // Increase max lines
    }

    // Ordered placements from LayoutEngine (Row ASC, Column ASC)
    private List<LayoutEngine.WidgetPlacement> _widgetPlacements = new();

    // Current focus index (-1 = none, 0+ = index into placements)
    private int _focusedIndex = -1;

    // Fast lookup: widgetId -> placement index
    private Dictionary<string, int> _widgetIdToIndex = new();

    // Reference to main window for FindControl<IWindowControl>()
    private Window? _mainWindow;

    // Store original colors before applying focus (for restoration)
    private Dictionary<string, Color> _originalBackgroundColors = new();

    // Focus color configuration
    private readonly Color _focusedBackgroundColor = Color.Grey30;
    private readonly Color[] _unfocusedColors = {
        Color.Grey15, Color.Grey19, Color.Grey23
    };

    /// <summary>
    /// Initialize or re-initialize focus manager with new layout placements.
    /// Call this after CreateMainWindow() and after every RebuildLayout().
    /// </summary>
    public void Initialize(Window window, List<LayoutEngine.WidgetPlacement> placements)
    {
        _mainWindow = window;
        _widgetPlacements = placements;

        // Rebuild index lookup
        _widgetIdToIndex.Clear();
        for (int i = 0; i < placements.Count; i++)
        {
            _widgetIdToIndex[placements[i].WidgetId] = i;
        }

        // Reset focus if current index is now invalid
        if (_focusedIndex >= _widgetPlacements.Count)
        {
            _focusedIndex = -1;
        }

        // DEBUG logging removed - focus system operational
    }

    /// <summary>
    /// Focus the next widget in layout order (Tab key).
    /// Wraps around from last to first.
    /// </summary>
    public void FocusNext()
    {
        if (_widgetPlacements.Count == 0) return;

        // Remove visual from currently focused widget
        if (_focusedIndex >= 0 && _focusedIndex < _widgetPlacements.Count)
        {
            RemoveFocusVisual(_widgetPlacements[_focusedIndex].WidgetId);
        }

        // Advance to next widget (with wrap-around)
        _focusedIndex = (_focusedIndex + 1) % _widgetPlacements.Count;

        // Apply visual to newly focused widget
        ApplyFocusVisual(_widgetPlacements[_focusedIndex].WidgetId);
    }

    /// <summary>
    /// Focus the previous widget in layout order (Shift+Tab).
    /// Wraps around from first to last.
    /// </summary>
    public void FocusPrevious()
    {
        if (_widgetPlacements.Count == 0) return;

        // Remove visual from currently focused
        if (_focusedIndex >= 0 && _focusedIndex < _widgetPlacements.Count)
        {
            RemoveFocusVisual(_widgetPlacements[_focusedIndex].WidgetId);
        }

        // Move backward with wrap-around
        _focusedIndex--;
        if (_focusedIndex < 0)
        {
            _focusedIndex = _widgetPlacements.Count - 1;
        }

        ApplyFocusVisual(_widgetPlacements[_focusedIndex].WidgetId);
    }

    /// <summary>
    /// Focus the first widget in layout order.
    /// </summary>
    public void FocusFirst()
    {
        if (_widgetPlacements.Count == 0) return;

        if (_focusedIndex >= 0 && _focusedIndex < _widgetPlacements.Count)
        {
            RemoveFocusVisual(_widgetPlacements[_focusedIndex].WidgetId);
        }

        _focusedIndex = 0;
        ApplyFocusVisual(_widgetPlacements[0].WidgetId);
    }

    /// <summary>
    /// Focus a specific widget by ID.
    /// Returns true if widget found and focused, false otherwise.
    /// </summary>
    public bool FocusWidget(string widgetId)
    {
        if (!_widgetIdToIndex.TryGetValue(widgetId, out var index))
        {
            return false;
        }

        // Remove focus from current widget
        if (_focusedIndex >= 0 && _focusedIndex < _widgetPlacements.Count)
        {
            RemoveFocusVisual(_widgetPlacements[_focusedIndex].WidgetId);
        }

        _focusedIndex = index;
        ApplyFocusVisual(widgetId);
        return true;
    }

    /// <summary>
    /// Get the widget ID of the currently focused widget.
    /// Returns null if no widget is focused.
    /// </summary>
    public string? GetFocusedWidgetId()
    {
        if (_focusedIndex < 0 || _focusedIndex >= _widgetPlacements.Count)
        {
            return null;
        }
        return _widgetPlacements[_focusedIndex].WidgetId;
    }

    /// <summary>
    /// Get the current focus index.
    /// Returns -1 if no widget is focused.
    /// </summary>
    public int GetFocusedIndex() => _focusedIndex;

    /// <summary>
    /// Find the widget to the left of the current widget in the same row.
    /// Returns null if no widget found.
    /// </summary>
    private LayoutEngine.WidgetPlacement? FindWidgetToLeft(LayoutEngine.WidgetPlacement current)
    {
        var widgetsInSameRow = _widgetPlacements
            .Where(p => p.Row == current.Row && p.Column < current.Column)
            .OrderByDescending(p => p.Column)
            .ToList();

        return widgetsInSameRow.FirstOrDefault();
    }

    /// <summary>
    /// Find the widget to the right of the current widget in the same row.
    /// Returns null if no widget found.
    /// </summary>
    private LayoutEngine.WidgetPlacement? FindWidgetToRight(LayoutEngine.WidgetPlacement current)
    {
        var widgetsInSameRow = _widgetPlacements
            .Where(p => p.Row == current.Row && p.Column > current.Column)
            .OrderBy(p => p.Column)
            .ToList();

        return widgetsInSameRow.FirstOrDefault();
    }

    /// <summary>
    /// Calculate the column overlap amount between two widgets.
    /// Returns the number of columns that overlap or are adjacent.
    /// </summary>
    private int CalculateOverlap(LayoutEngine.WidgetPlacement current, LayoutEngine.WidgetPlacement target)
    {
        // Current widget occupies columns [current.Column, current.Column + current.ColumnSpan)
        int currentStart = current.Column;
        int currentEnd = current.Column + current.ColumnSpan;

        // Target widget occupies columns [target.Column, target.Column + target.ColumnSpan)
        int targetStart = target.Column;
        int targetEnd = target.Column + target.ColumnSpan;

        // Check adjacency: targetColumn <= current.Column + current.ColumnSpan
        // AND targetColumn + targetSpan >= current.Column
        if (targetStart <= currentEnd && targetEnd >= currentStart)
        {
            // Calculate overlap amount
            int overlapStart = Math.Max(currentStart, targetStart);
            int overlapEnd = Math.Min(currentEnd, targetEnd);
            return Math.Max(0, overlapEnd - overlapStart);
        }

        return 0;
    }

    /// <summary>
    /// Find the widget above the current widget with the most column overlap/adjacency.
    /// Returns null if no widget found.
    /// </summary>
    private LayoutEngine.WidgetPlacement? FindWidgetAbove(LayoutEngine.WidgetPlacement current)
    {
        var widgetsAbove = _widgetPlacements
            .Where(p => p.Row < current.Row)
            .OrderByDescending(p => p.Row)
            .ToList();

        if (widgetsAbove.Count == 0)
            return null;

        // Get widgets in the row immediately above
        var adjacentRow = widgetsAbove.First().Row;
        var widgetsInAdjacentRow = widgetsAbove
            .Where(p => p.Row == adjacentRow)
            .ToList();

        // Find widget with most overlap
        var bestMatch = widgetsInAdjacentRow
            .Select(w => new { Widget = w, Overlap = CalculateOverlap(current, w) })
            .Where(x => x.Overlap > 0)
            .OrderByDescending(x => x.Overlap)
            .FirstOrDefault();

        return bestMatch?.Widget;
    }

    /// <summary>
    /// Find the widget below the current widget with the most column overlap/adjacency.
    /// Returns null if no widget found.
    /// </summary>
    private LayoutEngine.WidgetPlacement? FindWidgetBelow(LayoutEngine.WidgetPlacement current)
    {
        var widgetsBelow = _widgetPlacements
            .Where(p => p.Row > current.Row)
            .OrderBy(p => p.Row)
            .ToList();

        if (widgetsBelow.Count == 0)
            return null;

        // Get widgets in the row immediately below
        var adjacentRow = widgetsBelow.First().Row;
        var widgetsInAdjacentRow = widgetsBelow
            .Where(p => p.Row == adjacentRow)
            .ToList();

        // Find widget with most overlap
        var bestMatch = widgetsInAdjacentRow
            .Select(w => new { Widget = w, Overlap = CalculateOverlap(current, w) })
            .Where(x => x.Overlap > 0)
            .OrderByDescending(x => x.Overlap)
            .FirstOrDefault();

        return bestMatch?.Widget;
    }

    /// <summary>
    /// Reorders the focused widget in the specified direction.
    /// Handles pinned vs regular widget separation.
    /// Returns true if widget was moved, false if at boundary.
    /// </summary>
    public bool ReorderWidget(
        ReorderDirection direction,
        ServerHubConfig config,
        Action<string, ReorderDirection, int>? onReorder = null)
    {
        // Get focused widget ID
        var focusedWidgetId = GetFocusedWidgetId();
        if (focusedWidgetId == null || config.Layout?.Order == null)
            return false;

        // Separate pinned and regular widgets
        var pinnedWidgets = config.Widgets
            .Where(kv => kv.Value.Pinned)
            .Select(kv => kv.Key)
            .ToList();

        var regularWidgets = config.Layout.Order
            .Where(id => !pinnedWidgets.Contains(id))
            .ToList();

        // Determine which list the focused widget belongs to
        bool isFocusedPinned = pinnedWidgets.Contains(focusedWidgetId);
        var targetList = isFocusedPinned ? pinnedWidgets : regularWidgets;

        // Find current index in target list
        int currentIndex = targetList.IndexOf(focusedWidgetId);
        if (currentIndex == -1)
            return false;

        // Calculate new index
        int newIndex = direction == ReorderDirection.Earlier
            ? currentIndex - 1
            : currentIndex + 1;

        // Check boundaries
        if (newIndex < 0 || newIndex >= targetList.Count)
            return false;

        // Swap elements
        (targetList[currentIndex], targetList[newIndex]) =
            (targetList[newIndex], targetList[currentIndex]);

        // Rebuild config.Layout.Order from pinned + regular
        config.Layout.Order.Clear();
        config.Layout.Order.AddRange(pinnedWidgets);
        config.Layout.Order.AddRange(regularWidgets);

        // Invoke callback with new position (global index)
        int globalIndex = isFocusedPinned
            ? newIndex
            : pinnedWidgets.Count + newIndex;
        onReorder?.Invoke(focusedWidgetId, direction, globalIndex);

        return true;
    }

    /// <summary>
    /// Reorders the focused widget by visual position (swaps with adjacent widget).
    /// Enforces pinned vs regular widget separation.
    /// Returns true if widgets were swapped, false if no valid target found.
    /// </summary>
    public bool ReorderWidgetVisual(
        VisualReorderDirection direction,
        ServerHubConfig config,
        Action<string, VisualReorderDirection>? onReorder = null)
    {
        var focusedWidgetId = GetFocusedWidgetId();
        if (focusedWidgetId == null || config.Layout?.Order == null)
            return false;

        // Get current widget placement
        if (!_widgetIdToIndex.TryGetValue(focusedWidgetId, out var currentIndex))
            return false;

        var currentPlacement = _widgetPlacements[currentIndex];

        // Find target widget by visual position
        LayoutEngine.WidgetPlacement? targetPlacement = direction switch
        {
            VisualReorderDirection.VisualLeft => FindWidgetToLeft(currentPlacement),
            VisualReorderDirection.VisualRight => FindWidgetToRight(currentPlacement),
            VisualReorderDirection.VisualUp => FindWidgetAbove(currentPlacement),
            VisualReorderDirection.VisualDown => FindWidgetBelow(currentPlacement),
            _ => null
        };

        if (targetPlacement == null)
            return false;

        var targetWidgetId = targetPlacement.WidgetId;

        // Check if both widgets have the same pinned status (enforce separation)
        var currentConfig = config.Widgets.GetValueOrDefault(focusedWidgetId);
        var targetConfig = config.Widgets.GetValueOrDefault(targetWidgetId);

        bool currentPinned = currentConfig?.Pinned ?? false;
        bool targetPinned = targetConfig?.Pinned ?? false;

        if (currentPinned != targetPinned)
            return false; // Cannot swap pinned with regular

        // Swap in config.Layout.Order
        int currentOrderIndex = config.Layout.Order.IndexOf(focusedWidgetId);
        int targetOrderIndex = config.Layout.Order.IndexOf(targetWidgetId);

        if (currentOrderIndex == -1 || targetOrderIndex == -1)
            return false;

        (config.Layout.Order[currentOrderIndex], config.Layout.Order[targetOrderIndex]) =
            (config.Layout.Order[targetOrderIndex], config.Layout.Order[currentOrderIndex]);

        onReorder?.Invoke(focusedWidgetId, direction);
        return true;
    }

    /// <summary>
    /// Resizes the focused widget in the specified direction.
    /// Returns true if widget was resized, false if at boundary.
    /// </summary>
    public bool ResizeWidget(
        ResizeDirection direction,
        ServerHubConfig config,
        Action<string, ResizeDirection, int>? onResize = null)
    {
        var focusedWidgetId = GetFocusedWidgetId();
        if (focusedWidgetId == null)
            return false;

        if (!config.Widgets.TryGetValue(focusedWidgetId, out var widgetConfig))
            return false;

        int newValue = 0;
        bool changed = false;

        switch (direction)
        {
            case ResizeDirection.WidthDecrease:
                newValue = Math.Max(1, (widgetConfig.ColumnSpan ?? 1) - 1);
                if (newValue != (widgetConfig.ColumnSpan ?? 1))
                {
                    widgetConfig.ColumnSpan = newValue;
                    changed = true;
                }
                break;

            case ResizeDirection.WidthIncrease:
                newValue = Math.Min(4, (widgetConfig.ColumnSpan ?? 1) + 1);
                if (newValue != (widgetConfig.ColumnSpan ?? 1))
                {
                    widgetConfig.ColumnSpan = newValue;
                    changed = true;
                }
                break;

            case ResizeDirection.HeightDecrease:
                newValue = Math.Max(5, (widgetConfig.MaxLines ?? 20) - 5);
                if (newValue != (widgetConfig.MaxLines ?? 20))
                {
                    widgetConfig.MaxLines = newValue;
                    changed = true;
                }
                break;

            case ResizeDirection.HeightIncrease:
                newValue = Math.Min(100, (widgetConfig.MaxLines ?? 20) + 5);
                if (newValue != (widgetConfig.MaxLines ?? 20))
                {
                    widgetConfig.MaxLines = newValue;
                    changed = true;
                }
                break;
        }

        if (changed)
        {
            onResize?.Invoke(focusedWidgetId, direction, newValue);
        }

        return changed;
    }

    /// <summary>
    /// Apply focus visual (background color) to a widget.
    /// </summary>
    private void ApplyFocusVisual(string widgetId)
    {
        var control = _mainWindow?.FindControl<IWindowControl>($"widget_{widgetId}");
        if (control == null) return;

        // Save original colors on first focus
        if (!_originalBackgroundColors.ContainsKey(widgetId))
        {
            // Save the MarkupControl's background color
            if (control is MarkupControl markup)
            {
                _originalBackgroundColors[widgetId] = markup.BackgroundColor ?? Color.Grey15;
            }
            else
            {
                _originalBackgroundColors[widgetId] = Color.Grey15;
            }
        }

        // Apply focus color to BOTH the control AND its container for consistent coloring

        // 1. Set on the MarkupControl itself (for the markup content area)
        if (control is MarkupControl markupControl)
        {
            markupControl.BackgroundColor = _focusedBackgroundColor;
            markupControl.Invalidate();
        }

        // 2. Set on the container (HorizontalGrid column) for full height coverage
        var container = control.Container;
        if (container != null)
        {
            container.BackgroundColor = _focusedBackgroundColor;
            container.Invalidate(true);
        }

        // Also try to scroll the focused widget into view
        ScrollToWidget(control);
    }

    /// <summary>
    /// Remove focus visual from a widget, restoring original background color.
    /// </summary>
    private void RemoveFocusVisual(string widgetId)
    {
        var control = _mainWindow?.FindControl<IWindowControl>($"widget_{widgetId}");
        if (control == null) return;

        // Restore original color to BOTH control and container
        if (_originalBackgroundColors.TryGetValue(widgetId, out var originalColor))
        {
            // 1. Restore on the MarkupControl itself
            if (control is MarkupControl markupControl)
            {
                markupControl.BackgroundColor = originalColor;
                markupControl.Invalidate();
            }

            // 2. Restore on the container (HorizontalGrid column)
            var container = control.Container;
            if (container != null)
            {
                container.BackgroundColor = originalColor;
                container.Invalidate(true);
            }
        }
    }

    /// <summary>
    /// Scroll the window to make the focused widget visible.
    /// </summary>
    private void ScrollToWidget(IWindowControl control)
    {
        _mainWindow?.ScrollToControl(control);
    }

    /// <summary>
    /// Get the MarkupControl from a control (handles unwrapping if needed).
    /// </summary>
    private MarkupControl? GetMarkupControl(IWindowControl control)
    {
        // Phase 1: Direct MarkupControl
        if (control is MarkupControl markup)
        {
            return markup;
        }

        // Phase 3: Will handle ClickableWidgetPanel wrapper here
        // if (control is ClickableWidgetPanel wrapper)
        // {
        //     return wrapper.InnerControl;
        // }

        return null;
    }
}
