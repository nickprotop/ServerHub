using Spectre.Console;
using Spectre.Console.Cli;
using ServerHub.Commands.Settings.Marketplace;
using ServerHub.Config;
using ServerHub.Services;
using ServerHub.Utils;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace ServerHub.Commands.Cli.Marketplace;

/// <summary>
/// Marketplace check-updates command - lists widgets with available updates
/// </summary>
public class CheckUpdatesCommand : AsyncCommand<CheckUpdatesSettings>
{
    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access",
        Justification = "JSON serialization of anonymous types is safe here as the types are defined inline and will be preserved")]
    private static string SerializeToJson<T>(T data)
    {
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    public override async Task<int> ExecuteAsync(CommandContext context, CheckUpdatesSettings settings)
    {
        var installPath = WidgetPaths.GetMarketplaceInstallPath();
        var configPath = ConfigManager.GetDefaultConfigPath();
        var manager = new MarketplaceManager(installPath, configPath);

        AnsiConsole.MarkupLine("[cyan]Checking for widget updates...[/]\n");

        // Get all widgets and filter for updates
        var allWidgets = await manager.GetAllWidgetsAsync();
        var updates = manager.FilterByStatus(allWidgets, "updates");

        if (updates.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]All widgets are up to date![/]");
            return 0;
        }

        // JSON output
        if (settings.JsonOutput)
        {
            var jsonData = updates.Select(w => new
            {
                id = w.Id,
                name = w.Name,
                currentVersion = w.InstalledVersion,
                latestVersion = w.LatestVersion,
                manifestUrl = w.ManifestUrl
            });

            // Suppress trimming warning - anonymous types are preserved
            var json = SerializeToJson(jsonData);
            Console.WriteLine(json);
            return 0;
        }

        // Table output
        AnsiConsole.MarkupLine($"[yellow]Available Updates ({updates.Count}):[/]\n");

        var table = new Table();
        table.AddColumn("Widget ID");
        table.AddColumn("Name");
        table.AddColumn("Current");
        table.AddColumn("Latest");
        table.AddColumn("Category");

        foreach (var widget in updates.OrderBy(w => w.Id))
        {
            table.AddRow(
                widget.Id,
                widget.Name,
                widget.InstalledVersion ?? "?",
                $"[green]{widget.LatestVersion}[/]",
                widget.Category
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Run 'serverhub marketplace update <widget-id>' to update[/]");
        AnsiConsole.MarkupLine("[dim]Run 'serverhub marketplace update-all' to update all widgets[/]");

        return 0;
    }
}
