// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Models;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using Spectre.Console;

namespace ServerHub.UI;

/// <summary>
/// Dialog showing action execution progress with cancel support
/// </summary>
public static class ActionProgressDialog
{
    /// <summary>
    /// Shows execution progress in a modal dialog
    /// </summary>
    /// <param name="action">Action being executed</param>
    /// <param name="windowSystem">Console window system</param>
    /// <param name="parentWindow">Parent window (typically the expansion dialog)</param>
    /// <param name="onTerminate">Callback when user terminates execution</param>
    /// <param name="maxTimeout">Maximum execution timeout in seconds</param>
    /// <returns>The modal window instance</returns>
    public static Window Show(
        WidgetAction action,
        ConsoleWindowSystem windowSystem,
        Window? parentWindow = null,
        Action? onTerminate = null,
        int maxTimeout = 60)
    {
        // Calculate modal size - slightly taller for timer and progress bar
        int modalWidth = Math.Min(80, Console.WindowWidth - 10);
        int modalHeight = 15;

        // Create borderless modal (AgentStudio style)
        var builder = new WindowBuilder(windowSystem)
            .WithTitle("Executing Action")
            .WithSize(modalWidth, modalHeight)
            .Centered()
            .AsModal()
            .Borderless()
            .Resizable(false)
            .Movable(false)
            .WithColors(Color.Grey15, Color.Grey93);

        // Set parent window if provided (modal-on-modal)
        if (parentWindow != null)
        {
            builder = builder.WithParent(parentWindow);
        }

        var modal = builder.Build();

        // Header
        var actionLabel = action.IsDanger
            ? $"[yellow]{action.Label}[/]  [red]⚠[/]"
            : $"[cyan1]{action.Label}[/]";

        modal.AddControl(Controls.Markup()
            .AddLine($"[bold]Executing:[/] {actionLabel}")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 1, 1, 0)
            .Build());

        // Separator
        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .Build());

        // Status indicator
        modal.AddControl(Controls.Markup()
            .WithName("progress_status")
            .AddLine("[cyan1]●[/] [grey70]Running...[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        // Timer display
        modal.AddControl(Controls.Markup()
            .WithName("progress_timer")
            .AddLine($"[grey70]Elapsed: [cyan1]0s[/] / {maxTimeout}s[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        // Progress bar (text-based)
        modal.AddControl(Controls.Markup()
            .WithName("progress_bar")
            .AddLine("[grey23]━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        // Command display
        modal.AddControl(Controls.Markup()
            .AddLine("[grey70]Command:[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        modal.AddControl(Controls.Markup()
            .AddLine($"[cyan1]{action.Command}[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        // Spacing before button
        modal.AddControl(Controls.Markup()
            .AddLine("")
            .Build());

        // Terminate button (NOT cancel - actually terminates the process)
        var terminateButton = Controls.Button(" Terminate Action ")
            .WithName("terminate_button")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .OnClick((s, e) =>
            {
                // Disable button to prevent multiple clicks
                ((ButtonControl)s!).IsEnabled = false;
                onTerminate?.Invoke();
            })
            .Build();

        modal.AddControl(terminateButton);

        // Footer separator
        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .StickyBottom()
            .Build());

        // Footer instructions (non-closable - only terminate button works)
        modal.AddControl(Controls.Markup()
            .AddLine("[grey70]Click Terminate or press Enter to abort execution[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(0, 0, 0, 0)
            .StickyBottom()
            .Build());

        // Keyboard shortcut - Enter to terminate (NO Esc - non-closable)
        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Enter)
            {
                // Trigger terminate button click
                var button = modal.FindControl<ButtonControl>("terminate_button");
                if (button != null && button.IsEnabled)
                {
                    button.IsEnabled = false;
                    onTerminate?.Invoke();
                }
                e.Handled = true;
            }
        };

        // Show modal
        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);
        terminateButton.SetFocus(true, FocusReason.Programmatic);

        return modal;
    }

    /// <summary>
    /// Updates the progress status text
    /// </summary>
    public static void UpdateStatus(Window modal, string status, Color statusColor)
    {
        var statusControl = modal.FindControl<MarkupControl>("progress_status");
        if (statusControl != null)
        {
            var colorName = statusColor == Color.Green ? "green" :
                           statusColor == Color.Red ? "red" :
                           statusColor == Color.Yellow ? "yellow" : "cyan1";
            statusControl.SetContent(new List<string>
            {
                $"[{colorName}]●[/] [grey70]{status}[/]"
            });
        }
    }

    /// <summary>
    /// Updates the timer display
    /// </summary>
    public static void UpdateTimer(Window modal, int elapsedSeconds, int maxTimeout)
    {
        var timerControl = modal.FindControl<MarkupControl>("progress_timer");
        if (timerControl != null)
        {
            var remaining = maxTimeout - elapsedSeconds;
            var color = remaining <= 10 ? "red" : remaining <= 30 ? "yellow" : "cyan1";
            timerControl.SetContent(new List<string>
            {
                $"[grey70]Elapsed: [{color}]{elapsedSeconds}s[/] / {maxTimeout}s[/]"
            });
        }
    }

    /// <summary>
    /// Updates the progress bar value (text-based progress bar)
    /// </summary>
    public static void UpdateProgress(Window modal, int elapsedSeconds, int maxTimeout)
    {
        var progressBar = modal.FindControl<MarkupControl>("progress_bar");
        if (progressBar != null)
        {
            // Calculate progress percentage
            var percentage = (double)elapsedSeconds / maxTimeout;
            var barWidth = 50; // Total bar width in characters
            var filledWidth = (int)(barWidth * percentage);

            // Build progress bar with filled and empty sections
            var filled = new string('━', Math.Max(0, filledWidth));
            var empty = new string('━', Math.Max(0, barWidth - filledWidth));

            // Color based on time remaining
            var remaining = maxTimeout - elapsedSeconds;
            var color = remaining <= 10 ? "red" : remaining <= 30 ? "yellow" : "cyan1";

            progressBar.SetContent(new List<string>
            {
                $"[{color}]{filled}[/][grey23]{empty}[/]"
            });
        }
    }

    /// <summary>
    /// Shows terminating status (SIGTERM sent, waiting for graceful shutdown)
    /// </summary>
    public static void ShowTerminating(Window modal)
    {
        UpdateStatus(modal, "Terminating gracefully (SIGTERM)...", Color.Yellow);

        // Disable terminate button
        var button = modal.FindControl<ButtonControl>("terminate_button");
        if (button != null)
        {
            button.IsEnabled = false;
        }
    }

    /// <summary>
    /// Shows force killing status (SIGKILL sent after grace period)
    /// </summary>
    public static void ShowForceKilling(Window modal)
    {
        UpdateStatus(modal, "Force killing (SIGKILL)...", Color.Red);
    }
}
