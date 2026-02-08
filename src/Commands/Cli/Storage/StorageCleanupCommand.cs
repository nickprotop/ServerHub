// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Spectre.Console;
using Spectre.Console.Cli;
using ServerHub.Commands.Settings.Storage;
using ServerHub.Services;

namespace ServerHub.Commands.Cli.Storage;

/// <summary>
/// Command to manually run database cleanup.
/// </summary>
public class StorageCleanupCommand : Command<StorageCleanupSettings>
{
    public override int Execute(CommandContext context, StorageCleanupSettings settings)
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
            var statsBefore = storageService.GetStats();

            // Show current stats
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[cyan]Current Statistics[/]").RuleStyle("grey").LeftJustified());
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"Database Size: [cyan]{statsBefore.DatabaseSizeMB:F2} MB[/]");
            AnsiConsole.MarkupLine($"Total Records: [cyan]{statsBefore.TotalRecords:N0}[/]");
            AnsiConsole.MarkupLine($"Retention Policy: [yellow]{config.Storage.RetentionDays} days[/]");
            AnsiConsole.WriteLine();

            // Calculate what will be deleted
            var cutoffDate = DateTime.UtcNow.AddDays(-config.Storage.RetentionDays);
            AnsiConsole.MarkupLine($"[yellow]This will delete all records older than {cutoffDate:yyyy-MM-dd HH:mm:ss}[/]");

            if (config.Storage.AutoVacuum)
            {
                AnsiConsole.MarkupLine($"[yellow]VACUUM will be run to reclaim disk space[/]");
            }

            AnsiConsole.WriteLine();

            // Confirmation prompt
            if (!settings.Force)
            {
                if (!AnsiConsole.Confirm("Continue with cleanup?", false))
                {
                    AnsiConsole.MarkupLine("[grey]Cleanup cancelled[/]");
                    return 0;
                }
                AnsiConsole.WriteLine();
            }

            // Run cleanup
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start("Running cleanup...", ctx =>
                {
                    ctx.Status("Deleting old records...");
                    var deleted = storageService.RunCleanup();

                    if (deleted > 0)
                    {
                        ctx.Status($"Deleted {deleted:N0} records");
                    }
                });

            var statsAfter = storageService.GetStats();

            // Show results
            AnsiConsole.Write(new Rule("[green]Cleanup Complete[/]").RuleStyle("grey").LeftJustified());
            AnsiConsole.WriteLine();

            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.BorderColor(Color.Grey);
            table.AddColumn(new TableColumn("[grey]Metric[/]").NoWrap());
            table.AddColumn(new TableColumn("[grey]Before[/]").RightAligned());
            table.AddColumn(new TableColumn("[grey]After[/]").RightAligned());
            table.AddColumn(new TableColumn("[grey]Change[/]").RightAligned());

            // Database size
            var sizeDiff = statsBefore.DatabaseSizeMB - statsAfter.DatabaseSizeMB;
            var sizeChange = sizeDiff > 0 ? $"[green]-{sizeDiff:F2} MB[/]" : "[grey]0 MB[/]";
            table.AddRow("Database Size", $"{statsBefore.DatabaseSizeMB:F2} MB", $"{statsAfter.DatabaseSizeMB:F2} MB", sizeChange);

            // Record count
            var recordDiff = statsBefore.TotalRecords - statsAfter.TotalRecords;
            var recordChange = recordDiff > 0 ? $"[green]-{recordDiff:N0}[/]" : "[grey]0[/]";
            table.AddRow("Total Records", $"{statsBefore.TotalRecords:N0}", $"{statsAfter.TotalRecords:N0}", recordChange);

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            if (recordDiff == 0)
            {
                AnsiConsole.MarkupLine("[grey]No records were older than the retention period[/]");
            }
            else if (sizeDiff > 0)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Reclaimed {sizeDiff:F2} MB of disk space");
            }

            AnsiConsole.WriteLine();
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}
