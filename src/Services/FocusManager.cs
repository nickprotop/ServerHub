// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Spectre.Console;
using SharpConsoleUI;
using SharpConsoleUI.Controls;

namespace ServerHub.Services;

/// <summary>
/// Manages widget focus state and navigation for ServerHub.
/// Implements virtual focus system for MarkupControl widgets that don't support native focus.
/// </summary>
public class FocusManager
{
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
