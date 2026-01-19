// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Models;
using ServerHub.Services;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using Spectre.Console;

namespace ServerHub.UI;

/// <summary>
/// Handles displaying full widget content in a modal expansion dialog.
/// Self-contained: owns the widget while open and manages its own refresh loop.
/// </summary>
public static class WidgetExpansionDialog
{
    private static readonly string[] SpinnerFrames = { "◐", "◓", "◑", "◒" };

    /// <summary>
    /// Shows a modal dialog with the full widget content (AgentStudio-inspired design).
    /// The modal owns this widget while open and handles its own refresh with --extended data.
    /// </summary>
    /// <param name="widgetId">Widget identifier</param>
    /// <param name="widgetData">Initial widget data to display (from main dialog's cache)</param>
    /// <param name="windowSystem">Console window system</param>
    /// <param name="renderer">Widget renderer for creating content</param>
    /// <param name="refreshService">Service to handle widget refresh</param>
    /// <param name="onClose">Callback when modal closes</param>
    public static void Show(
        string widgetId,
        WidgetData widgetData,
        ConsoleWindowSystem windowSystem,
        WidgetRenderer renderer,
        WidgetRefreshService refreshService,
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

        // Get refresh interval from service
        int refreshInterval = refreshService.GetRefreshInterval(widgetId);

        // Header section (AgentStudio pattern) - show loading initially
        var headerLine2 = $"[grey50]Refresh: {refreshInterval}s  •  Loading extended data...[/]";

        var headerControl = Controls.Markup()
            .WithName("modal_header")
            .AddLine($"[cyan1 bold]{widgetData.Title}[/]")
            .AddLine(headerLine2)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build();
        modal.AddControl(headerControl);

        // Separator rule
        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .Build());

        // Loading panel (shown while fetching initial extended data)
        var loadingPanel = Controls.Markup()
            .WithName("loading_panel")
            .AddLine("")
            .AddLine("")
            .AddLine("[grey50]Loading extended data...[/]")
            .AddLine("")
            .AddLine("[cyan1]━━━━━━━━━━[/][grey23]━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .Build();
        loadingPanel.Visible = true;
        modal.AddControl(loadingPanel);

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

        // Create side-by-side layout: Content (left) + Actions (right)
        var mainGrid = Controls.HorizontalGrid()
            .WithName("main_grid")
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .Build();
        mainGrid.Visible = false;  // Hide until extended data arrives

        // LEFT COLUMN - Content
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
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .Build();

        // Populate initial actions
        PopulateActionsList(actionsList, widgetData.Actions);

        actionsColumn.AddContent(actionsList);
        actionsColumn.BackgroundColor = Color.Grey19;
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

        // State for refresh loop
        var cts = new CancellationTokenSource();
        var currentData = widgetData;
        var isRefreshing = false;

        // Create update context to share state with refresh loop
        var updateContext = new ModalUpdateContext
        {
            HeaderControl = headerControl,
            LoadingPanel = loadingPanel,
            MainGrid = mainGrid,
            WidgetPanel = widgetPanel,
            ActionsList = actionsList,
            SeparatorColumn = separatorColumn,
            ActionsSeparator = actionsSeparator,
            ActionsColumn = actionsColumn,
            ActionsHeader = actionsHeader,
            RefreshInterval = refreshInterval,
            Renderer = renderer,
            CurrentTitle = widgetData.Title,
            LastUpdated = widgetData.Timestamp,
            ActionCount = widgetData.Actions.Count
        };

        // Handle action activation (Enter or double-click)
        actionsList.ItemActivated += (s, item) =>
        {
            if (item?.Tag is WidgetAction selectedAction)
            {
                ExecuteAction(
                    selectedAction,
                    windowSystem,
                    modal,
                    onRefreshRequested: async () =>
                    {
                        // Trigger immediate refresh with extended data
                        isRefreshing = true;
                        var data = await refreshService.RefreshAsync(widgetId, extended: true);
                        currentData = data;
                        UpdateModalContent(updateContext, data);
                        isRefreshing = false;
                    });
            }
        };

        // Handle keyboard shortcuts (AgentStudio pattern)
        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                modal.Close();
                e.Handled = true;
            }
            // Only close on Enter if there are NO actions (content-only mode)
            else if (e.KeyInfo.Key == ConsoleKey.Enter && !currentData.HasActions)
            {
                modal.Close();
                e.Handled = true;
            }
            // Quick action execution via number keys (1-9)
            else if (currentData.HasActions && e.KeyInfo.Key >= ConsoleKey.D1 && e.KeyInfo.Key <= ConsoleKey.D9)
            {
                var index = e.KeyInfo.Key - ConsoleKey.D1;
                if (index < currentData.Actions.Count)
                {
                    var selectedAction = currentData.Actions[index];
                    ExecuteAction(
                        selectedAction,
                        windowSystem,
                        modal,
                        onRefreshRequested: async () =>
                        {
                            isRefreshing = true;
                            var data = await refreshService.RefreshAsync(widgetId, extended: true);
                            currentData = data;
                            UpdateModalContent(updateContext, data);
                            isRefreshing = false;
                        });
                    e.Handled = true;
                }
            }
        };

        // Handle modal close
        modal.OnClosed += (s, e) =>
        {
            cts.Cancel();
            onClose?.Invoke();
        };

        // Start self-contained refresh loop
        _ = RunRefreshLoopAsync(
            widgetId,
            refreshService,
            updateContext,
            refreshInterval,
            () => isRefreshing,
            (refreshing) => isRefreshing = refreshing,
            (data) => currentData = data,
            cts.Token);

        // Show modal
        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);
        scrollPanel.SetFocus(true, FocusReason.Programmatic);
    }

    /// <summary>
    /// Context containing all UI elements that need to be updated
    /// </summary>
    private class ModalUpdateContext
    {
        public MarkupControl? HeaderControl { get; init; }
        public MarkupControl? LoadingPanel { get; init; }
        public HorizontalGridControl? MainGrid { get; init; }
        public IWindowControl? WidgetPanel { get; init; }
        public ListControl? ActionsList { get; init; }
        public ColumnContainer? SeparatorColumn { get; init; }
        public SeparatorControl? ActionsSeparator { get; init; }
        public ColumnContainer? ActionsColumn { get; init; }
        public MarkupControl? ActionsHeader { get; init; }
        public int RefreshInterval { get; init; }
        public WidgetRenderer? Renderer { get; init; }
        public string CurrentTitle { get; set; } = "";
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public int ActionCount { get; set; }
    }

    /// <summary>
    /// Runs the self-contained refresh loop for the modal.
    /// First fetches extended data immediately, then refreshes at the widget's interval.
    /// </summary>
    private static async Task RunRefreshLoopAsync(
        string widgetId,
        WidgetRefreshService refreshService,
        ModalUpdateContext context,
        int refreshInterval,
        Func<bool> getIsRefreshing,
        Action<bool> setIsRefreshing,
        Action<WidgetData> setCurrentData,
        CancellationToken ct)
    {
        int spinnerFrame = 0;
        bool firstLoad = true;
        bool isCurrentlyRefreshing = false;

        // Start spinner animation task that runs continuously
        var spinnerTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (isCurrentlyRefreshing)
                    {
                        UpdateHeaderSpinner(context, spinnerFrame, true);
                    }
                    spinnerFrame++;
                    await Task.Delay(250, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Mark as refreshing
                isCurrentlyRefreshing = true;
                setIsRefreshing(true);

                // Fetch extended data (spinner animates concurrently)
                var newData = await refreshService.RefreshAsync(widgetId, extended: true);
                setCurrentData(newData);

                // Mark as not refreshing
                isCurrentlyRefreshing = false;
                setIsRefreshing(false);

                // Update modal content
                UpdateModalContent(context, newData);

                // On first load, hide loading panel and show content
                if (firstLoad && context.LoadingPanel != null && context.MainGrid != null)
                {
                    context.LoadingPanel.Visible = false;
                    context.MainGrid.Visible = true;
                    firstLoad = false;
                }

                // Wait for next refresh interval
                await Task.Delay(refreshInterval * 1000, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when modal closes
        }

        // Wait for spinner task to complete
        try
        {
            await spinnerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    /// <summary>
    /// Updates the modal header with spinner during refresh
    /// </summary>
    private static void UpdateHeaderSpinner(ModalUpdateContext context, int spinnerFrame, bool showSpinner)
    {
        if (context.HeaderControl == null)
            return;

        var title = showSpinner
            ? $"[cyan1 bold]{context.CurrentTitle} {SpinnerFrames[spinnerFrame % 4]}[/]"
            : $"[cyan1 bold]{context.CurrentTitle}[/]";

        // Build header line 2 from cached values
        var headerLine2 = $"[grey50]Last updated: {context.LastUpdated:yyyy-MM-dd HH:mm:ss}  •  Refresh: {context.RefreshInterval}s";
        if (context.ActionCount > 0)
        {
            var actionText = context.ActionCount == 1 ? "action" : "actions";
            headerLine2 += $"  •  {context.ActionCount} {actionText}";
        }
        headerLine2 += "[/]";

        context.HeaderControl.SetContent(new List<string>
        {
            title,
            headerLine2
        });
    }

    /// <summary>
    /// Updates the modal header with timestamp and optional spinner
    /// </summary>
    private static void UpdateHeader(ModalUpdateContext context, WidgetData data, bool isRefreshing, int spinnerFrame)
    {
        if (context.HeaderControl == null)
            return;

        var title = isRefreshing
            ? $"[cyan1 bold]{data.Title} {SpinnerFrames[spinnerFrame % 4]}[/]"
            : $"[cyan1 bold]{data.Title}[/]";

        var headerLine2 = BuildHeaderLine2(data, context.RefreshInterval);

        context.HeaderControl.SetContent(new List<string>
        {
            title,
            headerLine2
        });
    }

    /// <summary>
    /// Updates all modal content with new widget data
    /// </summary>
    private static void UpdateModalContent(ModalUpdateContext context, WidgetData newData)
    {
        if (context.HeaderControl == null || context.Renderer == null)
            return;

        // Update context tracking fields
        context.CurrentTitle = newData.Title;
        context.LastUpdated = newData.Timestamp;
        context.ActionCount = newData.Actions.Count;

        // Update header
        UpdateHeader(context, newData, false, 0);

        // Update widget content
        if (context.WidgetPanel != null)
        {
            context.Renderer.UpdateWidgetPanel(context.WidgetPanel, newData, maxLines: null, showTruncationIndicator: false);
        }

        // Update actions list only if actions changed
        if (context.ActionsList != null)
        {
            var actionsChanged = HasActionsChanged(context.ActionsList, newData.Actions);

            if (actionsChanged)
            {
                context.ActionsList.ClearItems();
                PopulateActionsList(context.ActionsList, newData.Actions);

                // Update visibility of actions section
                if (context.SeparatorColumn != null)
                    context.SeparatorColumn.Visible = newData.HasActions;
                if (context.ActionsSeparator != null)
                    context.ActionsSeparator.Visible = newData.HasActions;
                if (context.ActionsColumn != null)
                    context.ActionsColumn.Visible = newData.HasActions;
                if (context.ActionsHeader != null)
                    context.ActionsHeader.Visible = newData.HasActions;
                context.ActionsList.Visible = newData.HasActions;
            }
        }
    }

    /// <summary>
    /// Checks if actions have changed
    /// </summary>
    private static bool HasActionsChanged(ListControl actionsList, List<WidgetAction> newActions)
    {
        if (actionsList.Items.Count != newActions.Count)
            return true;

        for (int i = 0; i < newActions.Count; i++)
        {
            var currentAction = actionsList.Items[i].Tag as WidgetAction;
            var newAction = newActions[i];

            if (currentAction == null ||
                currentAction.Label != newAction.Label ||
                currentAction.Command != newAction.Command ||
                currentAction.IsDanger != newAction.IsDanger)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Populates the actions list with action items
    /// </summary>
    private static void PopulateActionsList(ListControl actionsList, List<WidgetAction> actions)
    {
        foreach (var action in actions)
        {
            var label = action.Label;
            if (action.IsDanger)
            {
                var padding = new string(' ', Math.Max(0, 30 - action.Label.Length));
                label = $"[yellow]{action.Label}[/]{padding}⚠";
            }
            var item = new ListItem(label) { Tag = action };
            actionsList.AddItem(item);
        }
    }

    /// <summary>
    /// Builds the second line of the header with timestamp and action count
    /// </summary>
    private static string BuildHeaderLine2(WidgetData widgetData, int refreshInterval)
    {
        var headerLine2 = $"[grey50]Last updated: {widgetData.Timestamp:yyyy-MM-dd HH:mm:ss}  •  Refresh: {refreshInterval}s";
        if (widgetData.HasActions)
        {
            var actionCount = widgetData.Actions.Count;
            var actionText = actionCount == 1 ? "action" : "actions";
            headerLine2 += $"  •  {actionCount} {actionText}";
        }
        headerLine2 += "[/]";
        return headerLine2;
    }

    /// <summary>
    /// Executes a widget action with confirmation and result display
    /// </summary>
    private static async void ExecuteAction(
        WidgetAction action,
        ConsoleWindowSystem windowSystem,
        Window parentModal,
        Func<Task>? onRefreshRequested)
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
                        // Terminate execution (SIGTERM -> SIGKILL)
                        cts.Cancel();
                    },
                    maxTimeout: 60);

                // Execute the action with progress updates and termination callbacks
                var executor = new ActionExecutor();
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
