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
        if (_mainWindow == null) return;

        try
        {
            // CRITICAL: Force layout update to get fresh widget positions
            // Without this, we get stale cached bounds from before the previous scroll
            _mainWindow.Invalidate(true);

            // Allow the layout to update (process pending invalidations)
            System.Threading.Thread.Sleep(1);

            // Get control's position using the layout manager
            var layoutManager = _mainWindow.GetType()
                .GetField("_layoutManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(_mainWindow);

            if (layoutManager == null) return;

            var getOrCreateMethod = layoutManager.GetType().GetMethod("GetOrCreateControlBounds");
            if (getOrCreateMethod == null) return;

            var bounds = getOrCreateMethod.Invoke(layoutManager, new object[] { control });
            if (bounds == null) return;

            // Get the control's content bounds
            var controlBoundsProperty = bounds.GetType().GetProperty("ControlContentBounds");
            if (controlBoundsProperty == null) return;

            var controlBounds = controlBoundsProperty.GetValue(bounds);
            if (controlBounds == null) return;

            // Extract Y position and height
            var yProperty = controlBounds.GetType().GetProperty("Y");
            var heightProperty = controlBounds.GetType().GetProperty("Height");
            if (yProperty == null || heightProperty == null) return;

            int contentTop = (int)(yProperty.GetValue(controlBounds) ?? 0);
            int contentHeight = (int)(heightProperty.GetValue(controlBounds) ?? 0);
            int contentBottom = contentTop + contentHeight;

            // Get window dimensions
            int windowHeight = _mainWindow.Height;
            int currentScrollOffset = _mainWindow.ScrollOffset;

            // Calculate visible region
            // IMPORTANT: Control bounds Y values are RELATIVE to scrollOffset, not absolute!
            // When scrollOffset=0, widget at absolute Y=50 has relative Y=50
            // When scrollOffset=50, same widget has relative Y=0
            // Visible region in relative coordinates is always [0, windowHeight-2]
            int visibleTop = 0;
            int visibleBottom = windowHeight - 2;  // -2 for window borders

            // Scroll if widget is not fully visible
            // Check if widget is cut off at top or bottom
            bool topCutOff = contentTop < visibleTop;
            bool bottomCutOff = contentBottom > visibleBottom;

            if (topCutOff)
            {
                // Widget top is cut off - scroll up to align top with viewport top
                int absoluteY = currentScrollOffset + contentTop;
                int newOffset = Math.Max(0, absoluteY);
                _mainWindow.ScrollOffset = newOffset;
                _mainWindow.Invalidate(true);
            }
            else if (bottomCutOff)
            {
                // Widget bottom is cut off - scroll to show widget fully
                int absoluteTopY = currentScrollOffset + contentTop;

                // Prefer showing widget at top of viewport for consistency
                int newOffset = absoluteTopY;

                // Clamp to maxOffset to prevent scrolling past content end
                int maxOffset = int.MaxValue;
                var windowContentLayoutField = _mainWindow.GetType()
                    .GetField("_windowContentLayout", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (windowContentLayoutField != null)
                {
                    var windowContentLayout = windowContentLayoutField.GetValue(_mainWindow);
                    if (windowContentLayout != null)
                    {
                        var maxScrollOffsetProperty = windowContentLayout.GetType().GetProperty("MaxScrollOffset");
                        if (maxScrollOffsetProperty != null)
                        {
                            maxOffset = (int)(maxScrollOffsetProperty.GetValue(windowContentLayout) ?? 0);
                            newOffset = Math.Min(newOffset, maxOffset);
                        }
                    }
                }

                _mainWindow.ScrollOffset = Math.Max(0, newOffset);
                _mainWindow.Invalidate(true);
            }

            // Widget is already visible - no scroll needed
        }
        catch
        {
            // If reflection fails, widget may be off-screen but won't crash
        }
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
