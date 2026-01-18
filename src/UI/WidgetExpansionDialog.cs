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
    /// <param name="onRefreshRequested">Callback to request widget refresh</param>
    /// <param name="onClose">Callback when modal closes</param>
    public static void Show(
        string widgetId,
        WidgetData widgetData,
        ConsoleWindowSystem windowSystem,
        WidgetRenderer renderer,
        ServerHubConfig? config,
        Action<Action<WidgetData>>? onUpdate = null,
        Action? onRefreshRequested = null,
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
        var headerLine2 = $"[grey50]Last updated: {widgetData.Timestamp:yyyy-MM-dd HH:mm:ss}  •  Refresh: {refreshInterval}s";
        if (widgetData.HasActions)
        {
            var actionCount = widgetData.Actions.Count;
            var actionText = actionCount == 1 ? "action" : "actions";
            headerLine2 += $"  •  {actionCount} {actionText}";
        }
        headerLine2 += "[/]";

        modal.AddControl(Controls.Markup()
            .WithName("modal_header")
            .AddLine($"[cyan1 bold]{widgetData.Title}[/]")
            .AddLine(headerLine2)
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

        // Create side-by-side layout: Content (left 65%) + Actions (right 35%)
        var mainGrid = Controls.HorizontalGrid()
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .Build();

        // LEFT COLUMN - Content (65%)
        var contentColumn = new ColumnContainer(mainGrid)
        {
            VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Fill
        };

        // Wrap widget panel in scrollable panel
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

        contentColumn.AddContent(scrollPanel);
        // No width or flex set - fills remaining space
        mainGrid.AddColumn(contentColumn);

        // MIDDLE - Vertical separator
        var separatorColumn = new ColumnContainer(mainGrid)
        {
            Width = 1,
            VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Fill,
            Visible = widgetData.HasActions
        };
        var actionsSeparator = new SeparatorControl
        {
            ForegroundColor = Color.Grey23,
            VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Fill,
            Visible = widgetData.HasActions
        };
        separatorColumn.AddContent(actionsSeparator);
        mainGrid.AddColumn(separatorColumn);

        // RIGHT COLUMN - Actions (fixed 40 width)
        var actionsColumn = new ColumnContainer(mainGrid)
        {
            Width = 40,  // Fixed width
            VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Fill,
            Visible = widgetData.HasActions
        };

        // Actions header
        var actionsHeader = Controls.Markup()
            .WithName("actions_header")
            .AddLine("[cyan1 bold]Actions[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 1, 1, 0)
            .Build();
        actionsHeader.BackgroundColor = Color.Grey19;
        actionsHeader.ForegroundColor = Color.Grey93;
        actionsColumn.AddContent(actionsHeader);

        // Actions list
        var actionsList = Controls.List()
            .WithName("actions_list")
            .WithTitle("")  // Remove default "List" title - we have markup header above
            .SimpleMode()
            .WithColors(Color.Grey19, Color.Grey93)
            .WithFocusedColors(Color.Grey19, Color.Grey93)  // AgentStudio pattern
            .WithHighlightColors(Color.Grey35, Color.White)  // Subtle gray highlight
            .WithDoubleClickActivation(true)
            .WithMargin(1, 0, 1, 0)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)  // Stretch horizontally
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .Build();

        // Populate initial actions
        foreach (var action in widgetData.Actions)
        {
            var label = action.Label;
            if (action.IsDanger)
            {
                // Pad with spaces and add warning symbol
                var padding = new string(' ', Math.Max(0, 30 - action.Label.Length));
                label = $"[yellow]{action.Label}[/]{padding}⚠";
            }
            var item = new ListItem(label) { Tag = action };
            actionsList.AddItem(item);
        }

        // Handle action activation (Enter or double-click)
        actionsList.ItemActivated += (s, item) =>
        {
            if (item?.Tag is WidgetAction selectedAction)
            {
                ExecuteAction(selectedAction, windowSystem, modal, onRefreshRequested);
            }
        };

        actionsColumn.AddContent(actionsList);
        actionsColumn.BackgroundColor = Color.Grey19;  // Subtle visual distinction
        actionsColumn.ForegroundColor = Color.Grey93;
        mainGrid.AddColumn(actionsColumn);

        // Add main grid to modal
        modal.AddControl(mainGrid);

        // Footer separator (sticky bottom)
        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .StickyBottom()
            .Build());

        // Footer with instructions (AgentStudio pattern)
        var footerInstructions = widgetData.HasActions
            ? "[grey70]↑↓/Click: Select  •  Enter/Dbl-click: Execute  •  Esc: Close[/]"
            : "[grey70]Escape/Enter: Close  •  Arrows/Mouse Wheel: Scroll[/]";

        modal.AddControl(Controls.Markup()
            .AddLine(footerInstructions)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(0, 0, 0, 0)
            .StickyBottom()
            .Build());

        // Handle keyboard shortcuts (AgentStudio pattern)
        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                modal.Close();
                e.Handled = true;
            }
            // Only close on Enter if there are NO actions (content-only mode)
            else if (e.KeyInfo.Key == ConsoleKey.Enter && !widgetData.HasActions)
            {
                modal.Close();
                e.Handled = true;
            }
            // Quick action execution via number keys (1-9)
            else if (widgetData.HasActions && e.KeyInfo.Key >= ConsoleKey.D1 && e.KeyInfo.Key <= ConsoleKey.D9)
            {
                var index = e.KeyInfo.Key - ConsoleKey.D1;
                if (index < widgetData.Actions.Count)
                {
                    var selectedAction = widgetData.Actions[index];
                    ExecuteAction(selectedAction, windowSystem, modal, onRefreshRequested);
                    e.Handled = true;
                }
            }
        };

        // Register update callback with captured references
        var headerControl = modal.FindControl<MarkupControl>("modal_header");

        onUpdate?.Invoke((newData) =>
        {
            // Update header with new timestamp
            if (headerControl != null)
            {
                var updatedHeaderLine2 = $"[grey50]Last updated: {newData.Timestamp:yyyy-MM-dd HH:mm:ss}  •  Refresh: {refreshInterval}s";
                if (newData.HasActions)
                {
                    var actionCount = newData.Actions.Count;
                    var actionText = actionCount == 1 ? "action" : "actions";
                    updatedHeaderLine2 += $"  •  {actionCount} {actionText}";
                }
                updatedHeaderLine2 += "[/]";

                headerControl.SetContent(new List<string>
                {
                    $"[cyan1 bold]{newData.Title}[/]",
                    updatedHeaderLine2
                });
            }

            // Update widget content (use captured reference to widgetPanel)
            renderer.UpdateWidgetPanel(widgetPanel, newData, maxLines: null, showTruncationIndicator: false);

            // Update actions list only if actions changed (better UX - avoid flicker)
            var actionsChanged = false;

            // Quick check: count different
            if (actionsList.Items.Count != newData.Actions.Count)
            {
                actionsChanged = true;
            }
            else
            {
                // Deep check: compare each action
                for (int i = 0; i < newData.Actions.Count; i++)
                {
                    var currentAction = actionsList.Items[i].Tag as WidgetAction;
                    var newAction = newData.Actions[i];

                    if (currentAction == null ||
                        currentAction.Label != newAction.Label ||
                        currentAction.Command != newAction.Command ||
                        currentAction.IsDanger != newAction.IsDanger)
                    {
                        actionsChanged = true;
                        break;
                    }
                }
            }

            if (actionsChanged)
            {
                actionsList.ClearItems();
                foreach (var action in newData.Actions)
                {
                    var label = action.Label;
                    if (action.IsDanger)
                    {
                        var padding = new string(' ', Math.Max(0, 50 - action.Label.Length));
                        label = $"[yellow]{action.Label}[/]{padding}⚠";
                    }
                    var item = new ListItem(label) { Tag = action };
                    actionsList.AddItem(item);
                }

                // Update visibility of actions section
                separatorColumn.Visible = newData.HasActions;
                actionsSeparator.Visible = newData.HasActions;
                actionsColumn.Visible = newData.HasActions;
                actionsHeader.Visible = newData.HasActions;
                actionsList.Visible = newData.HasActions;
                // Content column automatically stretches to fill remaining space
            }
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

    /// <summary>
    /// Executes a widget action with confirmation and result display
    /// </summary>
    private static async void ExecuteAction(
        WidgetAction action,
        ConsoleWindowSystem windowSystem,
        Window parentModal,
        Action? onRefreshRequested)
    {
        // Show confirmation dialog
        ActionConfirmationDialog.Show(
            action,
            windowSystem,
            parentModal,
            onConfirm: async () =>
            {
                // Create cancellation token source
                var cts = new CancellationTokenSource();

                // Show progress dialog (child of expansion dialog, non-closable)
                var progressModal = ActionProgressDialog.Show(
                    action,
                    windowSystem,
                    parentModal,
                    onTerminate: () =>
                    {
                        // Terminate execution (SIGTERM → SIGKILL)
                        cts.Cancel();
                    },
                    maxTimeout: 60);

                // Execute the action with progress updates and termination callbacks
                var executor = new Services.ActionExecutor();
                var result = await executor.ExecuteAsync(
                    action,
                    cts.Token,
                    onProgressUpdate: (elapsedSeconds) =>
                    {
                        // Update timer and progress bar every second
                        ActionProgressDialog.UpdateTimer(progressModal, elapsedSeconds, 60);
                        ActionProgressDialog.UpdateProgress(progressModal, elapsedSeconds, 60);
                    },
                    onGracefulTerminate: () =>
                    {
                        // Show terminating status (SIGTERM sent)
                        ActionProgressDialog.ShowTerminating(progressModal);
                    },
                    onForceKill: () =>
                    {
                        // Show force killing status (SIGKILL sent)
                        ActionProgressDialog.ShowForceKilling(progressModal);
                    });

                // Update final status
                if (result.Stderr.Contains("terminated", StringComparison.OrdinalIgnoreCase))
                {
                    ActionProgressDialog.UpdateStatus(progressModal, "Terminated", Color.Red);
                    await Task.Delay(500); // Brief pause to show status
                }
                else if (result.IsSuccess)
                {
                    ActionProgressDialog.UpdateStatus(progressModal, "Completed", Color.Green);
                    await Task.Delay(300); // Brief pause to show status
                }
                else
                {
                    ActionProgressDialog.UpdateStatus(progressModal, "Failed", Color.Red);
                    await Task.Delay(500); // Brief pause to show status
                }

                // Close progress dialog
                progressModal.Close();

                // Show result dialog (only if not terminated)
                if (!result.Stderr.Contains("terminated", StringComparison.OrdinalIgnoreCase))
                {
                    ActionResultDialog.Show(
                        action,
                        result,
                        windowSystem,
                        parentModal,
                        onClose: () =>
                        {
                            // If action succeeded and refresh flag is set, trigger widget refresh
                            if (result.IsSuccess && action.RefreshAfterSuccess)
                            {
                                onRefreshRequested?.Invoke();
                            }
                        });
                }
            },
            onCancel: () =>
            {
                // User cancelled, do nothing
            });
    }
}
