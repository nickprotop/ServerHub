// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Marketplace.Models;
using ServerHub.Marketplace.Services;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using Spectre.Console;

namespace ServerHub.UI;

/// <summary>
/// Dialog for prompting user to add installed widget to config
/// </summary>
public static class ConfigIntegrationDialog
{
    public static void Show(
        ConsoleWindowSystem windowSystem,
        WidgetInstaller.InstallResult result,
        WidgetManifest manifest,
        string configPath,
        Window? parentWindow = null,
        Action<bool>? onComplete = null)
    {
        // Calculate modal size
        int modalWidth = 80;
        int modalHeight = 24;

        // Create modal
        var builder = new WindowBuilder(windowSystem)
            .WithTitle("Widget Installed Successfully")
            .WithSize(modalWidth, modalHeight)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Green)
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

        // Success header
        modal.AddControl(Controls.Markup()
            .AddLine("")
            .AddLine("[green bold]✓ Widget Installed Successfully![/]")
            .AddLine("")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .Build());

        // Installation details
        var detailsBuilder = Controls.Markup()
            .WithMargin(1, 0, 1, 0)
            .AddLine($"[grey70]Widget:[/] [cyan1]{result.WidgetId}[/]")
            .AddLine($"[grey70]Location:[/] {result.InstalledPath}")
            .AddLine($"[grey70]SHA256:[/] [grey50]{result.Sha256}[/]")
            .AddLine("")
            .AddLine("[cyan1 bold]Add to your ServerHub configuration?[/]")
            .AddLine("");

        modal.AddControl(detailsBuilder.Build());

        // Check if widget already exists
        bool alreadyExists = ConfigHelper.WidgetExistsInConfig(configPath, result.WidgetId ?? "", manifest.Metadata.Id);

        // Declare controls that need to be accessed later for focus
        ButtonControl? okButton = null;
        PromptControl? refreshInput = null;

        if (alreadyExists)
        {
            // Widget already in config - just show message
            modal.AddControl(Controls.Markup()
                .WithMargin(1, 0, 1, 0)
                .AddLine("[yellow]Widget already exists in config file.[/]")
                .AddLine("")
                .AddLine("[grey70]The widget has been updated. Your existing configuration")
                .AddLine("settings have been preserved.[/]")
                .AddLine("")
                .Build());

            okButton = Controls
                .Button("  OK  ")
                .OnClick((s, e) =>
                {
                    modal.Close();
                    onComplete?.Invoke(false);
                })
                .Build();

            var buttonGrid = HorizontalGridControl.ButtonRow(okButton);
            buttonGrid.HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment.Center;
            buttonGrid.StickyPosition = StickyPosition.Bottom;
            modal.AddControl(buttonGrid);
        }
        else
        {
            // New widget - prompt for config
            var defaultRefresh = manifest.Config?.DefaultRefresh ?? 10;
            var defaultExpandedRefresh = manifest.Config?.DefaultExpandedRefresh;

            // Refresh interval input
            refreshInput = new PromptControl
            {
                Prompt = "Refresh interval (seconds):",
                Input = defaultRefresh.ToString(),
                InputWidth = 10,
                Margin = new Margin(1, 0, 1, 0)
            };
            modal.AddControl(refreshInput);

            // Expanded refresh interval input (optional)
            var expandedRefreshInput = new PromptControl
            {
                Prompt = "Expanded refresh (optional, seconds):",
                Input = defaultExpandedRefresh?.ToString() ?? "",
                InputWidth = 10,
                Margin = new Margin(1, 1, 1, 0)
            };
            modal.AddControl(expandedRefreshInput);

            // Hint text
            modal.AddControl(Controls.Markup()
                .WithMargin(1, 1, 1, 0)
                .AddLine("[grey50]Leave expanded refresh empty if not needed[/]")
                .AddLine("[grey50]Refresh intervals must be between 1-3600 seconds[/]")
                .AddLine("")
                .Build());

            // Buttons
            var addButton = Controls
                .Button(" Add to Config ")
                .OnClick((s, e) =>
                {
                    // Validate and add to config
                    if (!int.TryParse(refreshInput.Input, out int refresh) || refresh < 1 || refresh > 3600)
                    {
                        ShowError(windowSystem, "Refresh interval must be between 1 and 3600 seconds");
                        return;
                    }

                    int? expandedRefresh = null;
                    if (!string.IsNullOrWhiteSpace(expandedRefreshInput.Input))
                    {
                        if (!int.TryParse(expandedRefreshInput.Input, out int expanded) || expanded < 1 || expanded > 3600)
                        {
                            ShowError(windowSystem, "Expanded refresh interval must be between 1 and 3600 seconds");
                            return;
                        }
                        expandedRefresh = expanded;
                    }

                    // Check if this is an update (widget already exists)
                    bool widgetExists = ConfigHelper.WidgetExistsInConfig(
                        configPath,
                        result.WidgetId ?? "",
                        manifest.Metadata.Id
                    );

                    bool success;
                    if (widgetExists)
                    {
                        // Update existing widget
                        success = ConfigHelper.UpdateWidgetVersionInConfig(
                            configPath,
                            result.WidgetId ?? "",
                            manifest.LatestVersion?.Version ?? "",
                            result.Sha256 ?? ""
                        );
                    }
                    else
                    {
                        // Add new widget to config
                        var widgetFileName = System.IO.Path.GetFileName(result.InstalledPath) ?? "";
                        success = ConfigHelper.AddWidgetToConfig(
                            configPath,
                            result.WidgetId ?? "",
                            widgetFileName,
                            result.Sha256 ?? "",
                            refresh,
                            expandedRefresh,
                            source: "marketplace",
                            marketplaceId: manifest.Metadata.Id,
                            marketplaceVersion: manifest.LatestVersion?.Version
                        );
                    }

                    modal.Close();
                    onComplete?.Invoke(success);

                    if (!success)
                    {
                        var action = widgetExists ? "update" : "add";
                        ShowError(windowSystem, $"Failed to {action} widget in config. Please update manually.");
                    }
                })
                .Build();

            var manualButton = Controls
                .Button(" Manual Setup ")
                .WithMargin(2, 0, 0, 0)
                .OnClick((s, e) =>
                {
                    ShowManualInstructions(windowSystem, result, manifest, configPath);
                    modal.Close();
                    onComplete?.Invoke(false);
                })
                .Build();

            var skipButton = Controls
                .Button(" Skip ")
                .WithMargin(2, 0, 0, 0)
                .OnClick((s, e) =>
                {
                    modal.Close();
                    onComplete?.Invoke(false);
                })
                .Build();

            var buttonGrid = HorizontalGridControl.ButtonRow(addButton, manualButton, skipButton);
            buttonGrid.HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment.Center;
            buttonGrid.StickyPosition = StickyPosition.Bottom;
            buttonGrid.Margin = new Margin(0, 0, 0, 1);
            modal.AddControl(buttonGrid);
        }

        // Footer separator
        modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23).StickyBottom().Build());

        // Footer instructions
        modal.AddControl(
            Controls
                .Markup()
                .AddLine("[grey70]Tab: Switch • Enter: Add • Esc: Skip[/]")
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
                onComplete?.Invoke(false);
                e.Handled = true;
            }
        };

        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);

        // Explicitly focus first interactive control
        if (alreadyExists)
        {
            // Focus OK button
            okButton?.SetFocus(true, FocusReason.Programmatic);
        }
        else
        {
            // Focus refresh interval input
            refreshInput?.SetFocus(true, FocusReason.Programmatic);
        }
    }

    private static void ShowError(ConsoleWindowSystem windowSystem, string message)
    {
        windowSystem.NotificationStateService.ShowNotification(
            "Error",
            message,
            NotificationSeverity.Danger,
            timeout: 5000
        );
    }

    private static void ShowManualInstructions(
        ConsoleWindowSystem windowSystem,
        WidgetInstaller.InstallResult result,
        WidgetManifest manifest,
        string configPath)
    {
        var instructionsDialog = new WindowBuilder(windowSystem)
            .WithTitle("Manual Configuration")
            .WithSize(90, 20)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Cyan1)
            .WithColors(Color.Grey93, Color.Grey15)
            .Build();

        var widgetFileName = System.IO.Path.GetFileName(result.InstalledPath) ?? "";
        var defaultRefresh = manifest.Config?.DefaultRefresh ?? 10;

        var instructionsBuilder = Controls.Markup()
            .WithMargin(1, 1, 1, 0)
            .AddLine("[cyan1 bold]Manual Configuration Instructions[/]")
            .AddLine("")
            .AddLine($"[grey70]Add the following to {Markup.Escape(configPath)}:[/]")
            .AddLine("")
            .WithBackgroundColor(Color.Grey19);

        instructionsBuilder.AddLine("[yellow]widgets:[/]");
        instructionsBuilder.AddLine($"[yellow]  {result.WidgetId}:[/]");
        instructionsBuilder.AddLine($"[yellow]    path: {widgetFileName}[/]");
        instructionsBuilder.AddLine($"[yellow]    refresh: {defaultRefresh}[/]");
        instructionsBuilder.AddLine($"[yellow]    sha256: \"{result.Sha256}\"[/]");

        if (manifest.Config?.DefaultExpandedRefresh.HasValue == true)
        {
            instructionsBuilder.AddLine($"[yellow]    expanded_refresh: {manifest.Config.DefaultExpandedRefresh}[/]");
        }

        instructionsBuilder.AddLine("");
        instructionsBuilder.AddLine("[grey70]Then:[/]");
        instructionsBuilder.AddLine("  [grey70]•[/] Restart ServerHub or press F5 to load the widget");
        instructionsBuilder.AddLine("  [grey70]•[/] Press F2 to customize widget settings");
        instructionsBuilder.AddLine("");

        instructionsDialog.AddControl(instructionsBuilder.Build());

        var okButton = Controls
            .Button("  OK  ")
            .OnClick((s, e) => windowSystem.CloseWindow(instructionsDialog))
            .Build();

        var buttonGrid = HorizontalGridControl.ButtonRow(okButton);
        buttonGrid.HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment.Center;
        buttonGrid.StickyPosition = StickyPosition.Bottom;
        instructionsDialog.AddControl(buttonGrid);

        instructionsDialog.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape || e.KeyInfo.Key == ConsoleKey.Enter)
            {
                windowSystem.CloseWindow(instructionsDialog);
                e.Handled = true;
            }
        };

        windowSystem.AddWindow(instructionsDialog);
    }
}
