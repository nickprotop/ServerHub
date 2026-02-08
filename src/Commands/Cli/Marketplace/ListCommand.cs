using Spectre.Console;
using ServerHub.Marketplace.Services;

namespace ServerHub.Commands.Cli.Marketplace;

/// <summary>
/// Marketplace list command - lists all available widgets
/// </summary>
public class ListCommand
{
    public static async Task<int> ExecuteAsync()
    {
        var registryClient = new RegistryClient();

        AnsiConsole.MarkupLine("[cyan]Fetching marketplace widgets...[/]");

        var index = await registryClient.FetchRegistryIndexAsync();
        if (index == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Failed to fetch marketplace index");
            return 1;
        }

        var widgets = index.Widgets;

        AnsiConsole.WriteLine($"\nAvailable widgets ({widgets.Count}):\n");

        // Group by category
        var grouped = widgets.GroupBy(w => w.Category).OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            AnsiConsole.MarkupLine($"[bold cyan]{group.Key}[/]");

            foreach (var widget in group.OrderBy(w => w.Name))
            {
                var badge = Helpers.GetVerificationBadge(widget.VerificationLevel);
                AnsiConsole.MarkupLine($"  [yellow]{widget.Id}[/] - {widget.Name} v{widget.LatestVersion} {badge}");
            }

            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine("[dim]Use 'serverhub marketplace info <widget-id>' for more details[/]");
        return 0;
    }
}
