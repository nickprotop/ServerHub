// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Marketplace.Models;
using ServerHub.Services;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using Spectre.Console;
using System.Diagnostics;

namespace ServerHub.UI;

/// <summary>
/// Warning dialog shown before installing unverified widgets
/// </summary>
public static class UnverifiedWarningDialog
{
    public static void Show(
        ConsoleWindowSystem windowSystem,
        MarketplaceManager.MarketplaceWidgetInfo widget,
        WidgetManifest manifest,
        Window? parentWindow = null,
        Action<bool>? onResult = null)
    {
        // Calculate modal size
        int modalWidth = 80;
        int modalHeight = 22;

        // Create modal
        var builder = new WindowBuilder(windowSystem)
            .WithTitle("⚠ Unverified Widget Warning")
            .WithSize(modalWidth, modalHeight)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Red)
            .Resizable(false)
            .Movable(false)
            .Minimizable(false)
            .Maximizable(false)
            .WithColors(Color.Grey93, Color.Grey15);

        if (parentWindow != null)
        {
            builder = builder.WithParent(parentWindow);
        }

        var modal = builder.Build();

        // Warning header
        modal.AddControl(Controls.Markup()
            .AddLine("")
            .AddLine("[white on red3] ⚠ WARNING: Unverified Widget [/]")
            .AddLine("")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .Build());

        // Warning message
        var messageBuilder = Controls.Markup()
            .WithMargin(1, 0, 1, 0)
            .AddLine("[yellow bold]This widget has not been reviewed by ServerHub maintainers.[/]")
            .AddLine("")
            .AddLine("[grey70]Security Risks:[/]")
            .AddLine("  [yellow]•[/] Widget code has not been audited")
            .AddLine("  [yellow]•[/] May contain malicious or insecure code")
            .AddLine("  [yellow]•[/] You are responsible for reviewing the code")
            .AddLine("")
            .AddLine($"[grey70]Widget:[/] [cyan1]{widget.Id}[/]")
            .AddLine($"[grey70]Author:[/] {manifest.Metadata.Author}")
            .AddLine($"[grey70]Source:[/] {manifest.Metadata.Homepage}")
            .AddLine("")
            .AddLine("[red bold]Only install widgets from sources you trust![/]")
            .AddLine("");

        modal.AddControl(messageBuilder.Build());

        // Buttons
        var reviewButton = Controls
            .Button(" Review Source ")
            .OnClick((s, e) => OpenBrowser(manifest.Metadata.Homepage))
            .Build();

        var cancelButton = Controls
            .Button(" Cancel ")
            .WithMargin(2, 0, 0, 0)
            .OnClick((s, e) =>
            {
                modal.Close();
                onResult?.Invoke(false);
            })
            .Build();

        var acceptButton = Controls
            .Button(" I Accept the Risk ")
            .WithMargin(2, 0, 0, 0)
            .OnClick((s, e) =>
            {
                modal.Close();
                onResult?.Invoke(true);
            })
            .Build();

        var buttonGrid = HorizontalGridControl.ButtonRow(reviewButton, cancelButton, acceptButton);
        buttonGrid.HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment.Center;
        buttonGrid.StickyPosition = StickyPosition.Bottom;
        buttonGrid.Margin = new Margin(0, 0, 0, 1);
        modal.AddControl(buttonGrid);

        // Footer separator
        modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23).StickyBottom().Build());

        // Footer instructions
        modal.AddControl(
            Controls
                .Markup()
                .AddLine("[grey70]Tab: Switch • Enter: Accept • Esc: Cancel[/]")
                .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
                .StickyBottom()
                .Build()
        );

        // Keyboard shortcuts
        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                modal.Close();
                onResult?.Invoke(false);
                e.Handled = true;
            }
        };

        // Handle close without selection
        var resultInvoked = false;
        modal.OnClosed += (s, e) =>
        {
            if (!resultInvoked)
            {
                resultInvoked = true;
                onResult?.Invoke(false);
            }
        };

        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);
        cancelButton.SetFocus(true, FocusReason.Programmatic); // Default to Cancel
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore errors
        }
    }
}
