// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Models;
using ServerHub.Services;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using Spectre.Console;
using Point = System.Drawing.Point;

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
    /// <param name="onMainWidgetRefresh">Callback to refresh main dashboard widget immediately after action completion</param>
    public static void Show(
        string widgetId,
        WidgetData widgetData,
        ConsoleWindowSystem windowSystem,
        WidgetRenderer renderer,
        WidgetRefreshService refreshService,
        Action? onClose = null,
        Func<Task>? onMainWidgetRefresh = null)
    {
        // Calculate modal size: 90% of screen with reasonable max constraints
        int screenWidth = Console.WindowWidth;
        int screenHeight = Console.WindowHeight;

        int modalWidth = Math.Min((int)(screenWidth * 0.9), 150);   // 90% of screen, max 150
        int modalHeight = Math.Min((int)(screenHeight * 0.9), 40);  // 90% of screen, max 40

        // Create modal with subtle single border, no title
        var modal = new WindowBuilder(windowSystem)
            .WithSize(modalWidth, modalHeight)
            .Centered()                         // Call AFTER WithSize for correct centering
            .AsModal()                          // Block input to main window
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Grey35)      // Subtle border matching aesthetic
            .HideTitle()                        // No title bar
            .Resizable(true)                    // User can resize
            .Movable(true)                      // User can drag
            .Minimizable(false)                 // No minimize button
            .Maximizable(true)                  // User can maximize
            .WithColors(Color.Grey15, Color.Grey93)  // Dark bg, light text
            .Build();

        // Get refresh interval from service
        int refreshInterval = refreshService.GetExpandedRefreshInterval(widgetId);

        // Header section with refresh button (hybrid approach)
        var headerLine2 = $"[grey50]Refresh: {refreshInterval}s  •  Loading extended data...[/]";

        // HEADER GRID: Text (left) + Button (right)
        var headerGrid = Controls.HorizontalGrid()
            .WithName("header_grid")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .WithMargin(1, 0, 1, 0)
            .Build();

        // LEFT COLUMN - Title and metadata (fills available space)
        var headerTextColumn = new ColumnContainer(headerGrid)
        {
            VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Top
        };

        var headerControl = Controls.Markup()
            .WithName("modal_header_text")
            .AddLine($"[cyan1 bold]{widgetData.Title}[/]")
            .AddLine(headerLine2)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .Build();

        headerTextColumn.AddContent(headerControl);
        headerGrid.AddColumn(headerTextColumn);

        // RIGHT COLUMN - Refresh button (fixed width, hidden on narrow terminals or fast auto-refresh)
        // Hide refresh button for widgets with refresh < 15s (manual refresh is pointless)
        bool showRefreshButton = modalWidth >= 60 && refreshInterval >= 15;

        var headerButtonColumn = new ColumnContainer(headerGrid)
        {
            Width = 18,  // Fixed width to accommodate focus indicators: "> ↻  Refresh <"
            VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Top,
            Visible = showRefreshButton
        };

        ButtonControl? refreshButton = null;

        // Create refresh button (click handler will be wired up after state initialization)
        if (headerButtonColumn.Visible)
        {
            refreshButton = Controls.Button(" ↻  Refresh ")
                .Build();
            headerButtonColumn.AddContent(refreshButton);
        }

        headerGrid.AddColumn(headerButtonColumn);
        modal.AddControl(headerGrid);

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

        // Build content directly without panel wrapper (dialog already has border)
        var widgetPanel = Controls.Markup()
            .WithName("modal_widget")
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(1, 0, 1, 0)  // Left and right margins for padding
            .Build();

        // Populate with initial content
        UpdateWidgetContent(widgetPanel, widgetData, renderer);

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

        // Footer with instructions (AgentStudio pattern) - hide F5 refresh hint for fast auto-refresh
        var footerInstructions = widgetData.HasActions
            ? (showRefreshButton
                ? "[grey70]↑↓/Click: Select  •  Enter/Dbl-click: Execute  •  F5/↻: Refresh  •  Esc: Close[/]"
                : "[grey70]↑↓/Click: Select  •  Enter/Dbl-click: Execute  •  Esc: Close[/]")
            : (showRefreshButton
                ? "[grey70]↑↓/Mouse Wheel: Scroll  •  F5/↻: Refresh  •  Esc/Enter: Close[/]"
                : "[grey70]↑↓/Mouse Wheel: Scroll  •  Esc/Enter: Close[/]");

        modal.AddControl(Controls.Markup()
            .AddLine(footerInstructions)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(0, 0, 0, 0)
            .StickyBottom()
            .Build());

        // State for refresh loop - use wrapper class for shared state
        var cts = new CancellationTokenSource();
        var currentData = widgetData;
        var refreshState = new RefreshState();
        Timer? autoRefreshTimer = null;

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
            RefreshButton = refreshButton,
            RefreshInterval = refreshInterval,
            Renderer = renderer,
            CurrentTitle = widgetData.Title,
            LastUpdated = widgetData.Timestamp,
            ActionCount = widgetData.Actions.Count
        };

        // Wire up refresh button click handler now that context exists
        if (refreshButton != null)
        {
            refreshButton.Click += (s, e) =>
            {
                if (!refreshState.IsRefreshing)
                {
                    _ = Task.Run(async () =>
                    {
                        // Stop timer before manual refresh
                        autoRefreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                        refreshState.IsRefreshing = true;
                        var data = await refreshService.RefreshAsync(widgetId, extended: true);
                        currentData = data;
                        UpdateModalContent(updateContext, data);
                        refreshState.IsRefreshing = false;

                        // Restart timer with fresh interval
                        autoRefreshTimer?.Change(
                            TimeSpan.FromSeconds(refreshInterval),
                            TimeSpan.FromSeconds(refreshInterval)
                        );
                    });
                }
            };
        }

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
                        // Stop timer before action refresh
                        autoRefreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                        // Trigger immediate refresh with extended data
                        refreshState.IsRefreshing = true;
                        var data = await refreshService.RefreshAsync(widgetId, extended: true);
                        currentData = data;
                        UpdateModalContent(updateContext, data);
                        refreshState.IsRefreshing = false;

                        // Restart timer with fresh interval
                        autoRefreshTimer?.Change(
                            TimeSpan.FromSeconds(refreshInterval),
                            TimeSpan.FromSeconds(refreshInterval)
                        );
                    },
                    onMainWidgetRefresh: onMainWidgetRefresh);
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
            // F5: Force refresh (only if manual refresh is enabled)
            else if (e.KeyInfo.Key == ConsoleKey.F5 && showRefreshButton)
            {
                if (!refreshState.IsRefreshing)
                {
                    _ = Task.Run(async () =>
                    {
                        // Stop timer before manual refresh
                        autoRefreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                        refreshState.IsRefreshing = true;
                        var data = await refreshService.RefreshAsync(widgetId, extended: true);
                        currentData = data;
                        UpdateModalContent(updateContext, data);
                        refreshState.IsRefreshing = false;

                        // Restart timer with fresh interval
                        autoRefreshTimer?.Change(
                            TimeSpan.FromSeconds(refreshInterval),
                            TimeSpan.FromSeconds(refreshInterval)
                        );
                    });
                }
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
                            // Stop timer before action refresh
                            autoRefreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                            refreshState.IsRefreshing = true;
                            var data = await refreshService.RefreshAsync(widgetId, extended: true);
                            currentData = data;
                            UpdateModalContent(updateContext, data);
                            refreshState.IsRefreshing = false;

                            // Restart timer with fresh interval
                            autoRefreshTimer?.Change(
                                TimeSpan.FromSeconds(refreshInterval),
                                TimeSpan.FromSeconds(refreshInterval)
                            );
                        },
                        onMainWidgetRefresh: onMainWidgetRefresh);
                    e.Handled = true;
                }
            }
        };

        // Subscribe to screen resize event to maintain centering and 90% sizing
        EventHandler<SharpConsoleUI.Helpers.Size>? resizeHandler = null;
        resizeHandler = (sender, size) =>
        {
            // Recalculate modal dimensions with 90% sizing and max constraints
            int newModalWidth = Math.Min((int)(size.Width * 0.9), 150);
            int newModalHeight = Math.Min((int)(size.Height * 0.9), 40);

            // Resize the modal
            modal.SetSize(newModalWidth, newModalHeight);

            // Re-center manually (no Center() method exists)
            int centerX = (size.Width - newModalWidth) / 2;
            int centerY = (size.Height - newModalHeight) / 2;
            modal.SetPosition(new Point(centerX, centerY));
        };
        windowSystem.ConsoleDriver.ScreenResized += resizeHandler;

        // Handle modal close
        modal.OnClosed += (s, e) =>
        {
            // Unsubscribe from resize event to prevent memory leaks
            windowSystem.ConsoleDriver.ScreenResized -= resizeHandler;

            cts.Cancel();
            onClose?.Invoke();
        };

        // Start self-contained refresh loop
        _ = RunRefreshLoopAsync(
            widgetId,
            refreshService,
            updateContext,
            refreshInterval,
            refreshState,
            (data) => currentData = data,
            cts.Token,
            (timer) => autoRefreshTimer = timer);

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
        public ButtonControl? RefreshButton { get; init; }
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
        RefreshState refreshState,
        Action<WidgetData> setCurrentData,
        CancellationToken ct,
        Action<Timer?> setAutoRefreshTimer)
    {
        int spinnerFrame = 0;
        bool firstLoad = true;
        Timer? timer = null;

        // Start spinner animation task that runs continuously
        var spinnerTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (refreshState.IsRefreshing)
                    {
                        UpdateHeaderSpinner(context, spinnerFrame, true);
                    }
                    else
                    {
                        UpdateHeaderSpinner(context, spinnerFrame, false);
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

        // Define refresh callback for timer
        void RefreshCallback(object? state)
        {
            // Defense check: prevent concurrent execution
            if (refreshState.IsRefreshing)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    // Mark as refreshing
                    refreshState.IsRefreshing = true;

                    // Fetch extended data (spinner animates concurrently)
                    var newData = await refreshService.RefreshAsync(widgetId, extended: true);
                    setCurrentData(newData);

                    // Mark as not refreshing
                    refreshState.IsRefreshing = false;

                    // Update modal content
                    UpdateModalContent(context, newData);

                    // On first load, hide loading panel and show content
                    if (firstLoad && context.LoadingPanel != null && context.MainGrid != null)
                    {
                        context.LoadingPanel.Visible = false;
                        context.MainGrid.Visible = true;
                        firstLoad = false;
                    }
                }
                catch (Exception)
                {
                    // Ensure flag is reset on error
                    refreshState.IsRefreshing = false;
                }
            });
        }

        try
        {
            // Immediate initial refresh
            refreshState.IsRefreshing = true;
            var initialData = await refreshService.RefreshAsync(widgetId, extended: true);
            setCurrentData(initialData);
            refreshState.IsRefreshing = false;
            UpdateModalContent(context, initialData);

            // On first load, hide loading panel and show content
            if (context.LoadingPanel != null && context.MainGrid != null)
            {
                context.LoadingPanel.Visible = false;
                context.MainGrid.Visible = true;
                firstLoad = false;
            }

            // Create timer for automatic refresh
            timer = new Timer(
                RefreshCallback,
                null,
                TimeSpan.FromSeconds(refreshInterval),
                TimeSpan.FromSeconds(refreshInterval)
            );

            // Expose timer to outer scope
            setAutoRefreshTimer(timer);

            // Wait for cancellation
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            // Expected when modal closes
        }
        finally
        {
            // Dispose timer
            timer?.Dispose();
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

        // Update refresh button state
        if (context.RefreshButton != null)
        {
            context.RefreshButton.IsEnabled = !showSpinner;
            context.RefreshButton.Text = showSpinner ? " Wait " : " ↻  Refresh ";
        }
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

        // Update refresh button state
        if (context.RefreshButton != null)
        {
            context.RefreshButton.IsEnabled = !isRefreshing;
            context.RefreshButton.Text = isRefreshing ? " Wait " : " ↻  Refresh ";
        }
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
        if (context.WidgetPanel != null && context.WidgetPanel is MarkupControl markup)
        {
            UpdateWidgetContent(markup, newData, context.Renderer!);
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
    /// Updates widget content directly in a MarkupControl (no panel wrapper)
    /// </summary>
    private static void UpdateWidgetContent(MarkupControl markup, WidgetData widgetData, WidgetRenderer renderer)
    {
        var lines = new List<string>();

        if (widgetData.HasError)
        {
            lines.Add($"[red]Error:[/] {widgetData.Error}");
        }
        else
        {
            foreach (var row in widgetData.Rows)
            {
                lines.Add(FormatRowForExpansion(row, renderer));
            }
        }

        // Add footer info
        lines.Add("");
        var infoLine = $"[grey70]Updated: {widgetData.Timestamp:HH:mm:ss}[/]";
        if (widgetData.HasActions)
        {
            var actionCount = widgetData.Actions.Count;
            var actionText = actionCount == 1 ? "action" : "actions";
            infoLine += $"  [grey70]•[/]  [cyan1]{actionCount} {actionText}[/]";
        }
        lines.Add(infoLine);

        // Expand embedded newlines into separate list items
        // This fixes ScrollablePanel height calculation
        var expandedLines = new List<string>();
        foreach (var line in lines)
        {
            if (line.Contains('\n'))
            {
                expandedLines.AddRange(line.Split('\n'));
            }
            else
            {
                expandedLines.Add(line);
            }
        }

        markup.SetContent(expandedLines);
    }

    /// <summary>
    /// Formats a row for expansion view (uses renderer's internal logic)
    /// </summary>
    private static string FormatRowForExpansion(WidgetRow row, WidgetRenderer renderer)
    {
        // Use reflection to access the private FormatRow method
        var method = typeof(WidgetRenderer).GetMethod("FormatRow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (method != null)
        {
            return (string?)method.Invoke(renderer, new object[] { row }) ?? "";
        }
        return row.Content;
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
            var icons = new List<string>();

            if (action.RequiresSudo)
            {
                icons.Add("!");
            }
            if (action.IsDanger)
            {
                icons.Add("⚠");
            }

            if (icons.Count > 0)
            {
                var iconStr = string.Join(" ", icons);
                var padding = new string(' ', Math.Max(0, 30 - action.Label.Length));
                var color = action.IsDanger ? "yellow" : "white";
                label = $"[{color}]{action.Label}[/]{padding}{iconStr}";
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
    private static void ExecuteAction(
        WidgetAction action,
        ConsoleWindowSystem windowSystem,
        Window parentModal,
        Func<Task>? onRefreshRequested,
        Func<Task>? onMainWidgetRefresh = null)
    {
        // Minimize parent expanded dialog to avoid resize/position conflicts with action executor
        // Use force=true since dialog is built with Minimizable(false)
        parentModal.Minimize(force: true);

        // Show unified dialog (confirm → execute → results in one dialog)
        ActionExecutionDialog.Show(
            action,
            windowSystem,
            parentModal,
            onComplete: (result) =>
            {
                // Restore parent expanded dialog
                parentModal.Restore();

                // If refresh flag is set, trigger widget refresh (on any completion: success, failure, or termination)
                if (action.RefreshAfterSuccess)
                {
                    // Refresh the expansion dialog content
                    onRefreshRequested?.Invoke();

                    // Refresh the main dashboard widget box
                    onMainWidgetRefresh?.Invoke();
                }
            },
            onCancel: () =>
            {
                // Restore parent expanded dialog
                parentModal.Restore();

                // User cancelled, do nothing
            });
    }

    /// <summary>
    /// Shared state wrapper for refresh status - allows passing by reference
    /// </summary>
    private class RefreshState
    {
        public bool IsRefreshing { get; set; }
    }
}
