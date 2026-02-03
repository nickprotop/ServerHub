using Spectre.Console;
using Spectre.Console.Cli;
using ServerHub.Commands.Settings.Marketplace;
using ServerHub.Config;
using ServerHub.Marketplace.Services;
using ServerHub.Services;
using ServerHub.Utils;

namespace ServerHub.Commands.Cli.Marketplace;

/// <summary>
/// Marketplace update command - updates a widget to the latest or specified version
/// </summary>
public class UpdateCommand : AsyncCommand<UpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, UpdateSettings settings)
    {
        var installPath = WidgetPaths.GetMarketplaceInstallPath();
        var configPath = ConfigManager.GetDefaultConfigPath();
        var manager = new MarketplaceManager(installPath, configPath);

        AnsiConsole.MarkupLine($"[cyan]Checking widget:[/] {settings.WidgetId}\n");

        // Get all widgets to find this one
        var allWidgets = await manager.GetAllWidgetsAsync();
        var widget = allWidgets.FirstOrDefault(w => w.Id == settings.WidgetId);

        if (widget == null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Widget '{settings.WidgetId}' not found in marketplace");
            return 1;
        }

        if (widget.Status != MarketplaceManager.WidgetStatus.UpdateAvailable &&
            widget.Status != MarketplaceManager.WidgetStatus.Installed)
        {
            AnsiConsole.MarkupLine($"[yellow]Widget '{settings.WidgetId}' is not installed[/]");
            AnsiConsole.MarkupLine($"[dim]Use 'serverhub marketplace install {settings.WidgetId}' to install it[/]");
            return 1;
        }

        if (!widget.HasUpdate && settings.Version == null)
        {
            AnsiConsole.MarkupLine($"[green]Widget '{settings.WidgetId}' is already up to date (v{widget.InstalledVersion})[/]");
            return 0;
        }

        var targetVersion = settings.Version ?? widget.LatestVersion;

        // Get manifest for changelog and details
        var manifest = await manager.GetWidgetManifestAsync(widget.ManifestUrl);
        if (manifest == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Failed to fetch widget manifest");
            return 1;
        }

        var versionInfo = manifest.Versions.FirstOrDefault(v => v.Version == targetVersion);
        if (versionInfo == null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Version '{targetVersion}' not found");
            return 1;
        }

        // Show update information
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]{widget.Name}[/]");
        AnsiConsole.MarkupLine($"Current: [yellow]{widget.InstalledVersion}[/] → New: [green]{targetVersion}[/]");

        if (versionInfo.Released != DateTime.MinValue)
        {
            AnsiConsole.MarkupLine($"Released: {versionInfo.Released:yyyy-MM-dd}");
        }

        // Show changelog if available
        if (!string.IsNullOrEmpty(versionInfo.Changelog))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Changelog:[/]");
            foreach (var line in versionInfo.Changelog.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    AnsiConsole.MarkupLine($"  {line.Trim()}");
                }
            }
        }

        // Confirm update (unless --yes flag)
        if (!settings.SkipConfirmation)
        {
            AnsiConsole.WriteLine();
            if (!AnsiConsole.Confirm("Continue with update?", defaultValue: true))
            {
                AnsiConsole.MarkupLine("[yellow]Update cancelled[/]");
                return 0;
            }
        }

        // Download and install new version
        AnsiConsole.WriteLine();
        var result = await AnsiConsole.Status()
            .StartAsync("Updating widget...", async ctx =>
            {
                ctx.Spinner(Spinner.Known.Dots);
                ctx.Status("Downloading...");
                return await manager.InstallWidgetAsync(settings.WidgetId, settings.Version ?? targetVersion);
            });

        if (!result.Success)
        {
            AnsiConsole.MarkupLine($"[red]Update failed:[/] {result.ErrorMessage}");
            return 1;
        }

        // Update config with new version and SHA256
        bool configUpdated = ConfigHelper.UpdateWidgetVersionInConfig(
            configPath,
            settings.WidgetId,
            targetVersion,
            result.Sha256 ?? ""
        );

        if (!configUpdated)
        {
            AnsiConsole.MarkupLine("[yellow]Warning: Could not update config file automatically[/]");
            AnsiConsole.MarkupLine($"[dim]Please update marketplace_version to '{targetVersion}' and sha256 to '{result.Sha256}' manually[/]");
        }

        // Success
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]✓ Successfully updated {settings.WidgetId} to v{targetVersion}[/]");
        AnsiConsole.WriteLine($"  Location: {result.InstalledPath}");
        AnsiConsole.WriteLine($"  SHA256: {result.Sha256}");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Restart ServerHub or press F5 to reload the widget[/]");

        return 0;
    }
}
