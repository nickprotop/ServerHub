// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Models;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using Spectre.Console;

namespace ServerHub.UI;

/// <summary>
/// Dialog displaying action execution results
/// </summary>
public static class ActionResultDialog
{
    /// <summary>
    /// Shows execution results in a modal dialog
    /// </summary>
    /// <param name="action">Action that was executed</param>
    /// <param name="result">Execution result</param>
    /// <param name="windowSystem">Console window system</param>
    /// <param name="parentWindow">Parent window (typically the expansion dialog)</param>
    /// <param name="onClose">Callback when dialog closes</param>
    public static void Show(
        WidgetAction action,
        ActionResult result,
        ConsoleWindowSystem windowSystem,
        Window? parentWindow = null,
        Action? onClose = null)
    {
        // Calculate modal size
        int modalWidth = Math.Min(90, Console.WindowWidth - 10);
        int modalHeight = Math.Min(35, Console.WindowHeight - 5);

        // Create borderless modal (AgentStudio style)
        var builder = new WindowBuilder(windowSystem)
            .WithTitle("Action Result")
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

        // Header - Action name
        modal.AddControl(Controls.Markup()
            .AddLine($"[cyan1 bold]Action:[/] {action.Label}")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 1, 1, 0)
            .Build());

        // Separator
        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .Build());

        // Status line: Exit code and duration
        var exitCodeColor = result.IsSuccess ? "green" : "red";
        var exitCodeText = result.IsSuccess ? "Success" : "Failed";
        var statusLine = $"[grey70]Exit Code:[/] [{exitCodeColor}]{result.ExitCode}[/] [{exitCodeColor}]({exitCodeText})[/]" +
                        $"    [grey70]Duration:[/] [cyan1]{result.Duration.TotalSeconds:F1}s[/]";

        modal.AddControl(Controls.Markup()
            .AddLine(statusLine)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        // Output section
        if (result.HasOutput || !result.IsSuccess)
        {
            modal.AddControl(Controls.Markup()
                .AddLine("")
                .AddLine("[cyan1]Output:[/]")
                .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
                .WithMargin(1, 0, 1, 0)
                .Build());

            var outputText = result.HasOutput ? result.Stdout : "[grey70](no output)[/]";
            var outputPanel = Controls.ScrollablePanel()
                .WithName("output_scroll")
                .WithVerticalScroll(ScrollMode.Scroll)
                .WithScrollbar(true)
                .WithScrollbarPosition(ScrollbarPosition.Right)
                .WithMouseWheel(true)
                .WithBackgroundColor(Color.Grey19)
                .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
                .AddControl(Controls.Markup()
                    .AddLine(outputText)
                    .WithMargin(1, 0, 1, 0)
                    .Build())
                .Build();

            modal.AddControl(outputPanel);
        }

        // Errors section (only if there are errors)
        if (result.HasErrors)
        {
            modal.AddControl(Controls.Markup()
                .AddLine("")
                .AddLine("[red]Errors:[/]")
                .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
                .WithMargin(1, 0, 1, 0)
                .Build());

            var errorPanel = Controls.ScrollablePanel()
                .WithName("error_scroll")
                .WithVerticalScroll(ScrollMode.Scroll)
                .WithScrollbar(true)
                .WithScrollbarPosition(ScrollbarPosition.Right)
                .WithMouseWheel(true)
                .WithBackgroundColor(Color.Grey19)
                .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
                .AddControl(Controls.Markup()
                    .AddLine($"[red]{result.Stderr}[/]")
                    .WithMargin(1, 0, 1, 0)
                    .Build())
                .Build();

            modal.AddControl(errorPanel);
        }

        // Spacing before button
        modal.AddControl(Controls.Markup()
            .AddLine("")
            .Build());

        // Close button
        var closeButton = Controls.Button(" Close ")
            .WithName("close_button")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .OnClick((s, e) =>
            {
                modal.Close();
                onClose?.Invoke();
            })
            .Build();

        modal.AddControl(closeButton);

        // Footer separator
        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .StickyBottom()
            .Build());

        // Footer instructions
        modal.AddControl(Controls.Markup()
            .AddLine("[grey70]Enter/Esc: Close  •  ↑↓/Mouse Wheel: Scroll[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(0, 0, 0, 0)
            .StickyBottom()
            .Build());

        // Keyboard shortcuts
        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Enter || e.KeyInfo.Key == ConsoleKey.Escape)
            {
                modal.Close();
                onClose?.Invoke();
                e.Handled = true;
            }
        };

        // Handle modal close
        modal.OnClosed += (s, e) =>
        {
            onClose?.Invoke();
        };

        // Show modal
        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);
        closeButton.SetFocus(true, FocusReason.Programmatic);
    }
}
