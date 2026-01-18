// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Models;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using Spectre.Console;

namespace ServerHub.UI;

/// <summary>
/// Handles displaying full widget content in a modal expansion dialog
/// </summary>
public static class WidgetExpansionDialog
{
    /// <summary>
    /// Shows a modal dialog with the full widget content (AgentStudio-inspired design)
    /// </summary>
    /// <param name="widgetId">Widget identifier</param>
    /// <param name="widgetData">Full widget data to display</param>
    /// <param name="windowSystem">Console window system</param>
    /// <param name="renderer">Widget renderer for creating content</param>
    /// <param name="config">Configuration for getting refresh interval</param>
    /// <param name="onUpdate">Callback to register the update function</param>
    /// <param name="onClose">Callback when modal closes</param>
    public static void Show(
        string widgetId,
        WidgetData widgetData,
        ConsoleWindowSystem windowSystem,
        WidgetRenderer renderer,
        ServerHubConfig? config,
        Action<Action<WidgetData>>? onUpdate = null,
        Action? onClose = null)
    {
        // Calculate modal size: 90% of screen with reasonable max constraints
        int screenWidth = Console.WindowWidth;
        int screenHeight = Console.WindowHeight;

        int modalWidth = Math.Min((int)(screenWidth * 0.9), 150);   // 90% of screen, max 150
        int modalHeight = Math.Min((int)(screenHeight * 0.9), 40);  // 90% of screen, max 40

        // Create borderless modal with AgentStudio aesthetic
        var modal = new WindowBuilder(windowSystem)
            .WithTitle($"Widget: {widgetData.Title}")
            .WithSize(modalWidth, modalHeight)
            .Centered()                         // Call AFTER WithSize for correct centering
            .AsModal()                          // Block input to main window
            .Borderless()                       // Clean, no traditional borders
            .Resizable(false)                   // Fixed size
            .Movable(false)                     // Cannot be dragged
            .WithColors(Color.Grey15, Color.Grey93)  // Dark bg, light text
            .Build();

        // Header section (AgentStudio pattern)
        int refreshInterval = GetRefreshInterval(widgetId, config);
        modal.AddControl(Controls.Markup()
            .WithName("modal_header")
            .AddLine($"[cyan1 bold]{widgetData.Title}[/]")
            .AddLine($"[grey50]Last updated: {widgetData.Timestamp:yyyy-MM-dd HH:mm:ss}  •  Refresh interval: {refreshInterval}s[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        // Separator rule
        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .Build());

        // Build full content using existing WidgetRenderer (NO truncation, NO duplication)
        var widgetPanel = renderer.CreateWidgetPanel(
            "modal_widget",               // Name for updating
            widgetData,
            isPinned: false,              // Always show full widget, not pinned version
            backgroundColor: Color.Grey15,
            onClickCallback: null,
            maxLines: null,               // NO truncation - show all content
            showTruncationIndicator: false
        );

        // Wrap the widget panel in a scrollable panel
        var scrollPanel = Controls.ScrollablePanel()
            .WithName("modal_scroll_panel")
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithScrollbarPosition(ScrollbarPosition.Right)
            .WithMouseWheel(true)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithBackgroundColor(Color.Grey15)  // Match modal background
            .AddControl(widgetPanel)
            .Build();

        modal.AddControl(scrollPanel);

        // Footer separator (sticky bottom)
        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .StickyBottom()
            .Build());

        // Footer with instructions (AgentStudio pattern)
        modal.AddControl(Controls.Markup()
            .AddLine("[grey70]Escape/Enter: Close  •  Arrows/Mouse Wheel: Scroll[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(0, 0, 0, 0)
            .StickyBottom()
            .Build());

        // Handle keyboard shortcuts (AgentStudio pattern)
        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape || e.KeyInfo.Key == ConsoleKey.Enter)
            {
                modal.Close();
                e.Handled = true;
            }
        };

        // Register update callback with captured references
        var headerControl = modal.FindControl<MarkupControl>("modal_header");

        onUpdate?.Invoke((newData) =>
        {
            // Update header with new timestamp
            if (headerControl != null)
            {
                headerControl.SetContent(new List<string>
                {
                    $"[cyan1 bold]{newData.Title}[/]",
                    $"[grey50]Last updated: {newData.Timestamp:yyyy-MM-dd HH:mm:ss}  •  Refresh interval: {refreshInterval}s[/]"
                });
            }

            // Update widget content (use captured reference to widgetPanel)
            renderer.UpdateWidgetPanel(widgetPanel, newData, maxLines: null, showTruncationIndicator: false);
        });

        // Handle modal close
        modal.OnClosed += (s, e) =>
        {
            onClose?.Invoke();
        };

        // Show modal
        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);
        scrollPanel.SetFocus(true, FocusReason.Programmatic);
    }

    /// <summary>
    /// Gets the refresh interval for a widget
    /// </summary>
    private static int GetRefreshInterval(string widgetId, ServerHubConfig? config)
    {
        var widgetConfig = config?.Widgets.GetValueOrDefault(widgetId);
        return widgetConfig?.Refresh ?? config?.DefaultRefresh ?? 5;
    }
}
