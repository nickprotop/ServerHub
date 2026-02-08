using Spectre.Console;
using ServerHub.Config;
using ServerHub.Marketplace.Services;
using ServerHub.Services;
using ServerHub.Utils;

namespace ServerHub.Commands.Cli.Marketplace;

/// <summary>
/// Marketplace update-all command - updates all widgets with available updates
/// </summary>
public class UpdateAllCommand
{
    public static async Task<int> ExecuteAsync(bool skipConfirmation)
    {
        var installPath = WidgetPaths.GetMarketplaceInstallPath();
        var configPath = ConfigManager.GetDefaultConfigPath();
        var manager = new MarketplaceManager(installPath, configPath);

        AnsiConsole.MarkupLine("[cyan]Checking for widget updates...[/]\n");

        // Get all widgets with updates
        var allWidgets = await manager.GetAllWidgetsAsync();
        var updates = manager.FilterByStatus(allWidgets, "updates");

        if (updates.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]All widgets are up to date![/]");
            return 0;
        }

        // No breaking changes filtering since it's not in the manifest
        var updatesToInstall = updates;

        // Show summary
        AnsiConsole.MarkupLine($"[yellow]Found {updatesToInstall.Count} update(s):[/]\n");

        var table = new Table();
        table.AddColumn("Widget ID");
        table.AddColumn("Current");
        table.AddColumn("Latest");

        foreach (var widget in updatesToInstall)
        {
            table.AddRow(
                widget.Id,
                widget.InstalledVersion ?? "?",
                $"[green]{widget.LatestVersion}[/]"
            );
        }

        AnsiConsole.Write(table);

        // Confirm batch update (unless --yes flag)
        if (!skipConfirmation)
        {
            AnsiConsole.WriteLine();
            if (!AnsiConsole.Confirm($"Update all {updatesToInstall.Count} widget(s)?", defaultValue: true))
            {
                AnsiConsole.MarkupLine("[yellow]Update cancelled[/]");
                return 0;
            }
        }

        // Update each widget
        AnsiConsole.WriteLine();
        int successCount = 0;
        int failCount = 0;
        var failedWidgets = new List<string>();

        foreach (var widget in updatesToInstall)
        {
            AnsiConsole.MarkupLine($"[cyan]Updating {widget.Id}...[/]");

            var result = await manager.InstallWidgetAsync(widget.Id, widget.LatestVersion);

            if (result.Success)
            {
                // Update config
                bool configUpdated = ConfigHelper.UpdateWidgetVersionInConfig(
                    configPath,
                    widget.Id,
                    widget.LatestVersion,
                    result.Sha256 ?? ""
                );

                if (configUpdated)
                {
                    AnsiConsole.MarkupLine($"  [green]✓[/] Updated to v{widget.LatestVersion}");
                    successCount++;
                }
                else
                {
                    AnsiConsole.MarkupLine($"  [yellow]⚠[/] Updated file but config update failed");
                    failCount++;
                    failedWidgets.Add(widget.Id);
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"  [red]✗[/] Failed: {result.ErrorMessage}");
                failCount++;
                failedWidgets.Add(widget.Id);
            }

            AnsiConsole.WriteLine();
        }

        // Summary
        AnsiConsole.MarkupLine("[bold]Update Summary:[/]");
        AnsiConsole.MarkupLine($"  [green]✓ Successful:[/] {successCount}");

        if (failCount > 0)
        {
            AnsiConsole.MarkupLine($"  [red]✗ Failed:[/] {failCount}");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]Failed widgets:[/]");
            foreach (var id in failedWidgets)
            {
                AnsiConsole.MarkupLine($"  • {id}");
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Restart ServerHub or press F5 to reload widgets[/]");

        return failCount > 0 ? 1 : 0;
    }
}
