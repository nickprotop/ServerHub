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
    /// <param name="onCancel">Callback when user cancels</param>
    /// <returns>The modal window instance</returns>
    public static Window Show(
        WidgetAction action,
        ConsoleWindowSystem windowSystem,
        Window? parentWindow = null,
        Action? onCancel = null)
    {
        // Calculate modal size - compact progress dialog
        int modalWidth = Math.Min(80, Console.WindowWidth - 10);
        int modalHeight = 12;

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

        // Command display
        modal.AddControl(Controls.Markup()
            .AddLine("[grey70]Command:[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        modal.AddControl(Controls.Markup()
            .AddLine($"[cyan1]{action.Command}[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 1)
            .Build());

        // Cancel button
        var cancelButton = Controls.Button(" Cancel ")
            .WithName("cancel_button")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .OnClick((s, e) =>
            {
                onCancel?.Invoke();
                modal.Close();
            })
            .Build();

        modal.AddControl(cancelButton);

        // Footer separator
        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .StickyBottom()
            .Build());

        // Footer instructions
        modal.AddControl(Controls.Markup()
            .AddLine("[grey70]Click Cancel or press Esc to abort execution[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(0, 0, 0, 0)
            .StickyBottom()
            .Build());

        // Keyboard shortcuts
        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                onCancel?.Invoke();
                modal.Close();
                e.Handled = true;
            }
        };

        // Show modal
        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);
        cancelButton.SetFocus(true, FocusReason.Programmatic);

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
            var colorName = statusColor == Color.Green ? "green" : statusColor == Color.Red ? "red" : "cyan1";
            statusControl.SetContent(new List<string>
            {
                $"[{colorName}]●[/] [grey70]{status}[/]"
            });
        }
    }
}
