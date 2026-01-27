using Spectre.Console;
using ServerHub.Marketplace.Models;
using ServerHub.Marketplace.Services;
using ServerHub.Utils;
using ServerHub.Services;

namespace ServerHub.Commands;

/// <summary>
/// Handles marketplace CLI commands
/// </summary>
public class MarketplaceCommand
{
    private readonly RegistryClient _registryClient;
    private readonly WidgetInstaller _installer;
    private readonly DependencyChecker _dependencyChecker;
    private readonly string _installPath;
    private readonly string _configPath;

    public MarketplaceCommand(string installPath, string configPath)
    {
        _installPath = installPath;
        _configPath = configPath;
        _registryClient = new RegistryClient();
        _installer = new WidgetInstaller(_registryClient, installPath);
        _dependencyChecker = new DependencyChecker();
    }

    /// <summary>
    /// Executes a marketplace command
    /// </summary>
    public async Task<int> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return 1;
        }

        var subcommand = args[0].ToLower();

        return subcommand switch
        {
            "search" => await SearchAsync(args.Skip(1).ToArray()),
            "list" => await ListAsync(args.Skip(1).ToArray()),
            "info" => await InfoAsync(args.Skip(1).ToArray()),
            "install" => await InstallAsync(args.Skip(1).ToArray()),
            "refresh" => await RefreshAsync(),
            "list-installed" => await ListInstalledAsync(),
            "help" or "--help" or "-h" => ShowHelpWithReturn(),
            _ => UnknownCommand(subcommand)
        };
    }

    private async Task<int> SearchAsync(string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] search requires a query argument");
            AnsiConsole.WriteLine("Usage: serverhub marketplace search <query>");
            return 1;
        }

        var query = string.Join(" ", args);
        AnsiConsole.MarkupLine($"Searching marketplace for: [cyan]{query}[/]");

        var results = await _registryClient.SearchWidgetsAsync(query);

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
            var badge = GetVerificationBadge(widget.VerificationLevel);
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

    private async Task<int> ListAsync(string[] args)
    {
        string? category = null;

        // Parse --category flag
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--category" && i + 1 < args.Length)
            {
                category = args[i + 1];
                break;
            }
        }

        AnsiConsole.MarkupLine("[cyan]Fetching marketplace widgets...[/]");

        var index = await _registryClient.FetchRegistryIndexAsync();
        if (index == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Failed to fetch marketplace index");
            return 1;
        }

        var widgets = index.Widgets;

        if (category != null)
        {
            widgets = widgets.Where(w =>
                w.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();

            if (widgets.Count == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]No widgets found in category '{category}'[/]");
                return 0;
            }

            AnsiConsole.WriteLine($"\nWidgets in category '{category}' ({widgets.Count}):\n");
        }
        else
        {
            AnsiConsole.WriteLine($"\nAvailable widgets ({widgets.Count}):\n");
        }

        // Group by category
        var grouped = widgets.GroupBy(w => w.Category).OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            AnsiConsole.MarkupLine($"[bold cyan]{group.Key}[/]");

            foreach (var widget in group.OrderBy(w => w.Name))
            {
                var badge = GetVerificationBadge(widget.VerificationLevel);
                AnsiConsole.MarkupLine($"  [yellow]{widget.Id}[/] - {widget.Name} v{widget.LatestVersion} {badge}");
            }

            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine("[dim]Use 'serverhub marketplace info <widget-id>' for more details[/]");
        return 0;
    }

    private async Task<int> InfoAsync(string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] info requires a widget ID");
            AnsiConsole.WriteLine("Usage: serverhub marketplace info <widget-id>");
            return 1;
        }

        var widgetId = args[0];
        AnsiConsole.MarkupLine($"Fetching information for: [cyan]{widgetId}[/]\n");

        var registryWidget = await _registryClient.GetWidgetByIdAsync(widgetId);
        if (registryWidget == null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Widget '{widgetId}' not found in marketplace");
            return 1;
        }

        var manifest = await _registryClient.FetchWidgetManifestAsync(registryWidget.ManifestUrl);
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
                      $"[dim]Verification:[/] {GetVerificationBadge(metadata.VerificationLevel)}\n" +
                      $"[dim]Homepage:[/] {metadata.Homepage}"))
        {
            Header = new PanelHeader($" {widgetId} "),
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
                    var result = _dependencyChecker.CheckCommand(cmd);
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
                    var result = _dependencyChecker.CheckCommand(cmd, isOptional: true);
                    var status = result.Found ? "[green]✓[/]" : "[dim]○[/]";
                    var path = result.Found ? $"[dim]({result.Path})[/]" : "[dim](not found)[/]";
                    AnsiConsole.MarkupLine($"    {status} {cmd} {path}");
                }
            }
        }

        // Show installation command
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]To install:[/] serverhub marketplace install {widgetId}");

        return 0;
    }

    private async Task<int> InstallAsync(string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] install requires a widget ID");
            AnsiConsole.WriteLine("Usage: serverhub marketplace install <widget-id>");
            return 1;
        }

        var widgetId = args[0];
        string? version = null;

        // Parse version (e.g., "widget@1.0.0")
        if (widgetId.Contains('@'))
        {
            var parts = widgetId.Split('@');
            widgetId = parts[0];
            version = parts[1];
        }

        AnsiConsole.MarkupLine($"[cyan]Resolving widget:[/] {widgetId}");

        var registryWidget = await _registryClient.GetWidgetByIdAsync(widgetId);
        if (registryWidget == null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Widget '{widgetId}' not found in marketplace");
            return 1;
        }

        var manifest = await _registryClient.FetchWidgetManifestAsync(registryWidget.ManifestUrl);
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
        AnsiConsole.WriteLine($"  Verification: {GetVerificationBadge(metadata.VerificationLevel)}");
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
            var depResults = _dependencyChecker.CheckDependencies(manifest.Dependencies);
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
                return await _installer.InstallWidgetAsync(widgetId, version);
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
        if (ConfigHelper.WidgetExistsInConfig(_configPath, widgetId))
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

            // Determine widget filename for config (just the filename, not full path)
            var widgetFileName = Path.GetFileName(result.InstalledPath) ?? "";

            // Add to config
            var added = ConfigHelper.AddWidgetToConfig(
                _configPath,
                widgetId,
                widgetFileName,
                result.Sha256 ?? "",
                refresh
            );

            if (added)
            {
                AnsiConsole.MarkupLine($"[green]✓ Added '{widgetId}' to {_configPath}[/]");
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
                AnsiConsole.WriteLine($"Add this to {_configPath}:");
                AnsiConsole.WriteLine();
                ShowManualConfig(widgetId, widgetFileName, result.Sha256 ?? "", refresh);
            }
        }
        else
        {
            // User declined - show manual instructions
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Manual configuration:[/]");
            AnsiConsole.WriteLine($"Add this to {_configPath}:");
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

    private async Task<int> RefreshAsync()
    {
        AnsiConsole.MarkupLine("[cyan]Refreshing marketplace cache...[/]");

        var index = await _registryClient.FetchRegistryIndexAsync(forceRefresh: true);
        if (index == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Failed to refresh marketplace");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Cache refreshed. {index.Widgets.Count} widgets available.");
        return 0;
    }

    private Task<int> ListInstalledAsync()
    {
        if (!Directory.Exists(_installPath))
        {
            AnsiConsole.MarkupLine("[yellow]No marketplace widgets installed.[/]");
            return Task.FromResult(0);
        }

        var files = Directory.GetFiles(_installPath, "*", SearchOption.TopDirectoryOnly)
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.Name)
            .ToList();

        if (files.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No marketplace widgets installed.[/]");
            return Task.FromResult(0);
        }

        AnsiConsole.WriteLine($"\nInstalled marketplace widgets ({files.Count}):\n");

        var table = new Table();
        table.AddColumn("File");
        table.AddColumn("Size");
        table.AddColumn("Modified");

        foreach (var file in files)
        {
            table.AddRow(
                file.Name,
                FormatFileSize(file.Length),
                file.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]Location: {_installPath}[/]");

        return Task.FromResult(0);
    }

    private void ShowHelp()
    {
        AnsiConsole.WriteLine("ServerHub Marketplace - Community widget repository\n");
        AnsiConsole.WriteLine("Usage: serverhub marketplace <command> [arguments]\n");
        AnsiConsole.WriteLine("Commands:");
        AnsiConsole.WriteLine("  search <query>        Search for widgets");
        AnsiConsole.WriteLine("  list [--category X]   List all available widgets");
        AnsiConsole.WriteLine("  info <widget-id>      Show detailed widget information");
        AnsiConsole.WriteLine("  install <widget-id>   Install a widget from the marketplace");
        AnsiConsole.WriteLine("  list-installed        Show installed marketplace widgets");
        AnsiConsole.WriteLine("  refresh               Refresh the local marketplace cache");
        AnsiConsole.WriteLine("  help                  Show this help message\n");
        AnsiConsole.WriteLine("Examples:");
        AnsiConsole.WriteLine("  serverhub marketplace search api");
        AnsiConsole.WriteLine("  serverhub marketplace info username/widget-name");
        AnsiConsole.WriteLine("  serverhub marketplace install username/widget-name");
        AnsiConsole.WriteLine("  serverhub marketplace install username/widget-name@1.0.0");
    }

    private int ShowHelpWithReturn()
    {
        ShowHelp();
        return 0;
    }

    private int UnknownCommand(string command)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] Unknown marketplace command: {command}");
        AnsiConsole.WriteLine();
        ShowHelp();
        return 1;
    }

    private static string GetVerificationBadge(VerificationLevel level)
    {
        return level switch
        {
            VerificationLevel.Verified => "[green]✓ Verified[/]",
            VerificationLevel.Community => "[yellow]⚡ Community[/]",
            VerificationLevel.Unverified => "[red]⚠ Unverified[/]",
            _ => "[dim]Unknown[/]"
        };
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.#} {sizes[order]}";
    }
}
