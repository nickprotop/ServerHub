// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Marketplace.Models;
using ServerHub.Marketplace.Services;
using ServerHub.Services;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using Spectre.Console;

namespace ServerHub.UI;

/// <summary>
/// Dialog for showing widget installation progress
/// States: Confirm → Downloading → Verifying → Installing → Complete
/// </summary>
public static class InstallationDialog
{
    private enum InstallState
    {
        Downloading,
        Verifying,
        Installing,
        Configuring,
        Complete,
        Failed
    }

    public static void Show(
        ConsoleWindowSystem windowSystem,
        MarketplaceManager manager,
        MarketplaceManager.MarketplaceWidgetInfo widget,
        WidgetManifest manifest,
        string? version,
        Window? parentWindow = null,
        string? configPath = null,
        Action<bool>? onComplete = null)
    {
        // Calculate modal size
        int screenWidth = Console.WindowWidth;
        int screenHeight = Console.WindowHeight;
        int modalWidth = Math.Min((int)(screenWidth * 0.7), 100);
        int modalHeight = Math.Min((int)(screenHeight * 0.7), 30);

        // Create modal
        var builder = new WindowBuilder(windowSystem)
            .WithTitle("Installing Widget")
            .WithSize(modalWidth, modalHeight)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Grey35)
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

        // Header
        modal.AddControl(Controls.Markup()
            .WithName("header")
            .AddLine($"[bold]Installing:[/] [cyan1]{widget.Name}[/] v{version ?? widget.LatestVersion}")
            .WithMargin(1, 1, 1, 0)
            .Build());

        modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23).Build());

        // Status
        modal.AddControl(Controls.Markup()
            .WithName("status")
            .AddLine("[cyan1]⟳[/] Initializing...")
            .WithMargin(1, 0, 1, 0)
            .Build());

        // Progress bar
        var progressBar = Controls.ProgressBar()
            .WithName("progress_bar")
            .Stretch()
            .WithMargin(1, 1, 1, 0)
            .Build();
        progressBar.IsIndeterminate = true;
        modal.AddControl(progressBar);

        // Details
        modal.AddControl(Controls.Markup()
            .WithName("details")
            .AddLine("")
            .WithMargin(1, 0, 1, 0)
            .Build());

        // Output panel (for error messages)
        var outputPanel = Controls
            .ScrollablePanel()
            .WithName("output_panel")
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithScrollbarPosition(ScrollbarPosition.Right)
            .WithMouseWheel(true)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithBackgroundColor(Color.Grey19)
            .Build();
        outputPanel.Visible = false;
        modal.AddControl(outputPanel);

        // Buttons
        var closeButton = Controls
            .Button("  Close  ")
            .WithName("btn_close")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .Build();
        closeButton.Visible = false;
        closeButton.Click += (s, e) => modal.Close();
        modal.AddControl(closeButton);

        // Footer
        modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23).StickyBottom().Build());
        modal.AddControl(
            Controls
                .Markup()
                .AddLine("[grey70]Please wait...[/]")
                .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
                .StickyBottom()
                .Build()
        );

        // Track result for callback
        bool success = false;

        // Start installation
        _ = Task.Run(async () =>
        {
            try
            {
                // State: Downloading
                UpdateState(modal, InstallState.Downloading, "Downloading widget from marketplace...");
                await Task.Delay(100); // Allow UI to update

                var result = await manager.InstallWidgetAsync(widget.Id, version);

                if (!result.Success)
                {
                    UpdateState(modal, InstallState.Failed, "Installation failed");
                    ShowError(modal, result.ErrorMessage ?? "Unknown error");
                    return;
                }

                // State: Verifying
                UpdateState(modal, InstallState.Verifying, "Verifying SHA256 checksum...");
                await Task.Delay(500);

                // State: Installing
                UpdateState(modal, InstallState.Installing, $"Installing to {result.InstalledPath}");
                await Task.Delay(500);

                // State: Configuring (show config dialog)
                UpdateState(modal, InstallState.Configuring, "Installation complete");

                // Show config integration dialog
                if (configPath != null)
                {
                    ConfigIntegrationDialog.Show(
                        windowSystem,
                        result,
                        manifest,
                        configPath,
                        modal,
                        (addedToConfig) =>
                        {
                            success = true;
                            UpdateState(modal, InstallState.Complete, "Widget installed successfully");
                            ShowComplete(modal, result, addedToConfig);
                        }
                    );
                }
                else
                {
                    success = true;
                    UpdateState(modal, InstallState.Complete, "Widget installed successfully");
                    ShowComplete(modal, result, false);
                }
            }
            catch (Exception ex)
            {
                UpdateState(modal, InstallState.Failed, "Installation failed");
                ShowError(modal, ex.Message);
            }
        });

        // Handle close
        modal.OnClosed += (s, e) =>
        {
            onComplete?.Invoke(success);
        };

        // Keyboard shortcuts
        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape || e.KeyInfo.Key == ConsoleKey.Enter)
            {
                var closeBtn = modal.FindControl<ButtonControl>("btn_close");
                if (closeBtn != null && closeBtn.Visible)
                {
                    modal.Close();
                    e.Handled = true;
                }
            }
        };

        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);
    }

    private static void UpdateState(Window modal, InstallState state, string message)
    {
        var statusControl = modal.FindControl<MarkupControl>("status");
        var progressBar = modal.FindControl<ProgressBarControl>("progress_bar");

        if (statusControl == null || progressBar == null)
            return;

        var icon = state switch
        {
            InstallState.Downloading => "[cyan1]⬇[/]",
            InstallState.Verifying => "[yellow]✓[/]",
            InstallState.Installing => "[cyan1]⚙[/]",
            InstallState.Configuring => "[cyan1]⚙[/]",
            InstallState.Complete => "[green]✓[/]",
            InstallState.Failed => "[red]✗[/]",
            _ => "[cyan1]⟳[/]"
        };

        statusControl.SetContent(new List<string> { $"{icon} {message}" });

        if (state == InstallState.Complete || state == InstallState.Failed)
        {
            progressBar.Visible = false;
        }
        else
        {
            progressBar.IsIndeterminate = true;
        }
    }

    private static void ShowError(Window modal, string errorMessage)
    {
        var detailsControl = modal.FindControl<MarkupControl>("details");
        var outputPanel = modal.FindControl<ScrollablePanelControl>("output_panel");
        var closeButton = modal.FindControl<ButtonControl>("btn_close");

        if (detailsControl != null)
        {
            detailsControl.SetContent(new List<string>
            {
                "[red bold]Error:[/]",
                $"[red]{Markup.Escape(errorMessage)}[/]"
            });
        }

        if (outputPanel != null)
        {
            outputPanel.Visible = true;
        }

        if (closeButton != null)
        {
            closeButton.Visible = true;
            closeButton.SetFocus(true, FocusReason.Programmatic);
        }
    }

    private static void ShowComplete(Window modal, WidgetInstaller.InstallResult result, bool addedToConfig)
    {
        var detailsControl = modal.FindControl<MarkupControl>("details");
        var closeButton = modal.FindControl<ButtonControl>("btn_close");

        if (detailsControl != null)
        {
            var lines = new List<string>
            {
                "[green bold]✓ Installation successful[/]",
                "",
                $"[grey70]Location:[/] {result.InstalledPath}",
                $"[grey70]SHA256:[/] {result.Sha256}",
                ""
            };

            if (addedToConfig)
            {
                lines.Add("[green]✓ Added to config file[/]");
                lines.Add("");
                lines.Add("[grey70]Next steps:[/]");
                lines.Add("  • Press F5 in ServerHub to load the widget");
                lines.Add("  • Press F2 to customize widget settings");
            }
            else
            {
                lines.Add("[yellow]Widget installed but not added to config[/]");
                lines.Add("");
                lines.Add("[grey70]To use this widget:[/]");
                lines.Add("  • Add it to your config.yaml manually, or");
                lines.Add("  • Press F2 in ServerHub to add via config dialog");
            }

            detailsControl.SetContent(lines);
        }

        if (closeButton != null)
        {
            closeButton.Visible = true;
            closeButton.SetFocus(true, FocusReason.Programmatic);
        }
    }
}
