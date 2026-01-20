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
    private const int MaxTimeout = 60;

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

        // Progress/status bar - shows hint in confirm, progress in running, result in finished
        modal.AddControl(Controls.Markup()
            .WithName("dialog_progress")
            .AddLine("[grey50]Press Enter to execute[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
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
            .Build();
        closeButton.Visible = false;

        // Button grid for confirm state (Execute + Cancel)
        var confirmButtonGrid = Controls.HorizontalGrid()
            .WithName("confirm_buttons")
            .Build();
        var leftCol = new ColumnContainer(confirmButtonGrid);
        leftCol.AddContent(executeButton);
        confirmButtonGrid.AddColumn(leftCol);
        var rightCol = new ColumnContainer(confirmButtonGrid);
        rightCol.AddContent(cancelButton);
        confirmButtonGrid.AddColumn(rightCol);

        modal.AddControl(confirmButtonGrid);
        modal.AddControl(terminateButton);
        modal.AddControl(closeButton);

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

            // Transition to running state
            TransitionToRunning(modal, action);

            // Execute the action - ActionExecutor handles sudo checking and command wrapping
            var executor = new ActionExecutor();
            var result = await executor.ExecuteAsync(
                action,
                cts.Token,
                sudoPassword: sudoPassword,
                stdinInput: null,
                onProgressUpdate: (elapsedSeconds) => UpdateProgress(modal, elapsedSeconds, MaxTimeout),
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
        void HandleExecute()
        {
            // Simple guard: Don't do anything if dialog already closed
            if (_disposedDialogs.Contains(modal))
                return;

            if (action.RequiresSudo)
            {
                // Show password dialog immediately
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
                    modal.Close();
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

        // Show timer
        var timer = modal.FindControl<MarkupControl>("dialog_timer");
        if (timer != null)
        {
            timer.SetContent(new List<string> { $"[grey70]Elapsed: [cyan1]0s[/] / {MaxTimeout}s[/]" });
        }

        // Show progress bar
        var progress = modal.FindControl<MarkupControl>("dialog_progress");
        if (progress != null)
        {
            var emptyBar = new string('\u2501', 50);
            progress.SetContent(new List<string> { $"[grey23]{emptyBar}[/]" });
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

        // Update output placeholder
        var outputContent = modal.FindControl<MarkupControl>("dialog_output_content");
        if (outputContent != null)
        {
            outputContent.SetContent(new List<string> { "[grey50](waiting for output...)[/]" });
        }

        // Switch buttons: hide confirm buttons, show terminate
        var confirmButtons = modal.FindControl<HorizontalGridControl>("confirm_buttons");
        if (confirmButtons != null)
        {
            confirmButtons.Visible = false;
        }

        var terminateButton = modal.FindControl<ButtonControl>("btn_terminate");
        if (terminateButton != null)
        {
            terminateButton.Visible = true;
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
        // Update timer
        var timer = modal.FindControl<MarkupControl>("dialog_timer");
        if (timer != null)
        {
            var remaining = maxTimeout - elapsedSeconds;
            var color = remaining <= 10 ? "red" : remaining <= 30 ? "yellow" : "cyan1";
            timer.SetContent(new List<string> { $"[grey70]Elapsed: [{color}]{elapsedSeconds}s[/] / {maxTimeout}s[/]" });
        }

        // Update progress bar
        var progress = modal.FindControl<MarkupControl>("dialog_progress");
        if (progress != null)
        {
            var percentage = (double)elapsedSeconds / maxTimeout;
            var barWidth = 50;
            var filledWidth = (int)(barWidth * percentage);

            var filled = new string('\u2501', Math.Max(0, filledWidth));
            var empty = new string('\u2501', Math.Max(0, barWidth - filledWidth));

            var remaining = maxTimeout - elapsedSeconds;
            var color = remaining <= 10 ? "red" : remaining <= 30 ? "yellow" : "cyan1";

            progress.SetContent(new List<string> { $"[{color}]{filled}[/][grey23]{empty}[/]" });
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

        var progress = modal.FindControl<MarkupControl>("dialog_progress");
        if (progress != null)
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

            progress.SetContent(new List<string> { statusText });
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

        // Switch buttons: hide terminate, show close
        var terminateButton = modal.FindControl<ButtonControl>("btn_terminate");
        if (terminateButton != null)
        {
            terminateButton.Visible = false;
        }

        var closeButton = modal.FindControl<ButtonControl>("btn_close");
        if (closeButton != null)
        {
            closeButton.Visible = true;
            closeButton.SetFocus(true, FocusReason.Programmatic);
        }

        // Update footer
        var footer = modal.FindControl<MarkupControl>("dialog_footer");
        if (footer != null)
        {
            footer.SetContent(new List<string> { "[grey70]Enter/Esc: Close  \u2022  \u2191\u2193/Mouse Wheel: Scroll[/]" });
        }
    }
}
