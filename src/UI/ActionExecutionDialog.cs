// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Models;
using ServerHub.Services;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using Spectre.Console;

namespace ServerHub.UI;

/// <summary>
/// Unified dialog for action confirmation, execution, and results.
/// Evolves through states: Confirm → Running → Finished
/// For sudo actions, shows SudoPasswordDialog first.
/// </summary>
public static class ActionExecutionDialog
{
    // Output mode: true = add new control per line (uses AutoScroll), false = update single control content
    private const bool AddControlsPerLine = true;

    // Store output lines for each dialog instance (used when AddControlsPerLine = false)
    private static readonly Dictionary<Window, List<string>> _outputLines = new();

    // Track if first output received (to clear placeholder)
    private static readonly HashSet<Window> _hasReceivedOutput = new();

    // Track disposal state per dialog (to prevent orphaned callbacks)
    private static readonly HashSet<Window> _disposedDialogs = new();

    /// <summary>
    /// Shows the unified action dialog starting in confirm state.
    /// Handles the full lifecycle: confirm → execute → show results
    /// </summary>
    /// <param name="action">Action to execute</param>
    /// <param name="windowSystem">Console window system</param>
    /// <param name="parentWindow">Parent window (typically the expansion dialog)</param>
    /// <param name="onComplete">Callback when dialog closes after execution (includes result)</param>
    /// <param name="onCancel">Callback when user cancels before execution</param>
    public static void Show(
        WidgetAction action,
        ConsoleWindowSystem windowSystem,
        Window? parentWindow = null,
        Action<ActionResult>? onComplete = null,
        Action? onCancel = null)
    {
        // Calculate modal size: 90% of screen with reasonable max constraints
        int screenWidth = Console.WindowWidth;
        int screenHeight = Console.WindowHeight;

        int modalWidth = Math.Min((int)(screenWidth * 0.9), 150);
        int modalHeight = Math.Min((int)(screenHeight * 0.9), 40);

        // Create modal with subtle single border
        var builder = new WindowBuilder(windowSystem)
            .WithSize(modalWidth, modalHeight)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Grey35)
            .HideTitle()
            .Resizable(false)
            .Movable(false)
            .Minimizable(false)
            .Maximizable(false)
            .WithColors(Color.Grey15, Color.Grey93);

        if (parentWindow != null)
        {
            builder = builder.WithParent(parentWindow);
        }

        var modal = builder.Build();

        // Initialize output lines storage for this dialog
        _outputLines[modal] = new List<string>();

        // Track execution result for onComplete callback
        ActionResult? executionResult = null;

        // Track sudo password (set by SudoPasswordDialog if needed)
        string? sudoPassword = null;

        // Header - shows action name and state
        var actionLabel = action.IsDanger
            ? $"[yellow]{action.Label}[/]  [red]\u26a0[/]"
            : $"[cyan1]{action.Label}[/]";

        modal.AddControl(Controls.Markup()
            .WithName("dialog_header")
            .AddLine($"[bold]Execute:[/] {actionLabel}")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        // Header separator
        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .Build());

        // Status area - shows command in confirm state, progress in running state, result in finished
        modal.AddControl(Controls.Markup()
            .WithName("dialog_status")
            .AddLine("[grey70]Command to execute:[/]")
            .AddLine($"[cyan1]{Markup.Escape(action.Command)}[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        // Status line - universal across all states
        modal.AddControl(Controls.Markup()
            .WithName("dialog_timer")
            .AddLine("")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        // Progress hint - shows hint in confirm state, hidden during running
        modal.AddControl(Controls.Markup()
            .WithName("dialog_progress_hint")
            .AddLine("[grey50]Press Enter to execute[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        // Progress bar - hidden in confirm state, visible during running
        var progressBar = Controls.ProgressBar()
            .WithName("dialog_progress_bar")
            .Stretch()
            .WithMargin(1, 0, 1, 0)
            .Visible(false)
            .Build();
        modal.AddControl(progressBar);

        // Progress status - shows completion status in finished state
        modal.AddControl(Controls.Markup()
            .WithName("dialog_progress_status")
            .AddLine("")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Visible(false)
            .Build());

        // Danger warning if applicable (shown in confirm state)
        var dangerWarning = Controls.Markup()
            .WithName("dialog_danger")
            .AddLine("")
            .AddLine(action.IsDanger ? "[white on red3] \u26a0 WARNING: This action may be destructive [/]" : "")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(0, 0, 0, 0)
            .Build();
        dangerWarning.Visible = action.IsDanger;
        modal.AddControl(dangerWarning);

        // Sudo hint if applicable (shown in confirm state)
        var sudoHint = Controls.Markup()
            .WithName("dialog_sudo")
            .AddLine("")
            .AddLine(action.RequiresSudo ? "[orange1 on grey23] \U0001F512 This action requires elevated privileges (sudo) [/]" : "")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(0, 0, 0, 0)
            .Build();
        sudoHint.Visible = action.RequiresSudo;
        modal.AddControl(sudoHint);

        // Timeout hint if applicable (shown in confirm state for infinite timeout)
        var timeoutHint = Controls.Markup()
            .WithName("dialog_timeout")
            .AddLine("")
            .AddLine(action.HasNoTimeout ? "[orange1 on grey23] \u23f1 This action has no timeout limit [/]" : "")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(0, 0, 0, 0)
            .Build();
        timeoutHint.Visible = action.HasNoTimeout;
        modal.AddControl(timeoutHint);

        // Output section header
        modal.AddControl(Controls.Markup()
            .WithName("dialog_output_header")
            .AddLine("")
            .AddLine("[cyan1]Output:[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        // Scrollable output panel
        var outputMarkup = Controls.Markup()
            .WithName("dialog_output_content")
            .AddLine("[grey50](output will appear here when execution starts)[/]")
            .WithMargin(1, 0, 1, 0)
            .Build();

        var outputPanel = Controls.ScrollablePanel()
            .WithName("dialog_output_panel")
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithScrollbarPosition(ScrollbarPosition.Right)
            .WithMouseWheel(true)
            .WithAutoScroll(true)
            .WithBackgroundColor(Color.Grey19)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .AddControl(outputMarkup)
            .Build();

        modal.AddControl(outputPanel);

        // Spacing before buttons
        modal.AddControl(Controls.Markup()
            .AddLine("")
            .Build());

        // Button area - changes based on state
        var executeButton = Controls.Button(" Execute ")
            .WithName("btn_execute")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .Build();

        var cancelButton = Controls.Button(" Cancel ")
            .WithName("btn_cancel")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(2, 0, 0, 0)
            .Build();

        var terminateButton = Controls.Button(" Terminate ")
            .WithName("btn_terminate")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .Build();
        terminateButton.Visible = false;

        var closeButton = Controls.Button("  Close  ")
            .WithName("btn_close")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(2, 0, 0, 0)
            .Build();
        closeButton.Visible = false;

        var retryButton = Controls.Button("  Retry  ")
            .WithName("btn_retry")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .Build();
        retryButton.Visible = false;

        // Button grid for confirm state (Execute + Cancel) - centered
        var confirmButtonGrid = HorizontalGridControl.ButtonRow(executeButton, cancelButton);
        confirmButtonGrid.Name = "confirm_buttons";

        // Button grid for finished-failed state (Retry + Close) - centered
        var finishedButtonGrid = HorizontalGridControl.ButtonRow(retryButton, closeButton);
        finishedButtonGrid.Name = "finished_buttons";
        finishedButtonGrid.Visible = false;

        modal.AddControl(confirmButtonGrid);
        modal.AddControl(terminateButton);
        modal.AddControl(finishedButtonGrid);

        // Footer separator
        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .StickyBottom()
            .Build());

        // Footer instructions
        modal.AddControl(Controls.Markup()
            .WithName("dialog_footer")
            .AddLine("[grey70]Enter: Execute  \u2022  Esc: Cancel  \u2022  Tab: Switch Button[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(0, 0, 0, 0)
            .StickyBottom()
            .Build());

        // Cancellation token for execution
        var cts = new CancellationTokenSource();

        // Track dialog state: "confirm", "running", "finished"
        var dialogState = "confirm";

        // Execute action handler (called after password is obtained for sudo actions)
        async void StartExecution()
        {
            dialogState = "running";

            // Get the effective timeout for this action
            var effectiveTimeout = action.EffectiveTimeout;

            // Transition to running state
            TransitionToRunning(modal, action);

            // Execute the action - ActionExecutor handles sudo checking and command wrapping
            var executor = new ActionExecutor();
            var result = await executor.ExecuteAsync(
                action,
                cts.Token,
                sudoPassword: sudoPassword,
                stdinInput: null,
                timeoutSeconds: effectiveTimeout,
                onProgressUpdate: (elapsedSeconds) => UpdateProgress(modal, elapsedSeconds, effectiveTimeout),
                onOutputReceived: (line) => AppendOutput(modal, line),
                onErrorReceived: (line) => AppendError(modal, line),
                onGracefulTerminate: () => ShowTerminating(modal),
                onForceKill: () => ShowForceKilling(modal));

            // Clear password after execution
            sudoPassword = null;

            executionResult = result;
            dialogState = "finished";

            // Transition to finished state
            TransitionToFinished(modal, result);
        }

        // Handler for Execute button - shows password dialog if sudo required
        async void HandleExecute()
        {
            // Simple guard: Don't do anything if dialog already closed
            if (_disposedDialogs.Contains(modal))
                return;

            if (action.RequiresSudo)
            {
                // First check if sudo credentials are already cached
                var executor = new ActionExecutor();
                bool sudoCached = await executor.CheckSudoCachedAsync();

                if (sudoCached)
                {
                    // Credentials are cached, no password needed
                    StartExecution();
                }
                else
                {
                    // Show password dialog
                    SudoPasswordDialog.Show(action, windowSystem, (passwordResult) =>
                    {
                        // Simple guard: Don't execute if dialog was closed
                        if (_disposedDialogs.Contains(modal))
                            return;

                        if (passwordResult.Success)
                        {
                            sudoPassword = passwordResult.Password;
                            StartExecution();
                        }
                        // If cancelled - just stay in confirm state
                    });
                }
            }
            else
            {
                StartExecution();
            }
        }

        // Button click handlers
        executeButton.Click += (s, e) => HandleExecute();

        cancelButton.Click += (s, e) => modal.Close();

        terminateButton.Click += (s, e) =>
        {
            if (terminateButton.IsEnabled)
            {
                terminateButton.IsEnabled = false;
                cts.Cancel();
            }
        };

        closeButton.Click += (s, e) => modal.Close();

        retryButton.Click += (s, e) =>
        {
            // Reset for re-execution
            sudoPassword = null;
            cts = new CancellationTokenSource();

            // Clear previous output
            ClearOutput(modal);

            // Execute again (HandleExecute will check sudo cache and prompt if needed)
            HandleExecute();
        };

        // Keyboard shortcuts
        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Enter)
            {
                if (dialogState == "confirm")
                {
                    HandleExecute();
                }
                else if (dialogState == "running" && terminateButton.IsEnabled)
                {
                    terminateButton.IsEnabled = false;
                    cts.Cancel();
                }
                else if (dialogState == "finished")
                {
                    // Retry on Enter
                    sudoPassword = null;
                    cts = new CancellationTokenSource();
                    ClearOutput(modal);
                    HandleExecute();
                }
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                if (dialogState == "confirm" || dialogState == "finished")
                {
                    modal.Close();
                }
                // Don't allow Esc during running state
                e.Handled = true;
            }
        };

        // Handle window close button (X) - prevent close during running state
        modal.OnClosing += (s, e) =>
        {
            if (dialogState == "running")
            {
                // Don't allow closing during execution - must use Terminate
                e.Allow = false;
                windowSystem.NotificationStateService.ShowNotification(
                    "Cannot Close",
                    "Action is still running. Use Terminate to stop execution.",
                    NotificationSeverity.Warning,
                    timeout: 3000);
            }
        };

        // Handle modal close - cleanup and callbacks
        modal.OnClosed += (s, e) =>
        {
            // Mark as disposed to prevent orphaned callbacks
            _disposedDialogs.Add(modal);

            // Cleanup tracking dictionaries
            _outputLines.Remove(modal);
            _hasReceivedOutput.Remove(modal);
            // DON'T remove from _disposedDialogs - keep flag to prevent orphaned callbacks

            if (dialogState == "confirm")
            {
                // Closed during confirm state = cancelled
                onCancel?.Invoke();
            }
            else if (executionResult != null)
            {
                // Closed after execution = completed
                onComplete?.Invoke(executionResult);
            }
        };

        // Show modal
        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);
        executeButton.SetFocus(true, FocusReason.Programmatic);
    }

    /// <summary>
    /// Transitions from confirm state to running state
    /// </summary>
    private static void TransitionToRunning(Window modal, WidgetAction action)
    {
        // Update header
        var header = modal.FindControl<MarkupControl>("dialog_header");
        if (header != null)
        {
            var actionLabel = action.IsDanger
                ? $"[yellow]{action.Label}[/]  [red]\u26a0[/]"
                : $"[cyan1]{action.Label}[/]";
            header.SetContent(new List<string> { $"[bold]Executing:[/] {actionLabel}" });
        }

        // Update status to show running indicator
        var status = modal.FindControl<MarkupControl>("dialog_status");
        if (status != null)
        {
            status.SetContent(new List<string> { "[cyan1]\u25cf[/] [grey70]Running...[/]" });
        }

        // Show timer - display differently for infinite timeout
        var timer = modal.FindControl<MarkupControl>("dialog_timer");
        if (timer != null)
        {
            if (action.HasNoTimeout)
            {
                timer.SetContent(new List<string> { "[grey70]Elapsed: [cyan1]0s[/][/]" });
            }
            else
            {
                timer.SetContent(new List<string> { $"[grey70]Elapsed: [cyan1]0s[/] / {action.EffectiveTimeout}s[/]" });
            }
        }

        // Hide progress hint
        var progressHint = modal.FindControl<MarkupControl>("dialog_progress_hint");
        if (progressHint != null)
        {
            progressHint.Visible = false;
        }

        // Show and configure progress bar
        var progressBar = modal.FindControl<ProgressBarControl>("dialog_progress_bar");
        if (progressBar != null)
        {
            progressBar.Visible = true;
            progressBar.Value = 0;

            if (action.HasNoTimeout)
            {
                // Indeterminate mode for infinite timeout - control handles animation
                progressBar.IsIndeterminate = true;
            }
            else
            {
                // Determinate mode - we'll update Value in UpdateProgress
                progressBar.IsIndeterminate = false;
                progressBar.MaxValue = action.EffectiveTimeout;
            }
        }

        // Hide danger warning
        var danger = modal.FindControl<MarkupControl>("dialog_danger");
        if (danger != null)
        {
            danger.Visible = false;
        }

        // Hide sudo hint
        var sudo = modal.FindControl<MarkupControl>("dialog_sudo");
        if (sudo != null)
        {
            sudo.Visible = false;
        }

        // Hide timeout hint
        var timeout = modal.FindControl<MarkupControl>("dialog_timeout");
        if (timeout != null)
        {
            timeout.Visible = false;
        }

        // Update output placeholder
        var outputContent = modal.FindControl<MarkupControl>("dialog_output_content");
        if (outputContent != null)
        {
            outputContent.SetContent(new List<string> { "[grey50](waiting for output...)[/]" });
        }

        // Switch buttons: hide confirm/finished buttons, show terminate
        var confirmButtons = modal.FindControl<HorizontalGridControl>("confirm_buttons");
        if (confirmButtons != null)
        {
            confirmButtons.Visible = false;
        }

        var finishedButtons = modal.FindControl<HorizontalGridControl>("finished_buttons");
        if (finishedButtons != null)
        {
            finishedButtons.Visible = false;
        }

        // Hide retry and close buttons individually too (they might be visible from a previous run)
        var retryButton = modal.FindControl<ButtonControl>("btn_retry");
        if (retryButton != null)
        {
            retryButton.Visible = false;
        }

        var closeButton = modal.FindControl<ButtonControl>("btn_close");
        if (closeButton != null)
        {
            closeButton.Visible = false;
        }

        var terminateButton = modal.FindControl<ButtonControl>("btn_terminate");
        if (terminateButton != null)
        {
            terminateButton.Visible = true;
            terminateButton.IsEnabled = true;
            terminateButton.SetFocus(true, FocusReason.Programmatic);
        }

        // Update footer
        var footer = modal.FindControl<MarkupControl>("dialog_footer");
        if (footer != null)
        {
            footer.SetContent(new List<string> { "[grey70]Enter: Terminate  \u2022  Output streams live below[/]" });
        }
    }

    /// <summary>
    /// Updates the progress bar and timer
    /// </summary>
    private static void UpdateProgress(Window modal, int elapsedSeconds, int maxTimeout)
    {
        // Check if this is an infinite timeout (0)
        var hasNoTimeout = maxTimeout == 0;

        // Update timer
        var timer = modal.FindControl<MarkupControl>("dialog_timer");
        if (timer != null)
        {
            if (hasNoTimeout)
            {
                // Infinite timeout: just show elapsed time
                timer.SetContent(new List<string> { $"[grey70]Elapsed: [cyan1]{elapsedSeconds}s[/][/]" });
            }
            else
            {
                // Finite timeout: show elapsed / max with color warning
                var remaining = maxTimeout - elapsedSeconds;
                var color = remaining <= 10 ? "red" : remaining <= 30 ? "yellow" : "cyan1";
                timer.SetContent(new List<string> { $"[grey70]Elapsed: [{color}]{elapsedSeconds}s[/] / {maxTimeout}s[/]" });
            }
        }

        // Update progress bar value (indeterminate mode handles its own animation)
        var progressBar = modal.FindControl<ProgressBarControl>("dialog_progress_bar");
        if (progressBar != null && !hasNoTimeout)
        {
            // Determinate mode: update value and color based on remaining time
            progressBar.Value = elapsedSeconds;

            // Change color based on remaining time
            var remaining = maxTimeout - elapsedSeconds;
            if (remaining <= 10)
            {
                progressBar.FilledColor = Color.Red;
            }
            else if (remaining <= 30)
            {
                progressBar.FilledColor = Color.Yellow;
            }
            else
            {
                progressBar.FilledColor = Color.Cyan1;
            }
        }
    }

    /// <summary>
    /// Appends a stdout line to the output panel
    /// </summary>
    private static void AppendOutput(Window modal, string line)
    {
        AppendToOutput(modal, line, isError: false);
    }

    /// <summary>
    /// Appends a stderr line to the output panel (in red)
    /// </summary>
    private static void AppendError(Window modal, string line)
    {
        AppendToOutput(modal, line, isError: true);
    }

    /// <summary>
    /// Clears all output from the output panel for retry
    /// </summary>
    private static void ClearOutput(Window modal)
    {
        var scrollPanel = modal.FindControl<ScrollablePanelControl>("dialog_output_panel");
        if (scrollPanel == null) return;

        // Remove tracking for this modal
        _hasReceivedOutput.Remove(modal);
        _outputLines.Remove(modal);

        // Clear all controls from scroll panel
        foreach (var child in scrollPanel.Children.ToList())
        {
            scrollPanel.RemoveControl(child);
        }

        // Re-add the placeholder
        var placeholder = new MarkupControl(new List<string> { "[grey50](waiting for output...)[/]" });
        placeholder.Name = "dialog_output_content";
        placeholder.Margin = new Margin(1, 0, 1, 0);
        scrollPanel.AddControl(placeholder);

        // Reset progress controls for retry
        var progressHint = modal.FindControl<MarkupControl>("dialog_progress_hint");
        if (progressHint != null)
        {
            progressHint.Visible = true;
        }

        var progressBar = modal.FindControl<ProgressBarControl>("dialog_progress_bar");
        if (progressBar != null)
        {
            progressBar.IsIndeterminate = false;
            progressBar.Visible = false;
            progressBar.Value = 0;
            progressBar.FilledColor = Color.Cyan1; // Reset color
        }

        var progressStatus = modal.FindControl<MarkupControl>("dialog_progress_status");
        if (progressStatus != null)
        {
            progressStatus.Visible = false;
        }
    }

    private static void AppendToOutput(Window modal, string line, bool isError)
    {
        // Format line (escape any markup in the actual output)
        var escapedLine = Markup.Escape(line);
        var formattedLine = isError ? $"[red]{escapedLine}[/]" : escapedLine;

        var scrollPanel = modal.FindControl<ScrollablePanelControl>("dialog_output_panel");

        if (AddControlsPerLine)
        {
            // Mode: Add new control per line (AutoScroll handles scrolling)
            if (!_hasReceivedOutput.Contains(modal))
            {
                _hasReceivedOutput.Add(modal);

                // Remove placeholder on first output
                var placeholder = modal.FindControl<MarkupControl>("dialog_output_content");
                if (placeholder != null)
                {
                    scrollPanel?.RemoveControl(placeholder);
                }
            }

            // Add new MarkupControl for this line
            var lineControl = new MarkupControl(new List<string> { formattedLine });
            lineControl.Margin = new Margin(1, 0, 1, 0);
            scrollPanel?.AddControl(lineControl);
        }
        else
        {
#pragma warning disable CS0162 // Unreachable code detected - kept for alternative output mode
            // Mode: Update single control content (manual scroll)
            if (!_outputLines.TryGetValue(modal, out var lines))
            {
                return;
            }

            lines.Add(formattedLine);

            var outputContent = modal.FindControl<MarkupControl>("dialog_output_content");
            if (outputContent != null)
            {
                outputContent.SetContent(lines);
            }

            scrollPanel?.ScrollToBottom();
#pragma warning restore CS0162
        }
    }

    /// <summary>
    /// Shows terminating status (SIGTERM sent)
    /// </summary>
    private static void ShowTerminating(Window modal)
    {
        var status = modal.FindControl<MarkupControl>("dialog_status");
        if (status != null)
        {
            status.SetContent(new List<string> { "[yellow]\u25cf[/] [grey70]Terminating gracefully (SIGTERM)...[/]" });
        }

        var terminateButton = modal.FindControl<ButtonControl>("btn_terminate");
        if (terminateButton != null)
        {
            terminateButton.IsEnabled = false;
        }
    }

    /// <summary>
    /// Shows force killing status (SIGKILL sent)
    /// </summary>
    private static void ShowForceKilling(Window modal)
    {
        var status = modal.FindControl<MarkupControl>("dialog_status");
        if (status != null)
        {
            status.SetContent(new List<string> { "[red]\u25cf[/] [grey70]Force killing (SIGKILL)...[/]" });
        }
    }

    /// <summary>
    /// Transitions from running state to finished state
    /// </summary>
    private static void TransitionToFinished(Window modal, ActionResult result)
    {
        // Update header to show "Finished"
        var header = modal.FindControl<MarkupControl>("dialog_header");
        if (header != null)
        {
            header.SetContent(new List<string> { "[bold]Finished[/]" });
        }

        // Update status to show exit code
        var status = modal.FindControl<MarkupControl>("dialog_status");
        if (status != null)
        {
            var exitCodeColor = result.IsSuccess ? "green" : "red";
            var exitCodeText = result.IsSuccess ? "Success" : "Failed";
            status.SetContent(new List<string>
            {
                $"[grey70]Exit Code:[/] [{exitCodeColor}]{result.ExitCode}[/] [{exitCodeColor}]({exitCodeText})[/]"
            });
        }

        // Update timer and progress to show final status
        var timer = modal.FindControl<MarkupControl>("dialog_timer");
        if (timer != null)
        {
            timer.SetContent(new List<string> { "" });
        }

        // Hide progress bar and stop any animation
        var progressBar = modal.FindControl<ProgressBarControl>("dialog_progress_bar");
        if (progressBar != null)
        {
            progressBar.IsIndeterminate = false; // Stop animation timer
            progressBar.Visible = false;
        }

        // Show completion status
        var progressStatus = modal.FindControl<MarkupControl>("dialog_progress_status");
        if (progressStatus != null)
        {
            var duration = result.Duration.TotalSeconds;
            string statusText;

            if (result.Stderr.Contains("terminated", StringComparison.OrdinalIgnoreCase))
            {
                statusText = $"[yellow]\u2717 Terminated after {duration:F1}s[/]";
            }
            else if (result.IsSuccess)
            {
                statusText = $"[green]\u2713 Completed in {duration:F1}s[/]";
            }
            else
            {
                statusText = $"[red]\u2717 Failed after {duration:F1}s[/]";
            }

            progressStatus.SetContent(new List<string> { statusText });
            progressStatus.Visible = true;
        }

        // Check if output was streamed
        bool hasOutput = AddControlsPerLine
            ? _hasReceivedOutput.Contains(modal)
            : _outputLines.TryGetValue(modal, out var lines) && lines.Count > 0;

        // If no output was streamed, show result output
        if (!hasOutput)
        {
            if (result.HasOutput)
            {
                foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    AppendOutput(modal, line.TrimEnd('\r'));
                }
            }

            if (result.HasErrors)
            {
                foreach (var line in result.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    AppendError(modal, line.TrimEnd('\r'));
                }
            }
        }

        // Re-check if we have output now
        hasOutput = AddControlsPerLine
            ? _hasReceivedOutput.Contains(modal)
            : _outputLines.TryGetValue(modal, out var outputLines) && outputLines.Count > 0;

        // If still no output, show message
        if (!hasOutput)
        {
            var outputContent = modal.FindControl<MarkupControl>("dialog_output_content");
            if (outputContent != null)
            {
                var message = result.IsSuccess
                    ? "[grey50](command completed with no output)[/]"
                    : $"[grey50](command failed with no output, exit code: {result.ExitCode})[/]";
                outputContent.SetContent(new List<string> { message });
            }
        }

        // Switch buttons: hide terminate, show finished buttons (Retry + Close)
        var terminateButton = modal.FindControl<ButtonControl>("btn_terminate");
        if (terminateButton != null)
        {
            terminateButton.Visible = false;
        }

        var finishedButtons = modal.FindControl<HorizontalGridControl>("finished_buttons");
        if (finishedButtons != null)
        {
            finishedButtons.Visible = true;
        }

        var retryButton = modal.FindControl<ButtonControl>("btn_retry");
        if (retryButton != null)
        {
            retryButton.Visible = true;
            retryButton.SetFocus(true, FocusReason.Programmatic);
        }

        var closeButton = modal.FindControl<ButtonControl>("btn_close");
        if (closeButton != null)
        {
            closeButton.Visible = true;
        }

        // Update footer
        var footer = modal.FindControl<MarkupControl>("dialog_footer");
        if (footer != null)
        {
            footer.SetContent(new List<string> { "[grey70]Enter: Retry  \u2022  Esc: Close  \u2022  Tab: Switch  \u2022  \u2191\u2193: Scroll[/]" });
        }
    }
}
