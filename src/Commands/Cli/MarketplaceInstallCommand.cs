using Spectre.Console;
using Spectre.Console.Cli;
using ServerHub.Commands.Settings;
using ServerHub.Config;
using ServerHub.Marketplace.Models;
using ServerHub.Marketplace.Services;
using ServerHub.Services;
using ServerHub.Utils;

namespace ServerHub.Commands.Cli;

/// <summary>
/// Marketplace install command - installs a widget from the marketplace
/// </summary>
public class MarketplaceInstallCommand : AsyncCommand<MarketplaceInstallSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, MarketplaceInstallSettings settings)
    {
        var installPath = WidgetPaths.GetMarketplaceInstallPath();
        var configPath = ConfigManager.GetDefaultConfigPath();

        var registryClient = new RegistryClient();
        var installer = new WidgetInstaller(registryClient, installPath);
        var dependencyChecker = new DependencyChecker();

        var widgetId = settings.WidgetId;
        string? version = null;

        // Parse version (e.g., "widget@1.0.0")
        if (widgetId.Contains('@'))
        {
            var parts = widgetId.Split('@');
            widgetId = parts[0];
            version = parts[1];
        }

        AnsiConsole.MarkupLine($"[cyan]Resolving widget:[/] {widgetId}");

        var registryWidget = await registryClient.GetWidgetByIdAsync(widgetId);
        if (registryWidget == null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Widget '{widgetId}' not found in marketplace");
            return 1;
        }

        var manifest = await registryClient.FetchWidgetManifestAsync(registryWidget.ManifestUrl);
        if (manifest == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Failed to fetch widget manifest");
            return 1;
        }

        var targetVersion = version != null
            ? manifest.Versions.FirstOrDefault(v => v.Version == version)
            : manifest.LatestVersion;

        if (targetVersion == null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Version '{version}' not found");
            return 1;
        }

        var metadata = manifest.Metadata;

        // Show widget info
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]{metadata.Name}[/] v{targetVersion.Version} by {metadata.Author}");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]Description:[/]");
        AnsiConsole.WriteLine($"  {metadata.Description}");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]Details:[/]");
        AnsiConsole.WriteLine($"  Category: {metadata.Category}");
        AnsiConsole.WriteLine($"  License: {metadata.License}");
        AnsiConsole.WriteLine($"  Verification: {MarketplaceHelpers.GetVerificationBadge(metadata.VerificationLevel)}");
        AnsiConsole.WriteLine($"  Repository: {metadata.Homepage}");

        // Show warning for unverified widgets
        if (metadata.VerificationLevel == VerificationLevel.Unverified)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow bold]⚠  WARNING: This widget is UNVERIFIED[/]");
            AnsiConsole.MarkupLine("[yellow]  - Code has not been reviewed by ServerHub maintainers[/]");
            AnsiConsole.MarkupLine($"[yellow]  - Author: {metadata.Author}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Review the code before installing:[/]");
            AnsiConsole.MarkupLine($"[dim]  Visit: {metadata.Homepage}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]⚠  YOU are responsible for reviewing code from unverified sources.[/]");
            AnsiConsole.WriteLine();

            if (!AnsiConsole.Confirm("Install anyway?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[yellow]Installation cancelled.[/]");
                return 0;
            }
        }

        // Check dependencies
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[cyan]Checking dependencies...[/]");

        if (manifest.Dependencies != null)
        {
            var depResults = dependencyChecker.CheckDependencies(manifest.Dependencies);
            var missing = depResults.Where(r => !r.Found && !r.IsOptional).ToList();

            foreach (var dep in depResults)
            {
                var status = dep.Found ? "[green]✓[/]" : (dep.IsOptional ? "[dim]○[/]" : "[red]✗[/]");
                var location = dep.Found ? $"found at {dep.Path}" : "not found";
                var optional = dep.IsOptional ? " (optional)" : "";
                AnsiConsole.MarkupLine($"  {status} {dep.Command} {location}{optional}");
            }

            if (missing.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[red]Error:[/] Missing required dependencies. Please install them first.");
                return 1;
            }
        }
        else
        {
            AnsiConsole.MarkupLine("  [dim]No dependencies required[/]");
        }

        // Download and install
        AnsiConsole.WriteLine();
        var result = await AnsiConsole.Status()
            .StartAsync("Installing widget...", async ctx =>
            {
                ctx.Spinner(Spinner.Known.Dots);
                ctx.Status("Downloading...");
                return await installer.InstallWidgetAsync(widgetId, version);
            });

        if (!result.Success)
        {
            AnsiConsole.MarkupLine($"[red]Installation failed:[/] {result.ErrorMessage}");
            return 1;
        }

        // Success
        AnsiConsole.MarkupLine($"[green]✓ Successfully installed {widgetId} v{targetVersion.Version}[/]");
        AnsiConsole.WriteLine($"  Location: {result.InstalledPath}");
        AnsiConsole.WriteLine($"  SHA256: {result.Sha256}");
        AnsiConsole.WriteLine();

        // Prompt to add to config
        if (ConfigHelper.WidgetExistsInConfig(configPath, widgetId))
        {
            AnsiConsole.MarkupLine($"[yellow]Widget '{widgetId}' already exists in config.[/]");
        }
        else if (AnsiConsole.Confirm($"Add '{widgetId}' to config file?", defaultValue: true))
        {
            // Prompt for refresh interval
            var defaultRefresh = manifest.Config?.DefaultRefresh ?? 10;
            var refresh = AnsiConsole.Prompt(
                new TextPrompt<int>($"Refresh interval (seconds)?")
                    .DefaultValue(defaultRefresh)
                    .ValidationErrorMessage("[red]Please enter a valid number[/]")
                    .Validate(r => r >= 1 && r <= 3600
                        ? Spectre.Console.ValidationResult.Success()
                        : Spectre.Console.ValidationResult.Error("[red]Refresh interval must be between 1 and 3600 seconds[/]"))
            );

            // Prompt for expanded refresh interval (optional)
            int? expandedRefresh = null;
            if (manifest.Config?.DefaultExpandedRefresh.HasValue == true)
            {
                var defaultExpandedRefresh = manifest.Config.DefaultExpandedRefresh.Value;
                var useExpandedRefresh = AnsiConsole.Confirm(
                    $"Set expanded view refresh interval? (default: {defaultExpandedRefresh}s)",
                    defaultValue: true
                );

                if (useExpandedRefresh)
                {
                    expandedRefresh = AnsiConsole.Prompt(
                        new TextPrompt<int>($"Expanded view refresh interval (seconds)?")
                            .DefaultValue(defaultExpandedRefresh)
                            .ValidationErrorMessage("[red]Please enter a valid number[/]")
                            .Validate(r => r >= 1 && r <= 3600
                                ? Spectre.Console.ValidationResult.Success()
                                : Spectre.Console.ValidationResult.Error("[red]Refresh interval must be between 1 and 3600 seconds[/]"))
                    );
                }
            }

            // Determine widget filename for config (just the filename, not full path)
            var widgetFileName = Path.GetFileName(result.InstalledPath) ?? "";

            // Add to config
            var added = ConfigHelper.AddWidgetToConfig(
                configPath,
                widgetId,
                widgetFileName,
                result.Sha256 ?? "",
                refresh,
                expandedRefresh,
                source: "marketplace",
                marketplaceId: manifest.Metadata.Id,
                marketplaceVersion: targetVersion.Version
            );

            if (added)
            {
                AnsiConsole.MarkupLine($"[green]✓ Added '{widgetId}' to {configPath}[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Next steps:[/]");
                AnsiConsole.WriteLine("  • Restart ServerHub or press F5 to load the widget");
                AnsiConsole.WriteLine($"  • Press F2 in ServerHub to customize layout/settings");
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]Failed to add widget to config[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Manual configuration:[/]");
                AnsiConsole.WriteLine($"Add this to {configPath}:");
                AnsiConsole.WriteLine();
                ShowManualConfig(widgetId, widgetFileName, result.Sha256 ?? "", refresh);
            }
        }
        else
        {
            // User declined - show manual instructions
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Manual configuration:[/]");
            AnsiConsole.WriteLine($"Add this to {configPath}:");
            AnsiConsole.WriteLine();
            var widgetFileName = Path.GetFileName(result.InstalledPath) ?? "";
            ShowManualConfig(widgetId, widgetFileName, result.Sha256 ?? "", manifest.Config?.DefaultRefresh ?? 10);
        }

        return 0;
    }

    private void ShowManualConfig(string widgetId, string widgetPath, string sha256, int refresh)
    {
        AnsiConsole.MarkupLine("[dim]widgets:[/]");
        AnsiConsole.MarkupLine($"[dim]  {widgetId}:[/]");
        AnsiConsole.MarkupLine($"[dim]    path: {widgetPath}[/]");
        AnsiConsole.MarkupLine($"[dim]    refresh: {refresh}[/]");
        AnsiConsole.MarkupLine($"[dim]    sha256: \"{sha256}\"[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Then:[/]");
        AnsiConsole.WriteLine("  • Restart ServerHub or press F5 to load the widget");
    }
}
