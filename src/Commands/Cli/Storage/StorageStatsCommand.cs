// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Spectre.Console;
using Spectre.Console.Cli;
using ServerHub.Commands.Settings.Storage;
using ServerHub.Services;

namespace ServerHub.Commands.Cli.Storage;

/// <summary>
/// Command to display storage statistics.
/// </summary>
public class StorageStatsCommand : Command<StorageStatsSettings>
{
    public override int Execute(CommandContext context, StorageStatsSettings settings)
    {
        try
        {
            // Load configuration
            var configManager = new ConfigManager();
            var configPath = settings.ConfigPath ?? ConfigManager.GetDefaultConfigPath();
            var config = configManager.LoadConfig(configPath);

            if (config.Storage?.Enabled != true)
            {
                AnsiConsole.MarkupLine("[yellow]Storage is disabled in configuration[/]");
                return 1;
            }

            // Initialize storage service
            var storageService = ServerHub.Storage.StorageService.Initialize(config.Storage);
            var stats = storageService.GetStats();

            // Display statistics
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[cyan]Storage Statistics[/]").RuleStyle("grey").LeftJustified());
            AnsiConsole.WriteLine();

            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.BorderColor(Color.Grey);
            table.AddColumn(new TableColumn("[grey]Property[/]").NoWrap());
            table.AddColumn(new TableColumn("[grey]Value[/]"));

            table.AddRow("Database Path", stats.DatabasePath);
            table.AddRow("Database Size", $"{stats.DatabaseSizeMB:F2} MB");
            table.AddRow("Total Records", $"{stats.TotalRecords:N0}");
            table.AddRow("Widget Count", $"{stats.WidgetCount}");

            if (stats.OldestRecord.HasValue)
            {
                var age = DateTime.UtcNow - stats.OldestRecord.Value;
                table.AddRow("Oldest Record", $"{stats.OldestRecord.Value:yyyy-MM-dd HH:mm:ss} ({FormatAge(age)} ago)");
            }
            else
            {
                table.AddRow("Oldest Record", "[grey]None[/]");
            }

            if (stats.NewestRecord.HasValue)
            {
                var recency = DateTime.UtcNow - stats.NewestRecord.Value;
                table.AddRow("Newest Record", $"{stats.NewestRecord.Value:yyyy-MM-dd HH:mm:ss} ({FormatAge(recency)} ago)");
            }
            else
            {
                table.AddRow("Newest Record", "[grey]None[/]");
            }

            if (stats.LastCleanup != DateTime.MinValue)
            {
                var timeSince = DateTime.UtcNow - stats.LastCleanup;
                table.AddRow("Last Cleanup", $"{stats.LastCleanup:yyyy-MM-dd HH:mm:ss} ({FormatAge(timeSince)} ago)");
            }
            else
            {
                table.AddRow("Last Cleanup", "[grey]Never[/]");
            }

            if (stats.LastVacuum != DateTime.MinValue)
            {
                var timeSince = DateTime.UtcNow - stats.LastVacuum;
                table.AddRow("Last VACUUM", $"{stats.LastVacuum:yyyy-MM-dd HH:mm:ss} ({FormatAge(timeSince)} ago)");
            }
            else
            {
                table.AddRow("Last VACUUM", "[grey]Never[/]");
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            // Configuration info
            AnsiConsole.Write(new Rule("[cyan]Configuration[/]").RuleStyle("grey").LeftJustified());
            AnsiConsole.WriteLine();

            var configTable = new Table();
            configTable.Border(TableBorder.Rounded);
            configTable.BorderColor(Color.Grey);
            configTable.AddColumn(new TableColumn("[grey]Setting[/]").NoWrap());
            configTable.AddColumn(new TableColumn("[grey]Value[/]"));

            configTable.AddRow("Retention Period", $"{config.Storage.RetentionDays} days");
            configTable.AddRow("Cleanup Interval", $"{config.Storage.CleanupIntervalHours} hours");
            configTable.AddRow("Max Database Size", $"{config.Storage.MaxDatabaseSizeMb} MB");
            configTable.AddRow("Auto VACUUM", config.Storage.AutoVacuum ? "[green]Enabled[/]" : "[yellow]Disabled[/]");

            AnsiConsole.Write(configTable);
            AnsiConsole.WriteLine();

            // Warnings
            if (stats.DatabaseSizeMB > config.Storage.MaxDatabaseSizeMb)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠ Warning:[/] Database size exceeds configured maximum ({config.Storage.MaxDatabaseSizeMb} MB)");
                AnsiConsole.MarkupLine($"[grey]Consider running 'serverhub storage cleanup' or reducing retention_days[/]");
                AnsiConsole.WriteLine();
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalDays >= 1)
            return $"{(int)age.TotalDays}d {age.Hours}h";
        if (age.TotalHours >= 1)
            return $"{(int)age.TotalHours}h {age.Minutes}m";
        if (age.TotalMinutes >= 1)
            return $"{(int)age.TotalMinutes}m {age.Seconds}s";
        return $"{(int)age.TotalSeconds}s";
    }
}
