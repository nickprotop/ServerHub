// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Models;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using Spectre.Console;

namespace ServerHub.UI;

/// <summary>
/// Confirmation dialog for widget actions
/// Shows command details and gets user confirmation before execution
/// </summary>
public static class ActionConfirmationDialog
{
    /// <summary>
    /// Shows a confirmation dialog for an action
    /// </summary>
    /// <param name="action">Action to confirm</param>
    /// <param name="windowSystem">Console window system</param>
    /// <param name="parentWindow">Parent window (typically the expansion dialog)</param>
    /// <param name="onConfirm">Callback when user confirms (Execute clicked)</param>
    /// <param name="onCancel">Callback when user cancels</param>
    public static void Show(
        WidgetAction action,
        ConsoleWindowSystem windowSystem,
        Window? parentWindow = null,
        Action? onConfirm = null,
        Action? onCancel = null)
    {
        // Calculate modal size - compact confirmation dialog
        int modalWidth = Math.Min(70, Console.WindowWidth - 10);
        int modalHeight = action.IsDanger ? 18 : 15;  // Taller if danger warning shown

        // Create borderless modal (AgentStudio style)
        var builder = new WindowBuilder(windowSystem)
            .WithTitle("Confirm Action")
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
            .AddLine($"[bold]Action:[/] {actionLabel}")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 1, 1, 0)
            .Build());

        // Separator
        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .Build());

        // Command display (code block style)
        modal.AddControl(Controls.Markup()
            .AddLine("[grey70]Command to execute:[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        modal.AddControl(Controls.Markup()
            .AddLine($"[cyan1]{action.Command}[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        // Danger warning if applicable
        if (action.IsDanger)
        {
            modal.AddControl(Controls.Markup()
                .AddLine("")
                .AddLine("[yellow on red] ⚠ WARNING: This action may be destructive [/]")
                .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
                .WithMargin(0, 1, 0, 1)
                .Build());
        }

        // Spacing before buttons
        modal.AddControl(Controls.Markup()
            .AddLine("")
            .Build());

        // Buttons (Execute and Cancel)
        var executeButton = Controls.Button(" Execute ")
            .WithName("execute_button")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .OnClick((s, e) =>
            {
                modal.Close();
                onConfirm?.Invoke();
            })
            .Build();

        var cancelButton = Controls.Button(" Cancel ")
            .WithName("cancel_button")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(2, 0, 0, 0)
            .OnClick((s, e) =>
            {
                modal.Close();
                onCancel?.Invoke();
            })
            .Build();

        // Add buttons in a horizontal grid
        var buttonGrid = Controls.HorizontalGrid().Build();
        var leftColumn = new ColumnContainer(buttonGrid);
        leftColumn.AddContent(executeButton);
        buttonGrid.AddColumn(leftColumn);

        var rightColumn = new ColumnContainer(buttonGrid);
        rightColumn.AddContent(cancelButton);
        buttonGrid.AddColumn(rightColumn);

        modal.AddControl(buttonGrid);

        // Footer separator
        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .StickyBottom()
            .Build());

        // Footer instructions
        modal.AddControl(Controls.Markup()
            .AddLine("[grey70]Enter: Execute  •  Esc: Cancel  •  Tab: Switch Button[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(0, 0, 0, 0)
            .StickyBottom()
            .Build());

        // Keyboard shortcuts
        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Enter)
            {
                modal.Close();
                onConfirm?.Invoke();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                modal.Close();
                onCancel?.Invoke();
                e.Handled = true;
            }
        };

        // Show modal
        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);
        executeButton.SetFocus(true, FocusReason.Programmatic);  // Focus Execute by default
    }
}
