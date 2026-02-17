// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Marketplace.Models;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using Spectre.Console;

namespace ServerHub.UI;

/// <summary>
/// Dialog for selecting a specific widget version to install
/// </summary>
public static class VersionSelectorDialog
{
    public static void Show(
        ConsoleWindowSystem windowSystem,
        string widgetName,
        List<WidgetVersion> versions,
        Window? parentWindow = null,
        Action<string?>? onVersionSelected = null)
    {
        if (versions.Count == 0)
        {
            onVersionSelected?.Invoke(null);
            return;
        }

        // Calculate modal size
        int modalWidth = 70;
        int modalHeight = Math.Min(25, 10 + versions.Count);

        // Create modal
        var builder = new WindowBuilder(windowSystem)
            .WithTitle("Select Version to Install")
            .WithSize(modalWidth, modalHeight)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Cyan1)
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

        // Header
        modal.AddControl(Controls.Markup()
            .AddLine($"[bold]{widgetName}[/] has {versions.Count} version(s) available")
            .AddLine("")
            .WithMargin(1, 1, 1, 0)
            .Build());

        // Version list
        var versionList = Controls
            .List()
            .WithName("version_list")
            .WithTitle("")
            .WithColors(Color.Grey93, Color.Grey19)
            .WithFocusedColors(Color.Grey93, Color.Grey19)
            .WithHighlightColors(Color.White, Color.Grey35)
            .WithMargin(1, 0, 1, 0)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .Build();

        // Sort versions by release date (newest first)
        var sortedVersions = versions.OrderByDescending(v => v.Released).ToList();

        // Get latest version
        var latestVersion = sortedVersions.FirstOrDefault();

        foreach (var version in sortedVersions)
        {
            var isLatest = version == latestVersion;
            var latestTag = isLatest ? " [cyan1](latest)[/]" : "";
            var item = new ListItem($"  [cyan1]●[/] {version.Version}{latestTag} - {version.Released:yyyy-MM-dd}")
            {
                Tag = version.Version
            };
            versionList.AddItem(item);
        }

        // Select first (latest) by default
        versionList.SelectedIndex = 0;

        modal.AddControl(versionList);

        // Buttons
        var installLatestButton = Controls
            .Button(" Install Latest ")
            .OnClick((s, e) =>
            {
                modal.Close();
                onVersionSelected?.Invoke(latestVersion?.Version);
            })
            .Build();

        var selectButton = Controls
            .Button(" Select ")
            .WithMargin(2, 0, 0, 0)
            .OnClick((s, e) =>
            {
                var selected = versionList.Items[versionList.SelectedIndex].Tag as string;
                modal.Close();
                onVersionSelected?.Invoke(selected);
            })
            .Build();

        var cancelButton = Controls
            .Button(" Cancel ")
            .WithMargin(2, 0, 0, 0)
            .OnClick((s, e) =>
            {
                modal.Close();
                onVersionSelected?.Invoke(null);
            })
            .Build();

        var buttonGrid = HorizontalGridControl.ButtonRow(installLatestButton, selectButton, cancelButton);
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
                .AddLine("[grey70]↑↓: Navigate • Enter: Select • Esc: Cancel[/]")
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
                onVersionSelected?.Invoke(null);
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.Enter)
            {
                var selected = versionList.Items[versionList.SelectedIndex].Tag as string;
                modal.Close();
                onVersionSelected?.Invoke(selected);
                e.Handled = true;
            }
        };

        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);
        versionList.SetFocus(true, FocusReason.Programmatic);
    }
}
