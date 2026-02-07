// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Models;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Layout;
using Spectre.Console;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;
using VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment;

namespace ServerHub.UI;

/// <summary>
/// Command palette modal dialog for quick action finding
/// </summary>
public static class CommandPaletteDialog
{
    /// <summary>
    /// Show the command palette modal
    /// </summary>
    public static void Show(
        ConsoleWindowSystem windowSystem,
        Action<PaletteCommand?> onCommandSelected,
        IEnumerable<PaletteCommand> commands)
    {
        var allCommands = commands.ToList();

        var modal = new WindowBuilder(windowSystem)
            .WithTitle("Command Palette")
            .Centered()
            .WithSize(85, 22)
            .AsModal()
            .Borderless()
            .Resizable(false)
            .Movable(false)
            .WithColors(Color.Grey15, Color.Grey93)
            .Build();

        // Header
        modal.AddControl(Controls.Markup()
            .AddLine("[cyan1 bold]Commands[/]")
            .AddLine("[grey50]Type to search, Enter to execute[/]")
            .WithAlignment(HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build());

        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .Build());

        // Search input (using PromptControl for single-line input)
        var searchInput = Controls.Prompt()
            .WithPrompt("Search: ")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(1, 0, 1, 0)
            .Build();

        modal.AddControl(searchInput);

        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .Build());

        // Command list
        var commandList = Controls.List()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey15, Color.Grey93)
            .WithFocusedColors(Color.Grey15, Color.Grey93)
            .WithHighlightColors(Color.Grey35, Color.White)
            .SimpleMode()
            .WithDoubleClickActivation(true)
            .Build();

        modal.AddControl(commandList);

        // Status footer (show count)
        var statusText = Controls.Markup()
            .AddLine($"[grey50]{allCommands.Count} commands[/]")
            .WithAlignment(HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .StickyBottom()
            .Build();

        modal.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .StickyBottom()
            .Build());

        modal.AddControl(statusText);

        // Instructions footer
        modal.AddControl(Controls.Markup()
            .AddLine("[grey70]Enter/Double-click: Execute  •  Escape: Cancel  •  ↑↓: Navigate[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(0, 0, 0, 0)
            .StickyBottom()
            .Build());

        // Initialize with all commands
        UpdateCommandList(commandList, statusText, allCommands, "");

        // Handle search input changes
        searchInput.InputChanged += (sender, newText) =>
        {
            UpdateCommandList(commandList, statusText, allCommands, newText);
        };

        // Handle command activation (Enter or double-click)
        commandList.ItemActivated += (sender, item) =>
        {
            if (item?.Tag is PaletteCommand command)
            {
                onCommandSelected(command);
                modal.Close();
            }
        };

        // Handle keyboard navigation
        modal.KeyPressed += (sender, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Enter)
            {
                // If search has focus and list has items, move to first item and select
                if (searchInput.HasFocus && commandList.Items.Count > 0)
                {
                    commandList.SetFocus(true, FocusReason.Keyboard);
                    e.Handled = true;
                }
                // If list has focus, execute selected command
                else if (commandList.HasFocus)
                {
                    var selectedItem = commandList.SelectedItem;
                    if (selectedItem?.Tag is PaletteCommand command)
                    {
                        onCommandSelected(command);
                        modal.Close();
                    }
                    e.Handled = true;
                }
            }
            else if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                onCommandSelected(null);
                modal.Close();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.DownArrow)
            {
                // Move from search to list
                if (searchInput.HasFocus && commandList.Items.Count > 0)
                {
                    commandList.SetFocus(true, FocusReason.Keyboard);
                    e.Handled = true;
                }
            }
            else if (e.KeyInfo.Key == ConsoleKey.UpArrow)
            {
                // Move from list back to search if at top
                if (commandList.HasFocus && commandList.SelectedIndex <= 0)
                {
                    searchInput.SetFocus(true, FocusReason.Keyboard);
                    e.Handled = true;
                }
            }
        };

        // Add modal to window system
        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);

        // Start with focus on search input
        searchInput.SetFocus(true, FocusReason.Programmatic);
    }

    /// <summary>
    /// Update command list based on search query
    /// </summary>
    private static void UpdateCommandList(
        ListControl list,
        MarkupControl status,
        List<PaletteCommand> allCommands,
        string searchQuery)
    {
        list.ClearItems();

        // Filter commands (substring search in label, description, and command text)
        var filtered = string.IsNullOrWhiteSpace(searchQuery)
            ? allCommands
            : allCommands.Where(cmd =>
                cmd.Label.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                cmd.Description.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                (cmd.Action != null && cmd.Action.Command.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
            ).ToList();

        // Sort: exact prefix matches first, then by priority
        filtered = filtered
            .OrderByDescending(cmd => cmd.Label.StartsWith(searchQuery, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(cmd => cmd.Priority)
            .ToList();

        // Add filtered commands to list
        foreach (var command in filtered)
        {
            var icon = string.IsNullOrEmpty(command.Icon) ? "  " : command.Icon;
            var label = $"{icon} {command.Label,-40} [grey70]{command.Description}[/]";
            list.AddItem(new ListItem(label) { Tag = command });
        }

        // Update status text
        var statusLine = string.IsNullOrWhiteSpace(searchQuery)
            ? $"[grey50]{filtered.Count} commands[/]"
            : $"[grey50]{filtered.Count} of {allCommands.Count} commands[/]";

        status.SetContent(new List<string> { statusLine });
    }
}
