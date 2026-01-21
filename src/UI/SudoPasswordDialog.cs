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
/// UAC-style password dialog for sudo authentication.
/// Minimizes all windows and shows a dramatic centered prompt.
/// Single attempt - user must retry by clicking Execute again if password is wrong.
/// </summary>
public static class SudoPasswordDialog
{
    /// <summary>
    /// Indicates whether the sudo password dialog is currently open.
    /// Used to prevent background widget refresh during password entry.
    /// </summary>
    public static bool IsOpen { get; private set; }

    /// <summary>
    /// Result of the password dialog
    /// </summary>
    public class PasswordResult
    {
        public bool Success { get; init; }
        public string? Password { get; init; }
        public bool Cancelled { get; init; }
    }

    /// <summary>
    /// Shows the sudo password dialog with UAC-style presentation.
    /// Minimizes all windows and restores them on completion.
    /// </summary>
    /// <param name="action">The action requiring sudo</param>
    /// <param name="windowSystem">Console window system</param>
    /// <param name="onResult">Callback with the result (password or cancellation)</param>
    public static void Show(
        WidgetAction action,
        ConsoleWindowSystem windowSystem,
        Action<PasswordResult> onResult)
    {
        // Store minimized state of all windows to restore later
        var windowStates = new Dictionary<Window, WindowState>();
        foreach (var window in windowSystem.Windows.Values.ToList())
        {
            windowStates[window] = window.State;
            if (window.State != WindowState.Minimized)
            {
                window.Minimize(force: true);  // UAC-style: minimize ALL windows
            }
        }

        // Calculate modal size - compact and centered
        int modalWidth = 60;
        int modalHeight = 19;

        var modal = new WindowBuilder(windowSystem)
            .WithSize(modalWidth, modalHeight)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.DoubleLine)
            .WithBorderColor(Color.Orange1)
            .HideTitle()
            .Resizable(false)
            .Movable(false)
            .Minimizable(false)
            .Maximizable(false)
            .WithColors(Color.Grey11, Color.Grey93)
            .Build();

        // Mark dialog as open to prevent widget refresh during password entry
        IsOpen = true;

        // Track state
        var dialogComplete = false;
        PasswordResult? result = null;

        // Header with lock icon
        modal.AddControl(Controls.Markup()
            .AddLine("")
            .AddLine("[bold orange1]\U0001F512 Sudo Authentication Required[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .Build());

        // Command being executed
        modal.AddControl(Controls.Markup()
            .WithName("command_info")
            .AddLine("")
            .AddLine("[grey70]Action:[/]")
            .AddLine($"[white]{Markup.Escape(action.Label)}[/]")
            .AddLine("")
            .AddLine("[grey70]Command:[/]")
            .AddLine($"[grey58]{Markup.Escape(action.Command)}[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .Build());

        // Error message area (initially hidden)
        var errorMessage = Controls.Markup()
            .WithName("error_message")
            .AddLine("")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .Build();
        errorMessage.Visible = false;
        modal.AddControl(errorMessage);

        // Password prompt
        modal.AddControl(Controls.Markup()
            .AddLine("")
            .Build());

        var passwordPrompt = new PromptControl();
        passwordPrompt.Name = "password_input";
        passwordPrompt.Prompt = "  Password: ";
        passwordPrompt.MaskCharacter = '\u2022';
        passwordPrompt.InputWidth = 30;
        passwordPrompt.InputBackgroundColor = Color.Grey19;
        passwordPrompt.InputFocusedBackgroundColor = Color.Grey23;
        passwordPrompt.UnfocusOnEnter = false;
        passwordPrompt.HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment.Center;
        modal.AddControl(passwordPrompt);

        // Disclaimer
        modal.AddControl(Controls.Markup()
            .AddLine("")
            .AddLine("[grey50]Password is used once and immediately discarded.[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .Build());

        // Buttons - use ButtonRow factory for automatic centering
        var authenticateButton = Controls.Button(" Authenticate ")
            .WithName("btn_auth")
            .Build();
        var cancelButton = Controls.Button("   Cancel   ")
            .WithName("btn_cancel")
            .WithMargin(2, 0, 0, 0)
            .Build();

        var buttonGrid = HorizontalGridControl.ButtonRow(authenticateButton, cancelButton);
        buttonGrid.Name = "buttons";

        modal.AddControl(Controls.Markup().AddLine("").Build());
        modal.AddControl(buttonGrid);

        // Footer
        modal.AddControl(Controls.Markup()
            .WithName("footer")
            .AddLine("")
            .AddLine("[grey50]Enter: Authenticate  \u2022  Esc: Cancel[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .StickyBottom()
            .Build());

        // Restore windows helper
        void RestoreWindows()
        {
            foreach (var kvp in windowStates)
            {
                if (kvp.Value != WindowState.Minimized) // Was not minimized before
                {
                    kvp.Key.Restore();
                }
            }
        }

        // Complete dialog helper
        void CompleteDialog(PasswordResult dialogResult)
        {
            if (dialogComplete) return;
            dialogComplete = true;
            result = dialogResult;

            // Clear password from control
            passwordPrompt.Input = "";

            modal.Close();
        }

        // Authenticate helper - return whatever user entered, let executor handle it
        void DoAuthenticate()
        {
            var password = passwordPrompt.Input;
            CompleteDialog(new PasswordResult { Success = true, Password = password });
        }

        // Button handlers
        authenticateButton.Click += (s, e) => DoAuthenticate();
        cancelButton.Click += (s, e) => CompleteDialog(new PasswordResult { Success = false, Cancelled = true });

        // Keyboard shortcuts
        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Enter)
            {
                DoAuthenticate();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                CompleteDialog(new PasswordResult { Success = false, Cancelled = true });
                e.Handled = true;
            }
        };

        // Handle close
        modal.OnClosed += (s, e) =>
        {
            // Mark dialog as closed - allows widget refresh to resume
            IsOpen = false;

            RestoreWindows();

            if (result == null)
            {
                result = new PasswordResult { Success = false, Cancelled = true };
            }

            onResult(result);
        };

        // Show and focus
        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);
        passwordPrompt.SetFocus(true, FocusReason.Programmatic);
    }

}
