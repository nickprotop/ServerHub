// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Models;

namespace ServerHub.Services;

/// <summary>
/// Service for gathering and managing command palette commands
/// </summary>
public class CommandPaletteService
{
    private readonly Dictionary<string, Action> _systemCommandCallbacks = new();

    /// <summary>
    /// Register a system command callback
    /// </summary>
    public void RegisterSystemCommand(string commandId, Action callback)
    {
        _systemCommandCallbacks[commandId] = callback;
    }

    /// <summary>
    /// Get all available commands
    /// </summary>
    public List<PaletteCommand> GetAllCommands(
        Dictionary<string, WidgetData> widgetData,
        FocusManager? focusManager)
    {
        var commands = new List<PaletteCommand>();

        // System commands
        commands.AddRange(GetSystemCommands());

        // Navigation commands (jump to widget)
        commands.AddRange(GetNavigationCommands(widgetData));

        // Widget actions
        commands.AddRange(GetWidgetActions(widgetData));

        return commands.OrderByDescending(c => c.Priority).ToList();
    }

    /// <summary>
    /// Get system/dashboard commands
    /// </summary>
    private List<PaletteCommand> GetSystemCommands()
    {
        return new List<PaletteCommand>
        {
            new PaletteCommand
            {
                Id = "system-refresh-all",
                Label = "Refresh All Widgets",
                Description = "Refresh all dashboard data (F5)",
                Type = CommandType.System,
                Icon = "[cyan1]↻[/]",
                Priority = 100,
                Execute = GetSystemCallback("system-refresh-all")
            },
            new PaletteCommand
            {
                Id = "system-toggle-pause",
                Label = "Toggle Pause",
                Description = "Pause/resume auto-refresh (Space)",
                Type = CommandType.System,
                Icon = "[yellow]||[/]",
                Priority = 90,
                Execute = GetSystemCallback("system-toggle-pause")
            },
            new PaletteCommand
            {
                Id = "system-configure",
                Label = "Configure Widgets",
                Description = "Open widget configuration (F2)",
                Type = CommandType.System,
                Icon = "[grey70]⚙[/]",
                Priority = 80,
                Execute = GetSystemCallback("system-configure")
            },
            new PaletteCommand
            {
                Id = "system-marketplace",
                Label = "Browse Marketplace",
                Description = "Open marketplace browser (F3)",
                Type = CommandType.System,
                Icon = "[green]◆[/]",
                Priority = 70,
                Execute = GetSystemCallback("system-marketplace")
            },
            new PaletteCommand
            {
                Id = "system-help",
                Label = "Show Help",
                Description = "Show keyboard shortcuts (? or F1)",
                Type = CommandType.System,
                Icon = "[blue]?[/]",
                Priority = 60,
                Execute = GetSystemCallback("system-help")
            }
        };
    }

    /// <summary>
    /// Get navigation commands (jump to widget)
    /// </summary>
    private List<PaletteCommand> GetNavigationCommands(Dictionary<string, WidgetData> widgetData)
    {
        var commands = new List<PaletteCommand>();

        foreach (var (widgetId, data) in widgetData)
        {
            // Skip null entries
            if (data == null)
                continue;

            // Use widgetId as fallback if Title is null/empty
            var title = string.IsNullOrEmpty(data.Title) ? widgetId : data.Title;

            commands.Add(new PaletteCommand
            {
                Id = $"nav-{widgetId}",
                Label = $"Jump to: {title}",
                Description = $"Focus {title} widget",
                Type = CommandType.Navigation,
                Icon = "[cyan1]→[/]",
                Priority = 50,
                WidgetId = widgetId
            });
        }

        return commands;
    }

    /// <summary>
    /// Get widget actions
    /// </summary>
    private List<PaletteCommand> GetWidgetActions(Dictionary<string, WidgetData> widgetData)
    {
        var commands = new List<PaletteCommand>();

        foreach (var (widgetId, data) in widgetData)
        {
            // Skip null entries
            if (data == null)
                continue;

            if (data.Actions == null || data.Actions.Count == 0)
                continue;

            // Use widgetId as fallback if Title is null/empty
            var title = string.IsNullOrEmpty(data.Title) ? widgetId : data.Title;

            foreach (var action in data.Actions)
            {
                // Skip null actions
                if (action == null)
                    continue;

                // Build description with flag icons
                var flagIcons = new List<string>();

                if (action.RequiresSudo)
                    flagIcons.Add("🔒");
                if (action.IsDanger)
                    flagIcons.Add("⚠");

                var description = $"Execute: {action.Label ?? "Unknown"}";
                if (flagIcons.Count > 0)
                    description += $" {string.Join(" ", flagIcons)}";

                commands.Add(new PaletteCommand
                {
                    Id = $"action-{widgetId}-{action.Label ?? "unknown"}",
                    Label = $"{title} › {action.Label ?? "Unknown"}",
                    Description = description,
                    Type = CommandType.WidgetAction,
                    Icon = action.IsDanger ? "[yellow]⚠[/]" : "[yellow]»[/]",
                    Priority = 40,
                    WidgetId = widgetId,
                    Action = action
                });
            }
        }

        return commands;
    }

    /// <summary>
    /// Get callback for system command
    /// </summary>
    private Action? GetSystemCallback(string commandId)
    {
        return _systemCommandCallbacks.TryGetValue(commandId, out var callback) ? callback : null;
    }
}
