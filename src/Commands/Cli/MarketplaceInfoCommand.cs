using Spectre.Console;
using Spectre.Console.Cli;
using ServerHub.Commands.Settings;
using ServerHub.Marketplace.Services;
using ServerHub.Services;

namespace ServerHub.Commands.Cli;

/// <summary>
/// Marketplace info command - shows detailed information about a widget
/// </summary>
public class MarketplaceInfoCommand : AsyncCommand<MarketplaceInfoSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, MarketplaceInfoSettings settings)
    {
        var registryClient = new RegistryClient();
        var dependencyChecker = new DependencyChecker();

        AnsiConsole.MarkupLine($"Fetching information for: [cyan]{settings.WidgetId}[/]\n");

        var registryWidget = await registryClient.GetWidgetByIdAsync(settings.WidgetId);
        if (registryWidget == null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Widget '{settings.WidgetId}' not found in marketplace");
            return 1;
        }

        var manifest = await registryClient.FetchWidgetManifestAsync(registryWidget.ManifestUrl);
        if (manifest == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Failed to fetch widget manifest");
            return 1;
        }

        var metadata = manifest.Metadata;
        var latestVersion = manifest.LatestVersion;

        // Display widget information
        var panel = new Panel(
            new Markup($"[bold]{metadata.Name}[/] v{latestVersion?.Version ?? "unknown"}\n\n" +
                      $"{metadata.Description}\n\n" +
                      $"[dim]Author:[/] {metadata.Author}\n" +
                      $"[dim]Category:[/] {metadata.Category}\n" +
                      $"[dim]License:[/] {metadata.License}\n" +
                      $"[dim]Verification:[/] {MarketplaceHelpers.GetVerificationBadge(metadata.VerificationLevel)}\n" +
                      $"[dim]Homepage:[/] {metadata.Homepage}"))
        {
            Header = new PanelHeader($" {settings.WidgetId} "),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);

        // Show dependencies
        if (manifest.Dependencies != null &&
            (manifest.Dependencies.SystemCommands.Count > 0 || manifest.Dependencies.Optional.Count > 0))
        {
            AnsiConsole.WriteLine("\n[bold]Dependencies:[/]");

            if (manifest.Dependencies.SystemCommands.Count > 0)
            {
                AnsiConsole.WriteLine("  Required:");
                foreach (var cmd in manifest.Dependencies.SystemCommands)
                {
                    var result = dependencyChecker.CheckCommand(cmd);
                    var status = result.Found ? "[green]✓[/]" : "[red]✗[/]";
                    var path = result.Found ? $"[dim]({result.Path})[/]" : "[red](not found)[/]";
                    AnsiConsole.MarkupLine($"    {status} {cmd} {path}");
                }
            }

            if (manifest.Dependencies.Optional.Count > 0)
            {
                AnsiConsole.WriteLine("  Optional:");
                foreach (var cmd in manifest.Dependencies.Optional)
                {
                    var result = dependencyChecker.CheckCommand(cmd, isOptional: true);
                    var status = result.Found ? "[green]✓[/]" : "[dim]○[/]";
                    var path = result.Found ? $"[dim]({result.Path})[/]" : "[dim](not found)[/]";
                    AnsiConsole.MarkupLine($"    {status} {cmd} {path}");
                }
            }
        }

        // Show installation command
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]To install:[/] serverhub marketplace install {settings.WidgetId}");

        return 0;
    }
}
