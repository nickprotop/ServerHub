using Spectre.Console.Cli;
using ServerHub.Commands.Settings;
using ServerHub.Config;
using ServerHub.Services;
using ServerHub.Utils;
using ServerHub.Models;
using Spectre.Console;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ServerHub.Commands.Cli;

/// <summary>
/// Default command - runs the ServerHub dashboard
/// </summary>
public class DefaultCommand : AsyncCommand<DefaultCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DefaultCommandSettings settings)
    {
        // Handle utility commands first
        if (settings.Discover)
        {
            return await DiscoverWidgetsAsync();
        }

        if (settings.VerifyChecksums)
        {
            return await VerifyChecksumsAsync(settings.ConfigPath);
        }

        if (!string.IsNullOrEmpty(settings.InitConfig))
        {
            return await InitConfigAsync(settings.InitConfig, settings.WidgetsPath);
        }

        // Set custom widgets path if provided
        if (!string.IsNullOrEmpty(settings.WidgetsPath))
        {
            if (!Directory.Exists(settings.WidgetsPath))
            {
                Console.Error.WriteLine($"Error: Widgets path does not exist: {settings.WidgetsPath}");
                return 1;
            }
            WidgetPaths.SetCustomWidgetsPath(settings.WidgetsPath);
            Console.WriteLine($"Using custom widgets path: {settings.WidgetsPath}");
        }

        // Ensure directories exist
        WidgetPaths.EnsureDirectoriesExist();

        // Load configuration
        var configPath = settings.ConfigPath ?? ConfigManager.GetDefaultConfigPath();
        var configMgr = new ConfigManager();

        // Auto-create ONLY the default config path for first-time users
        var isDefaultPath = configPath == ConfigManager.GetDefaultConfigPath();

        if (!File.Exists(configPath))
        {
            if (isDefaultPath)
            {
                // First-time user: silent auto-create with production template
                Console.WriteLine("First-time setup: Creating default configuration...");
                configMgr.CreateDefaultConfig(configPath);
                Console.WriteLine($"Created: {configPath}");
                Console.WriteLine("Starting ServerHub...");
                Console.WriteLine();
            }
            else
            {
                // Custom path: fail with helpful error
                Console.Error.WriteLine($"Configuration file not found: {configPath}");
                Console.Error.WriteLine();
                Console.Error.WriteLine("To create this configuration file:");
                Console.Error.WriteLine($"  serverhub --init-config {Path.GetFileName(configPath)}");
                if (!string.IsNullOrEmpty(settings.WidgetsPath))
                    Console.Error.WriteLine($"            --widgets-path {settings.WidgetsPath}");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Or use the default configuration:");
                Console.Error.WriteLine("  serverhub");
                return 1;
            }
        }

        // Call Program.Main to start the dashboard
        // This delegates to the existing Program class which has all the UI logic
        var args = new List<string>();
        if (!string.IsNullOrEmpty(settings.ConfigPath))
            args.Add(settings.ConfigPath);
        if (settings.DevMode)
            args.Add("--dev-mode");
        if (!string.IsNullOrEmpty(settings.WidgetsPath))
        {
            args.Add("--widgets-path");
            args.Add(settings.WidgetsPath);
        }

        return await Program.RunDashboardAsync(args.ToArray(), configPath, settings.DevMode);
    }

    private static Task<int> DiscoverWidgetsAsync()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var customWidgetsPath = Path.Combine(home, ".config", "serverhub", "widgets");
        var configPath = ConfigManager.GetDefaultConfigPath();

        if (!Directory.Exists(customWidgetsPath))
        {
            Console.WriteLine($"No custom widgets directory found: {customWidgetsPath}");
            Console.WriteLine("Create it and add your widget scripts there.");
            return Task.FromResult(0);
        }

        // Load or create config
        var configManager = new ConfigManager();
        ServerHubConfig? config = null;

        if (File.Exists(configPath))
        {
            try
            {
                config = configManager.LoadConfig(configPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not load existing config: {ex.Message}");
                Console.WriteLine("A new config will be created if you approve any widgets.\n");
            }
        }

        // Discover unconfigured widgets
        var discoveredWidgets = WidgetConfigurationHelper.DiscoverUnconfiguredWidgets(customWidgetsPath, config);

        if (discoveredWidgets.Count == 0)
        {
            Console.WriteLine("No unconfigured widgets found.");
            return Task.FromResult(0);
        }

        Console.WriteLine($"Found {discoveredWidgets.Count} unconfigured widget(s):\n");

        bool configModified = false;
        int addedCount = 0;

        foreach (var widget in discoveredWidgets)
        {
            var filename = widget.RelativePath;
            var fileInfo = new FileInfo(widget.FullPath!);
            var checksum = widget.Config.Sha256!;

            Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.WriteLine($"Widget: {filename}");
            Console.WriteLine($"Path:   {widget.FullPath}");
            Console.WriteLine($"Size:   {fileInfo.Length} bytes");
            Console.WriteLine($"SHA256: {checksum}");
            Console.WriteLine();

            // Show preview (first 50 lines for text files)
            if (IsTextFile(widget.FullPath!))
            {
                Console.WriteLine("Preview:");
                var lines = File.ReadLines(widget.FullPath!).Take(50).ToArray();
                for (int i = 0; i < lines.Length; i++)
                {
                    Console.WriteLine($"    {i + 1,3}  {lines[i]}");
                }
                if (File.ReadLines(widget.FullPath!).Count() > 50)
                    Console.WriteLine("    ... (truncated)");
            }
            else
            {
                Console.WriteLine("[Binary file - no preview]");
            }

            Console.WriteLine();
            Console.Write($"Add '{filename}' to config? [y/N] ");
            var response = Console.ReadLine();

            if (response?.ToLower() == "y")
            {
                // Create config if it doesn't exist
                if (config == null)
                {
                    config = new ServerHubConfig
                    {
                        Widgets = new Dictionary<string, WidgetConfig>(),
                        Layout = new LayoutConfig { Order = new List<string>() }
                    };
                }

                var id = Path.GetFileNameWithoutExtension(widget.FullPath!);

                // Ensure unique ID
                var baseId = id;
                int suffix = 1;
                while (config.Widgets.ContainsKey(id))
                {
                    id = $"{baseId}_{suffix++}";
                }

                // Add to config using helper
                WidgetConfigurationHelper.AddWidget(config, id, widget.Config, addToLayout: true);

                AnsiConsole.MarkupLine($"[green]Added '{id}' to config[/]");
                configModified = true;
                addedCount++;
            }
        }

        if (!configModified || config == null)
        {
            Console.WriteLine("\nNo widgets added.");
            return Task.FromResult(0);
        }

        // Save the config
        try
        {
            configManager.SaveConfig(config, configPath);
            Console.WriteLine();
            AnsiConsole.MarkupLine($"[green]Config saved: {configPath}[/]");
            Console.WriteLine($"Added {addedCount} widget(s) with checksums.");
            Console.WriteLine("\nRun 'serverhub' to start with the new widgets.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error saving config: {ex.Message}");
            return Task.FromResult(1);
        }

        return Task.FromResult(0);
    }

    private static Task<int> VerifyChecksumsAsync(string? configPath)
    {
        configPath ??= ConfigManager.GetDefaultConfigPath();

        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Config file not found: {configPath}");
            return Task.FromResult(1);
        }

        var configManager = new ConfigManager();
        ServerHubConfig config;
        try
        {
            config = configManager.LoadConfig(configPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Config error: {ex.Message}");
            return Task.FromResult(1);
        }

        var bundledPath = WidgetPaths.GetBundledWidgetsDirectory();
        var bundledChecksums = ServerHub.Config.BundledWidgets.Checksums;
        int passed = 0, failed = 0, missing = 0;

        Console.WriteLine("Verifying widget checksums...\n");

        foreach (var (id, widget) in config.Widgets)
        {
            var resolved = WidgetPaths.ResolveWidgetPath(widget.Path, widget.Location);

            if (resolved == null || !File.Exists(resolved))
            {
                AnsiConsole.MarkupLine($"  {id,-20} [red]NOT FOUND[/]");
                missing++;
                continue;
            }

            var actual = ScriptValidator.CalculateChecksum(resolved);
            string? expected = null;
            string source = "";

            // Check config sha256
            if (!string.IsNullOrEmpty(widget.Sha256))
            {
                expected = widget.Sha256;
                source = "config";
            }
            // Check bundled
            else if (resolved.StartsWith(bundledPath, StringComparison.Ordinal))
            {
                var filename = Path.GetFileName(resolved);
                if (bundledChecksums.TryGetValue(filename, out var bundledChecksum))
                {
                    expected = bundledChecksum;
                    source = "bundled";
                }
            }

            if (expected == null)
            {
                AnsiConsole.MarkupLine($"  {id,-20} [yellow]NO CHECKSUM[/]");
                continue;
            }

            if (actual == expected)
            {
                AnsiConsole.MarkupLine($"  {id,-20} [green]PASS[/] ({source})");
                passed++;
            }
            else
            {
                AnsiConsole.MarkupLine($"  {id,-20} [red]FAIL[/] ({source})");
                failed++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Results: {passed} passed, {failed} failed, {missing} missing");

        return Task.FromResult(failed > 0 || missing > 0 ? 1 : 0);
    }

    private static Task<int> InitConfigAsync(string configPath, string? customWidgetsPath)
    {
        if (File.Exists(configPath))
        {
            Console.Error.WriteLine($"Configuration file already exists: {configPath}");
            Console.Error.WriteLine("Delete it first or choose a different name.");
            return Task.FromResult(1);
        }

        Console.WriteLine("Discovering widgets...\n");

        // Start with production config template (keep it exactly as-is)
        var configManager = new ConfigManager();
        ServerHubConfig config;
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .WithTypeConverter(new WidgetLocationTypeConverter())
                .IgnoreUnmatchedProperties()
                .Build();
            config = deserializer.Deserialize<ServerHubConfig>(DefaultConfig.YamlContent);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to parse production config template: {ex.Message}");
            return Task.FromResult(1);
        }

        int bundledCount = config.Widgets.Count;  // All bundled widgets from template
        int customCount = 0;

        // Scan for custom widgets to add
        var userCustomDir = WidgetPaths.GetUserCustomWidgetsDirectory();
        var customPath = !string.IsNullOrEmpty(customWidgetsPath)
            ? Path.GetFullPath(customWidgetsPath)
            : null;

        // Scan user custom directory
        if (Directory.Exists(userCustomDir))
        {
            foreach (var file in Directory.GetFiles(userCustomDir).OrderBy(f => f))
            {
                if (WidgetConfigurationHelper.IsExecutable(file))
                {
                    var filename = Path.GetFileName(file);
                    var id = GenerateUniqueId(Path.GetFileNameWithoutExtension(filename), config.Widgets);
                    var widgetConfig = WidgetConfigurationHelper.CreateWidgetConfig(
                        filename,
                        WidgetLocation.Custom,
                        includeChecksum: false);  // SECURITY: No checksum - requires --dev-mode or --discover
                    WidgetConfigurationHelper.AddWidget(config, id, widgetConfig, addToLayout: true);
                    customCount++;
                }
            }
        }

        // Scan --widgets-path directory
        if (customPath != null && Directory.Exists(customPath))
        {
            foreach (var file in Directory.GetFiles(customPath).OrderBy(f => f))
            {
                if (WidgetConfigurationHelper.IsExecutable(file))
                {
                    var filename = Path.GetFileName(file);
                    var id = GenerateUniqueId(Path.GetFileNameWithoutExtension(filename), config.Widgets);
                    var widgetConfig = WidgetConfigurationHelper.CreateWidgetConfig(
                        filename,
                        WidgetLocation.Custom,
                        includeChecksum: false);  // SECURITY: No checksum - requires --dev-mode or --discover
                    WidgetConfigurationHelper.AddWidget(config, id, widgetConfig, addToLayout: true);
                    customCount++;
                }
            }
        }

        // Create directory if needed
        var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath));
        if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        // Save config
        try
        {
            configManager.SaveConfig(config, configPath);

            Console.WriteLine($"✓ Included {bundledCount} bundled widget(s) from template");
            if (customCount > 0)
                Console.WriteLine($"✓ Added {customCount} custom widget(s)");

            Console.WriteLine();
            AnsiConsole.MarkupLine($"[green]Generated {configPath} with {bundledCount + customCount} widgets[/]");

            // Security note for custom widgets
            if (customCount > 0)
            {
                Console.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Note:[/] Custom widgets have no checksums (security).");
                Console.WriteLine("To run securely:");
                Console.WriteLine("  1. Run: serverhub --discover");
                Console.WriteLine("     (reviews and adds checksums for custom widgets)");
                Console.WriteLine("  2. Or use: --dev-mode flag to skip checksum validation");
            }

            // Show command to run
            Console.WriteLine("\nTo start ServerHub:");
            var devModeFlag = customCount > 0 ? " --dev-mode" : "";
            if (!string.IsNullOrEmpty(customWidgetsPath))
                Console.WriteLine($"  serverhub --widgets-path {customWidgetsPath} {Path.GetFileName(configPath)}{devModeFlag}");
            else if (configPath != ConfigManager.GetDefaultConfigPath())
                Console.WriteLine($"  serverhub {Path.GetFileName(configPath)}{devModeFlag}");
            else
                Console.WriteLine($"  serverhub{devModeFlag}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return Task.FromResult(1);
        }

        return Task.FromResult(0);
    }

    private static string GenerateUniqueId(string baseId, Dictionary<string, WidgetConfig> existingWidgets)
    {
        var id = baseId;
        int suffix = 1;
        while (existingWidgets.ContainsKey(id))
        {
            id = $"{baseId}_{suffix++}";
        }
        return id;
    }

    private static bool IsTextFile(string path)
    {
        var ext = Path.GetExtension(path).ToLower();
        return ext switch
        {
            ".sh" or ".bash" or ".py" or ".rb" or ".pl" or ".js" or ".ts" => true,
            _ => false
        };
    }
}
