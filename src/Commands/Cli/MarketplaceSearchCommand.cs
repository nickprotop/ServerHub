using Spectre.Console;
using Spectre.Console.Cli;
using ServerHub.Commands.Settings;
using ServerHub.Marketplace.Services;

namespace ServerHub.Commands.Cli;

/// <summary>
/// Marketplace search command - searches for widgets in the marketplace
/// </summary>
public class MarketplaceSearchCommand : AsyncCommand<MarketplaceSearchSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, MarketplaceSearchSettings settings)
    {
        var registryClient = new RegistryClient();

        AnsiConsole.MarkupLine($"Searching marketplace for: [cyan]{settings.Query}[/]");

        var results = await registryClient.SearchWidgetsAsync(settings.Query);

        if (results.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No widgets found matching your search.[/]");
            return 0;
        }

        AnsiConsole.WriteLine($"\nFound {results.Count} widget(s):\n");

        var table = new Table();
        table.AddColumn("ID");
        table.AddColumn("Name");
        table.AddColumn("Version");
        table.AddColumn("Category");
        table.AddColumn("Status");

        foreach (var widget in results)
        {
            var badge = MarketplaceHelpers.GetVerificationBadge(widget.VerificationLevel);
            table.AddRow(
                widget.Id,
                widget.Name,
                widget.LatestVersion,
                widget.Category,
                badge
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Use 'serverhub marketplace info <widget-id>' for more details[/]");

        return 0;
    }
}
