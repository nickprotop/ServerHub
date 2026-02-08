using Spectre.Console;
using ServerHub.Marketplace.Services;

namespace ServerHub.Commands.Cli.Marketplace;

/// <summary>
/// Marketplace search command - searches for widgets in the marketplace
/// </summary>
public class SearchCommand
{
    public static async Task<int> ExecuteAsync(string query)
    {
        var registryClient = new RegistryClient();

        AnsiConsole.MarkupLine($"Searching marketplace for: [cyan]{query}[/]");

        var results = await registryClient.SearchWidgetsAsync(query);

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
            var badge = Helpers.GetVerificationBadge(widget.VerificationLevel);
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
