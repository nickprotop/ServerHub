// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.CommandLine;
using ServerHub.Config;
using ServerHub.Models;
using ServerHub.Services;
using ServerHub.UI;
using ServerHub.Utils;
using ServerHub.Exceptions;
using ServerHub.Commands;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drivers;
using Spectre.Console;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ServerHub;

public class Program
{
    private static ConsoleWindowSystem? _windowSystem;
    private static Window? _mainWindow;
    private static ServerHubConfig? _config;
    private static readonly Dictionary<string, WidgetData> _widgetDataCache = new();
    private static readonly Dictionary<string, Task> _widgetTasks = new();
    private static readonly Dictionary<string, PeriodicTimer> _widgetTimers = new();
    private static CancellationTokenSource _cancellationTokenSource = new();
    private static WidgetRenderer? _renderer;
    private static LayoutEngine? _layoutEngine;
    private static ScriptExecutor? _executor;
    private static WidgetProtocolParser? _parser;
    private static int _lastTerminalWidth = 0;
    private static bool _isPaused = false;
    private static readonly Dictionary<string, DateTime> _lastSuccessfulUpdate = new();
    private static readonly Dictionary<string, int> _consecutiveErrors = new();
    private static readonly string[] _spinnerFrames = { "◐", "◓", "◑", "◒" };
    private static int _spinnerFrame = 0;
    private static readonly Dictionary<string, bool> _isRefreshing = new();
    private static FocusManager? _focusManager;
    private static readonly Dictionary<string, WidgetData> _fullWidgetData = new();
    private static string? _openModalWidgetId = null;
    private static WidgetRefreshService? _refreshService;
    private static bool _devMode = false;
    private static string? _configPath;
    private static DateTime _lastConfigLoadTime = DateTime.MinValue;
    private static CommandPaletteService? _commandPaletteService;
    private static StatusBarManager? _statusBarManager;
    private static int _initialLoadPendingCount = 0;
    private static readonly object _initialLoadLock = new object();

    public static async Task<int> Main(string[] args)
    {
        // Initialize SQLite for embedded single-file deployment
        SQLitePCL.Batteries_V2.Init();

        // Create root command (default dashboard command)
        var rootCommand = new RootCommand("ServerHub - Terminal-based server monitoring dashboard");

        // Default command arguments and options
        var configArgument = new Argument<string?>(
            name: "config",
            description: "Path to configuration file",
            getDefaultValue: () => null);

        var widgetsPathOption = new Option<string?>(
            name: "--widgets-path",
            description: "Override widget directory path");

        var devModeOption = new Option<bool>(
            name: "--dev-mode",
            description: "Enable development mode (disables custom widget checksum validation)");

        var discoverOption = new Option<bool>(
            name: "--discover",
            description: "Discover and list all available widgets, then exit");

        var verifyChecksumsOption = new Option<bool>(
            name: "--verify-checksums",
            description: "Verify bundled widget checksums, then exit");

        var initConfigOption = new Option<string?>(
            name: "--init-config",
            description: "Initialize a new configuration file at specified path");

        rootCommand.AddArgument(configArgument);
        rootCommand.AddOption(widgetsPathOption);
        rootCommand.AddOption(devModeOption);
        rootCommand.AddOption(discoverOption);
        rootCommand.AddOption(verifyChecksumsOption);
        rootCommand.AddOption(initConfigOption);

        rootCommand.SetHandler(async (string? config, string? widgetsPath, bool devMode, bool discover, bool verifyChecksums, string? initConfig) =>
        {
            var exitCode = await Commands.Cli.DefaultCommand.ExecuteAsync(
                config, widgetsPath, devMode, discover, verifyChecksums, initConfig);
            Environment.ExitCode = exitCode;
        }, configArgument, widgetsPathOption, devModeOption, discoverOption, verifyChecksumsOption, initConfigOption);

        // Marketplace commands
        var marketplaceCommand = new Command("marketplace", "Manage marketplace widgets");
        rootCommand.AddCommand(marketplaceCommand);

        // marketplace search
        var marketplaceSearchCommand = new Command("search", "Search for community widgets");
        var searchQueryArgument = new Argument<string>("query", "Search query for widgets");
        marketplaceSearchCommand.AddArgument(searchQueryArgument);
        marketplaceSearchCommand.SetHandler(async (string query) =>
        {
            var exitCode = await Commands.Cli.Marketplace.SearchCommand.ExecuteAsync(query);
            Environment.ExitCode = exitCode;
        }, searchQueryArgument);
        marketplaceCommand.AddCommand(marketplaceSearchCommand);

        // marketplace list
        var marketplaceListCommand = new Command("list", "List all available widgets");
        marketplaceListCommand.SetHandler(async () =>
        {
            var exitCode = await Commands.Cli.Marketplace.ListCommand.ExecuteAsync();
            Environment.ExitCode = exitCode;
        });
        marketplaceCommand.AddCommand(marketplaceListCommand);

        // marketplace info
        var marketplaceInfoCommand = new Command("info", "Show widget details");
        var infoWidgetIdArgument = new Argument<string>("widget-id", "Widget ID to show information for");
        marketplaceInfoCommand.AddArgument(infoWidgetIdArgument);
        marketplaceInfoCommand.SetHandler(async (string widgetId) =>
        {
            var exitCode = await Commands.Cli.Marketplace.InfoCommand.ExecuteAsync(widgetId);
            Environment.ExitCode = exitCode;
        }, infoWidgetIdArgument);
        marketplaceCommand.AddCommand(marketplaceInfoCommand);

        // marketplace install
        var marketplaceInstallCommand = new Command("install", "Install widget from marketplace");
        var installWidgetIdArgument = new Argument<string>("widget-id", "Widget ID to install");
        marketplaceInstallCommand.AddArgument(installWidgetIdArgument);
        marketplaceInstallCommand.SetHandler(async (string widgetId) =>
        {
            var exitCode = await Commands.Cli.Marketplace.InstallCommand.ExecuteAsync(widgetId);
            Environment.ExitCode = exitCode;
        }, installWidgetIdArgument);
        marketplaceCommand.AddCommand(marketplaceInstallCommand);

        // marketplace list-installed
        var marketplaceListInstalledCommand = new Command("list-installed", "List installed marketplace widgets");
        marketplaceListInstalledCommand.SetHandler(async () =>
        {
            var exitCode = await Commands.Cli.Marketplace.ListInstalledCommand.ExecuteAsync();
            Environment.ExitCode = exitCode;
        });
        marketplaceCommand.AddCommand(marketplaceListInstalledCommand);

        // marketplace check-updates
        var marketplaceCheckUpdatesCommand = new Command("check-updates", "Check for widget updates");
        var checkUpdatesJsonOption = new Option<bool>("--json", "Output results as JSON");
        marketplaceCheckUpdatesCommand.AddOption(checkUpdatesJsonOption);
        marketplaceCheckUpdatesCommand.SetHandler(async (bool jsonOutput) =>
        {
            var exitCode = await Commands.Cli.Marketplace.CheckUpdatesCommand.ExecuteAsync(jsonOutput);
            Environment.ExitCode = exitCode;
        }, checkUpdatesJsonOption);
        marketplaceCommand.AddCommand(marketplaceCheckUpdatesCommand);

        // marketplace update
        var marketplaceUpdateCommand = new Command("update", "Update widget to latest version");
        var updateWidgetIdArgument = new Argument<string>("widget-id", "Widget ID to update");
        var updateVersionOption = new Option<string?>("--version", "Specific version to install (defaults to latest)");
        var updateYesOption = new Option<bool>("--yes", "Skip confirmation prompts");
        marketplaceUpdateCommand.AddArgument(updateWidgetIdArgument);
        marketplaceUpdateCommand.AddOption(updateVersionOption);
        marketplaceUpdateCommand.AddOption(updateYesOption);
        marketplaceUpdateCommand.SetHandler(async (string widgetId, string? version, bool skipConfirmation) =>
        {
            var exitCode = await Commands.Cli.Marketplace.UpdateCommand.ExecuteAsync(widgetId, version, skipConfirmation);
            Environment.ExitCode = exitCode;
        }, updateWidgetIdArgument, updateVersionOption, updateYesOption);
        marketplaceCommand.AddCommand(marketplaceUpdateCommand);

        // marketplace update-all
        var marketplaceUpdateAllCommand = new Command("update-all", "Update all widgets");
        var updateAllYesOption = new Option<bool>("--yes", "Skip confirmation prompts");
        marketplaceUpdateAllCommand.AddOption(updateAllYesOption);
        marketplaceUpdateAllCommand.SetHandler(async (bool skipConfirmation) =>
        {
            var exitCode = await Commands.Cli.Marketplace.UpdateAllCommand.ExecuteAsync(skipConfirmation);
            Environment.ExitCode = exitCode;
        }, updateAllYesOption);
        marketplaceCommand.AddCommand(marketplaceUpdateAllCommand);

        // Storage commands
        var storageCommand = new Command("storage", "Manage storage and database");
        rootCommand.AddCommand(storageCommand);

        // storage stats
        var storageStatsCommand = new Command("stats", "Show database statistics");
        var statsConfigOption = new Option<string?>("--config", "Path to configuration file");
        storageStatsCommand.AddOption(statsConfigOption);
        storageStatsCommand.SetHandler((string? config) =>
        {
            var exitCode = Commands.Cli.Storage.StorageStatsCommand.Execute(config);
            Environment.ExitCode = exitCode;
        }, statsConfigOption);
        storageCommand.AddCommand(storageStatsCommand);

        // storage cleanup
        var storageCleanupCommand = new Command("cleanup", "Run database cleanup");
        var cleanupConfigOption = new Option<string?>("--config", "Path to configuration file");
        var cleanupForceOption = new Option<bool>("--force", "Skip confirmation prompt");
        storageCleanupCommand.AddOption(cleanupConfigOption);
        storageCleanupCommand.AddOption(cleanupForceOption);
        storageCleanupCommand.SetHandler((string? config, bool force) =>
        {
            var exitCode = Commands.Cli.Storage.StorageCleanupCommand.Execute(config, force);
            Environment.ExitCode = exitCode;
        }, cleanupConfigOption, cleanupForceOption);
        storageCommand.AddCommand(storageCleanupCommand);

        // storage export
        var storageExportCommand = new Command("export", "Export widget data to CSV or JSON");
        var exportWidgetOption = new Option<string?>("--widget", "Widget ID to export data for");
        var exportOutputOption = new Option<string?>("--output", "Output file path");
        var exportFormatOption = new Option<string>("--format", () => "csv", "Output format (csv or json)");
        var exportConfigOption = new Option<string?>("--config", "Path to configuration file");
        storageExportCommand.AddOption(exportWidgetOption);
        storageExportCommand.AddOption(exportOutputOption);
        storageExportCommand.AddOption(exportFormatOption);
        storageExportCommand.AddOption(exportConfigOption);
        storageExportCommand.SetHandler((string? widgetId, string? output, string format, string? config) =>
        {
            var exitCode = Commands.Cli.Storage.StorageExportCommand.Execute(widgetId, output, format, config);
            Environment.ExitCode = exitCode;
        }, exportWidgetOption, exportOutputOption, exportFormatOption, exportConfigOption);
        storageCommand.AddCommand(storageExportCommand);

        // test-widget command
        var testWidgetCommand = new Command("test-widget", "Test and validate widget scripts");
        var testScriptArgument = new Argument<string>("script", "Path to widget script");
        var testExtendedOption = new Option<bool>("--extended", "Show extended output");
        var testUiModeOption = new Option<bool>("--ui", "Show UI preview");
        var testSkipConfirmOption = new Option<bool>("--skip-confirmation", "Skip confirmation prompts");
        testWidgetCommand.AddArgument(testScriptArgument);
        testWidgetCommand.AddOption(testExtendedOption);
        testWidgetCommand.AddOption(testUiModeOption);
        testWidgetCommand.AddOption(testSkipConfirmOption);
        testWidgetCommand.SetHandler(async (string script, bool extended, bool ui, bool skipConfirm) =>
        {
            var exitCode = await Commands.Cli.TestWidgetCommandCli.ExecuteAsync(script, extended, ui, skipConfirm);
            Environment.ExitCode = exitCode;
        }, testScriptArgument, testExtendedOption, testUiModeOption, testSkipConfirmOption);
        rootCommand.AddCommand(testWidgetCommand);

        // new-widget command
        var newWidgetCommand = new Command("new-widget", "Interactive widget creation wizard");
        var newTemplateArgument = new Argument<string?>("template", () => null, "Widget template to use");
        var newNameOption = new Option<string?>("--name", "Widget name");
        var newOutputOption = new Option<string?>("--output", "Output file path");
        var newListOption = new Option<bool>("--list", "List available templates and exit");
        newWidgetCommand.AddArgument(newTemplateArgument);
        newWidgetCommand.AddOption(newNameOption);
        newWidgetCommand.AddOption(newOutputOption);
        newWidgetCommand.AddOption(newListOption);
        newWidgetCommand.SetHandler(async (string? templateName, string? name, string? outputFile, bool listTemplates) =>
        {
            var exitCode = await Commands.Cli.NewWidgetCommandCli.ExecuteAsync(templateName, name, outputFile, listTemplates);
            Environment.ExitCode = exitCode;
        }, newTemplateArgument, newNameOption, newOutputOption, newListOption);
        rootCommand.AddCommand(newWidgetCommand);

        return await rootCommand.InvokeAsync(args);
    }

    /// <summary>
    /// Runs the ServerHub dashboard (called by DefaultCommand)
    /// </summary>
    public static async Task<int> RunDashboardAsync(string[] args, string configPath, bool devMode)
    {
        Storage.StorageService? storageService = null;

        try
        {
            _configPath = configPath;
            _devMode = devMode;

            var configMgr = new ConfigManager();
            _config = configMgr.LoadConfig(configPath);
            _lastConfigLoadTime = File.GetLastWriteTime(configPath);

            // Initialize storage service if enabled
            if (_config.Storage?.Enabled == true)
            {
                try
                {
                    storageService = Storage.StorageService.Initialize(_config.Storage);
                }
                catch (Exception ex)
                {
                    // Log storage initialization error but don't crash the app
                    // Note: Using Console.Error here because Logger isn't initialized yet
                    Console.Error.WriteLine($"Warning: Failed to initialize storage service: {ex.Message}");
                    Console.Error.WriteLine("Dashboard will continue without data persistence.");
                }
            }

            // Initialize services
            var validator = new ScriptValidator(devMode: devMode);
            _executor = new ScriptExecutor(validator);
            _parser = new WidgetProtocolParser();
            _renderer = new WidgetRenderer();
            _layoutEngine = new LayoutEngine();
            _refreshService = new WidgetRefreshService(_executor, _parser, _config, storageService);
            _commandPaletteService = new CommandPaletteService();
            RegisterCommandPaletteCallbacks();

            // Initialize ConsoleEx window system
            _windowSystem = new ConsoleWindowSystem(
                new NetConsoleDriver(RenderMode.Buffer),
                options: new ConsoleWindowSystemOptions(
                    StatusBarOptions: new StatusBarOptions(
                        ShowTaskBar: false,
                        ShowBottomStatus: true
                    )
                ));

            // Initialize logger with SharpConsoleUI's LogService
            // Enable debug logging with: export SHARPCONSOLEUI_DEBUG_LOG=/tmp/serverhub-debug.log
            Utils.Logger.Initialize(_windowSystem.LogService);
            Utils.Logger.Info("ServerHub logger initialized", "Startup");
            Utils.Logger.Debug($"Storage enabled: {_config.Storage?.Enabled}, widgets count: {_config.Widgets.Count}", "Startup");

            // Initialize StatusBarManager
            _statusBarManager = new StatusBarManager(_windowSystem.StatusBarStateService, devMode: _devMode);

            // Setup graceful shutdown
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                _windowSystem?.Shutdown(0);
            };

            // Create main window
            CreateMainWindow();

            // Subscribe to terminal resize events
            _windowSystem.ConsoleDriver.ScreenResized += OnTerminalResized;

            // Check for unconfigured scripts and show warning
            CheckForUnconfiguredWidgets();

            // Dev mode: show warning dialog that MUST be acknowledged before scripts run
            if (_devMode)
            {
                ShowDevModeWarningDialog(); // Will call StartWidgetRefreshTimers() when acknowledged
            }
            else
            {
                // Normal mode: start scripts immediately
                StartWidgetRefreshTimers();
            }

            // Run the application
            await Task.Run(() => _windowSystem.Run());

            // Cleanup
            StopWidgetRefreshTimers();

            return 0;
        }
        catch (ConfigurationException configEx)
        {
            DisplayConfigurationError(configEx);
            return 1;
        }
        catch (Exception ex)
        {
            DisplayGenericError(ex);
            return 1;
        }
        finally
        {
            // Ensure storage service is properly disposed
            if (storageService != null)
            {
                try
                {
                    Storage.StorageService.Shutdown();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Error shutting down storage service: {ex.Message}");
                }
            }
        }
    }

    private static void CreateMainWindow()
    {
        if (_windowSystem == null || _config == null)
            return;

        var windowBuilder = new WindowBuilder(_windowSystem)
            .WithTitle("ServerHub")
            .WithName("MainWindow")
            .WithColors(Spectre.Console.Color.Grey11, Spectre.Console.Color.Grey93)
            .Borderless()
            .Maximized()
            .WithAsyncWindowThread(UpdateDashboardAsync)
            .OnKeyPressed(HandleKeyPress);

        // Dev mode: orange border color (not foreground!) as visual indicator
        if (_devMode)
        {
            windowBuilder.WithActiveBorderColor(Spectre.Console.Color.Orange1);
        }

        _mainWindow = windowBuilder.Build();

        // Get terminal dimensions
        var terminalWidth = Console.WindowWidth;
        var terminalHeight = Console.WindowHeight;

        // Store initial terminal width
        _lastTerminalWidth = terminalWidth;

        // Calculate layout
        var placements = _layoutEngine!.CalculateLayout(_config, terminalWidth, terminalHeight);

        // Initialize FocusManager
        _focusManager = new FocusManager();
        _focusManager.Initialize(_mainWindow, placements);
        // No widget focused on startup - user must click or tab to focus

        // Build widget layout
        BuildWidgetLayout(placements, terminalWidth, useCache: false);

        _windowSystem.AddWindow(_mainWindow);
    }

    private static void OnTerminalResized(object? sender, SharpConsoleUI.Helpers.Size size)
    {
        var newWidth = size.Width;

        // Only rebuild if width changed (column count might change)
        if (newWidth != _lastTerminalWidth)
        {
            _lastTerminalWidth = newWidth;
            RebuildLayout();
        }
    }

    private static void RebuildLayout()
    {
        if (
            _mainWindow == null
            || _windowSystem == null
            || _config == null
            || _layoutEngine == null
        )
            return;

        try
        {
            // SAVE current focus BEFORE clearing controls
            var currentFocusedId = _focusManager?.GetFocusedWidgetId();

            // Clear all controls from the window
            _mainWindow.ClearControls();

            // Get terminal dimensions
            var terminalWidth = Console.WindowWidth;
            var terminalHeight = Console.WindowHeight;

            // Recalculate layout
            var placements = _layoutEngine.CalculateLayout(_config, terminalWidth, terminalHeight);

            // Check if dashboard is empty
            if (placements.Count == 0)
            {
                ShowEmptyDashboardMessage();
                UpdateStatusBar();
                return;
            }

            // RE-INITIALIZE FocusManager with new placements
            _focusManager?.Initialize(_mainWindow, placements);

            // Build widget layout using cache
            BuildWidgetLayout(placements, terminalWidth, useCache: true);

            // RESTORE focus to same widget (by ID) after rebuild completes
            if (currentFocusedId != null)
            {
                _focusManager?.FocusWidget(currentFocusedId);
            }
            // If no widget was focused before rebuild, keep it that way

            // Update status
            _statusBarManager?.ShowInfo($"Layout rebuilt for {terminalWidth} cols", 2000);
        }
        catch (Exception ex)
        {
            // If rebuild fails, just log it - don't crash the app
            _statusBarManager?.ShowError($"Layout rebuild error: {ex.Message}", 5000);
        }
    }

    private static void BuildWidgetLayout(
        List<LayoutEngine.WidgetPlacement> placements,
        int terminalWidth,
        bool useCache
    )
    {
        if (_mainWindow == null || _renderer == null)
            return;

        // Determine column count for this terminal width
        var columnCount = GetColumnCountFromWidth(terminalWidth);

        // Calculate base column width (no spacing between widgets)
        const int spacingBetweenWidgets = 0;
        int totalSpacing = Math.Max(0, (columnCount - 1) * spacingBetweenWidgets);
        int availableWidth = terminalWidth - totalSpacing;
        int baseColumnWidth = availableWidth / columnCount;

        // Group placements by row
        var rowGroups = placements.GroupBy(p => p.Row).OrderBy(g => g.Key);

        // Consistent styling for all widgets (btop-style with rounded borders)
        var widgetBackgroundColor = Spectre.Console.Color.Grey11;  // Minimal background, same as window

        // Build rows (no vertical spacing between rows)
        foreach (var rowGroup in rowGroups)
        {

            var rowPlacements = rowGroup.OrderBy(p => p.Column).ToList();

            // Create horizontal grid for this row
            var rowGrid = Controls
                .HorizontalGrid()
                .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
                .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Top);

            bool firstWidget = true;
            var widgetBgColors = new List<Spectre.Console.Color>();
            var widgetIdByColumnIndex = new List<string?>(); // Track which widget owns each column

            foreach (var placement in rowPlacements)
            {
                // Add spacing between widgets (not before first widget)
                if (!firstWidget)
                {
                    rowGrid.Column(colBuilder => colBuilder.Width(spacingBetweenWidgets));
                    widgetBgColors.Add(Spectre.Console.Color.Grey11); // Spacer color
                    widgetIdByColumnIndex.Add(null); // Spacer column has no widget
                }
                firstWidget = false;

                // Calculate widget width based on column span
                // width = (baseColumnWidth * spanCount) + (spacing * (spanCount - 1))
                int widgetWidth =
                    (baseColumnWidth * placement.ColumnSpan)
                    + (spacingBetweenWidgets * Math.Max(0, placement.ColumnSpan - 1));

                // Get widget data: use cache if rebuilding, otherwise show loading spinner
                var widgetData =
                    useCache
                    && _widgetDataCache.TryGetValue(placement.WidgetId, out var cachedData)
                        ? cachedData
                        : CreateLoadingWidget(placement.WidgetId);

                // Determine border color: highlight pinned widgets with cyan border
                var borderColor = placement.IsPinned
                    ? Spectre.Console.Color.Cyan1
                    : Spectre.Console.Color.Grey35;

                widgetBgColors.Add(widgetBackgroundColor);
                widgetIdByColumnIndex.Add(placement.WidgetId); // Track widget for this column

                // Get max lines: widget-specific > global > default 20
                var widgetConfig = _config?.Widgets.GetValueOrDefault(placement.WidgetId);
                int maxLines = widgetConfig?.MaxLines ?? _config?.MaxLinesPerWidget ?? 20;
                bool showIndicator = _config?.ShowTruncationIndicator ?? true;

                var widgetPanel = _renderer.CreateWidgetPanel(
                    placement.WidgetId,
                    widgetData,
                    placement.IsPinned,
                    widgetBackgroundColor,
                    borderColor,
                    widgetId =>
                    {
                        // Toggle focus: click again to deselect
                        if (_focusManager?.GetFocusedWidgetId() == widgetId)
                        {
                            _focusManager?.ClearFocus();
                        }
                        else
                        {
                            _focusManager?.FocusWidget(widgetId);
                        }
                    },
                    widgetId => ShowWidgetDialog(widgetId),  // Double-click opens dialog
                    maxLines,
                    showIndicator
                );

                // Store full widget data for expansion dialog
                _fullWidgetData[placement.WidgetId] = widgetData;

                // Add widget column with explicit width
                rowGrid.Column(colBuilder =>
                {
                    colBuilder.Width(widgetWidth);
                    colBuilder.Add(widgetPanel);
                });
            }

            // Build and add row grid
            var builtRowGrid = rowGrid.Build();

            // Set background colors for columns and wire up click events
            for (int i = 0; i < builtRowGrid.Columns.Count && i < widgetBgColors.Count; i++)
            {
                builtRowGrid.Columns[i].BackgroundColor = widgetBgColors[i];

                // Wire up click events for widget columns (skip spacer columns)
                // Note: Double-click handler is already registered on PanelControl in CreateWidgetPanel
                if (i < widgetIdByColumnIndex.Count && widgetIdByColumnIndex[i] != null)
                {
                    var widgetId = widgetIdByColumnIndex[i]!;

                    // Single click: toggle focus (click again to deselect)
                    builtRowGrid.Columns[i].MouseClick += (sender, e) =>
                    {
                        if (_focusManager?.GetFocusedWidgetId() == widgetId)
                        {
                            _focusManager?.ClearFocus();
                        }
                        else
                        {
                            _focusManager?.FocusWidget(widgetId);
                        }
                    };

                    // REMOVED: Duplicate MouseDoubleClick handler that was causing two modals to open
                    // The handler is already registered on the PanelControl via CreateWidgetPanel (line 438)
                }
            }

            _mainWindow.AddControl(builtRowGrid);
        }
    }

    private static async Task UpdateDashboardAsync(Window window, CancellationToken ct)
    {
        // This async thread updates the status bar with current time and system stats
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Rotate spinner frame
                _spinnerFrame++;

                // Update widgets that are currently refreshing to animate spinner
                foreach (
                    var widgetId in _isRefreshing
                        .Where(kvp => kvp.Value)
                        .Select(kvp => kvp.Key)
                        .ToList()
                )
                {
                    if (_widgetDataCache.TryGetValue(widgetId, out var widgetData))
                    {
                        UpdateWidgetUI(widgetId, widgetData);
                    }
                }

                // Update status bar with widget counts and system stats
                UpdateStatusBar();

                await Task.Delay(250, ct);  // Faster spinner animation
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static WidgetData CreateLoadingWidget(string widgetId)
    {
        return new WidgetData
        {
            Title = widgetId,
            Timestamp = DateTime.Now,
            Rows = new List<WidgetRow>
            {
                new() { Content = "" },
                new()
                {
                    Content = $"          [cyan1]Loading... {_spinnerFrames[_spinnerFrame % 4]}[/]",
                },
                new() { Content = "" },
            },
        };
    }

    private static void TogglePause()
    {
        _isPaused = !_isPaused;

        if (_windowSystem != null)
        {
            // Bottom status doesn't need updating - it's static keyboard shortcuts
            // Just force a status bar update to show pause state in top status
            UpdateStatusBar();
        }
    }

    private static void ShowHelpOverlay()
    {
        if (_windowSystem == null)
            return;

        // Calculate centered position and size based on terminal dimensions
        var terminalWidth = Console.WindowWidth;
        var terminalHeight = Console.WindowHeight;

        // Dialog size: larger for better readability
        var dialogWidth = Math.Min(78, terminalWidth - 10);
        var dialogHeight = Math.Min(24, terminalHeight - 6);

        // Center the dialog
        var dialogX = (terminalWidth - dialogWidth) / 2;
        var dialogY = (terminalHeight - dialogHeight) / 2;

        // Create help window with rounded borders
        var helpWindow = new WindowBuilder(_windowSystem)
            .WithName("HelpOverlay")
            .WithBounds(dialogX, dialogY, dialogWidth, dialogHeight)
            .WithBorderStyle(BorderStyle.Rounded)
            .WithBorderColor(Color.Grey35)
            .WithColors(Color.Grey15, Color.Grey93)
            .HideTitle()
            .AsModal()
            .Minimizable(false)
            .Maximizable(false)
            .Resizable(false)
            .Movable(false)
            .OnKeyPressed(
                (sender, e) =>
                {
                    // Close on ESC, Enter, or ? key
                    if (
                        e.KeyInfo.Key == ConsoleKey.Escape
                        || e.KeyInfo.Key == ConsoleKey.Enter
                        || e.KeyInfo.KeyChar == '?'
                        || e.KeyInfo.Key == ConsoleKey.F1
                    )
                    {
                        _windowSystem.CloseWindow((Window)sender!);
                        e.Handled = true;
                    }
                }
            )
            .Build();

        var helpContent =
            @"
  [bold cyan1]ServerHub[/] [grey70]v0.1.0[/] [grey50]•[/] [grey70]Server Monitoring Dashboard[/]

  [bold cyan1]▸ Navigation[/]
    [cyan1]Tab[/] [grey50]/[/] [cyan1]Shift+Tab[/]       Move between widgets
    [cyan1]Arrow keys[/]            Scroll within widget
    [cyan1]Ctrl[/] [grey50]+[/] [cyan1]←/→[/]             Swap widget left/right in row
    [cyan1]Ctrl[/] [grey50]+[/] [cyan1]↑/↓[/]             Swap widget with row above/below
    [cyan1]Ctrl+Shift[/] [grey50]+[/] [cyan1]←/→[/]       Decrease/increase widget width
    [cyan1]Ctrl+Shift[/] [grey50]+[/] [cyan1]↑/↓[/]       Decrease/increase widget height

  [bold cyan1]▸ Widget Actions[/]
    [cyan1]Click[/]                   Focus widget
    [cyan1]Double-click[/] [grey50]or[/] [cyan1]Enter[/]   Open expanded view

  [bold cyan1]▸ Application[/]
    [cyan1]F2[/]                      Configure widgets [grey50](add/remove/edit)[/]
    [cyan1]F3[/]                      Browse marketplace [grey50](install widgets)[/]
    [cyan1]Ctrl+P[/]                  Command palette [grey50](quick actions)[/]
    [cyan1]F5[/]                      Refresh all widgets
    [cyan1]Space[/]                   Pause / resume auto-refresh
    [cyan1]? [/][grey50]or[/] [cyan1]F1[/]                Show this help
    [cyan1]Ctrl+Q[/]                  Exit ServerHub

  [grey50]───────────────────────────────────────────────────────────────────────[/]
  [grey70]Press [cyan1]ESC[/], [cyan1]Enter[/], [cyan1]?[/], or [cyan1]F1[/] to close this help[/]
";

        var helpBuilder = Controls
            .Markup()
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(2, 1, 2, 1);

        // Split content into lines and add them
        var lines = helpContent.Split('\n');
        foreach (var line in lines)
        {
            helpBuilder.AddLine(line);
        }

        var helpControl = helpBuilder.Build();

        helpWindow.AddControl(helpControl);
        _windowSystem.AddWindow(helpWindow);
    }

    /// <summary>
    /// Shows a warning dialog when running in dev mode (Layer 3 of dev mode warnings)
    /// Scripts will not start until user acknowledges by clicking the button
    /// </summary>
    private static void ShowDevModeWarningDialog()
    {
        if (_windowSystem == null)
            return;

        Window? warningWindow = null;
        warningWindow = new WindowBuilder(_windowSystem)
            .WithName("DevModeWarning")
            .HideTitle()
            .WithSize(60, 16)
            .Centered()
            .WithBackgroundColor(Color.Grey11)
            .WithBorderStyle(BorderStyle.Single)
            .AsModal()
            .Minimizable(false)
            .Maximizable(false)
            .Resizable(false)
            .Movable(false)
            .HideCloseButton()
            .Build();

        var contentBuilder = Controls
            .Markup()
            .WithBackgroundColor(Color.Grey11)
            .WithMargin(2, 1, 2, 1);

        contentBuilder.AddLine("");
        contentBuilder.AddLine("[bold yellow]  Development Mode Active[/]");
        contentBuilder.AddLine("");
        contentBuilder.AddLine("[white]Custom widget checksum validation is DISABLED.[/]");
        contentBuilder.AddLine("");
        contentBuilder.AddLine("[grey70]• Custom widgets will run without integrity checks[/]");
        contentBuilder.AddLine("[grey70]• Bundled widgets are still validated[/]");
        contentBuilder.AddLine("[grey70]• Use for development and testing only[/]");
        contentBuilder.AddLine("");
        contentBuilder.AddLine("[red]Do NOT use --dev-mode in production![/]");

        var contentControl = contentBuilder.Build();
        warningWindow.AddControl(contentControl);

        // Buttons: I Understand and Quit
        var understandButton = Controls
            .Button(" I Understand ")
            .OnClick((sender, e) =>
            {
                _windowSystem?.CloseWindow(warningWindow!);
                StartWidgetRefreshTimers(); // Start scripts only after acknowledgment
            })
            .Build();

        var quitButton = Controls
            .Button("    Quit    ")
            .WithMargin(2, 0, 0, 0)
            .OnClick((sender, e) =>
            {
                _windowSystem?.Shutdown(0);
            })
            .Build();

        var buttonGrid = HorizontalGridControl.ButtonRow(understandButton, quitButton);
        warningWindow.AddControl(buttonGrid);

        _windowSystem.AddWindow(warningWindow);
    }

    /// <summary>
    /// Checks for unconfigured executable scripts in widget directories and shows warning
    /// </summary>
    private static void CheckForUnconfiguredWidgets()
    {
        if (_windowSystem == null || _config == null)
            return;

        var unconfigured = new List<string>();

        // Get all configured paths
        var configuredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var widget in _config.Widgets.Values)
        {
            var resolved = WidgetPaths.ResolveWidgetPath(widget.Path, widget.Location);
            if (resolved != null)
                configuredPaths.Add(Path.GetFullPath(resolved));
        }

        // Check each search path for unconfigured executables
        foreach (var searchPath in WidgetPaths.GetSearchPaths())
        {
            if (!Directory.Exists(searchPath)) continue;

            // Skip bundled widgets directory (those are trusted)
            if (searchPath == WidgetPaths.GetBundledWidgetsDirectory()) continue;

            foreach (var file in Directory.GetFiles(searchPath))
            {
                if (WidgetConfigurationHelper.IsExecutable(file) && !configuredPaths.Contains(Path.GetFullPath(file)))
                {
                    unconfigured.Add(Path.GetFileName(file));
                }
            }
        }

        if (unconfigured.Count > 0)
        {
            // Show persistent warning in bottom status bar
            _statusBarManager?.ShowWarning($"{unconfigured.Count} unconfigured script(s) found. Run --discover to review.", 10000);
        }
    }

    private static void HandleKeyPress(object? sender, KeyPressedEventArgs e)
    {
        // ===== PRIORITY 0: Widget Expansion & Focus Clearing =====
        // Handle Enter key on focused widget to show expansion dialog
        if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            string? focusedWidgetId = _focusManager?.GetFocusedWidgetId();
            if (focusedWidgetId != null)
            {
                ShowWidgetDialog(focusedWidgetId);
                e.Handled = true;
                return;
            }
        }

        // Handle Esc key to clear widget focus
        if (e.KeyInfo.Key == ConsoleKey.Escape)
        {
            if (_focusManager?.GetFocusedWidgetId() != null)
            {
                _focusManager?.ClearFocus();
                e.Handled = true;
                return;
            }
        }

        // ===== PRIORITY 1: Widget Focus Navigation =====
        // CRITICAL: Handle Tab BEFORE Window.HandleKeyPress() processes it
        if (e.KeyInfo.Key == ConsoleKey.Tab)
        {
            if (e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift))
            {
                _focusManager?.FocusPrevious();
            }
            else
            {
                _focusManager?.FocusNext();
            }
            e.Handled = true; // Prevent Window.SwitchFocus() from processing
            return;
        }

        // ===== PRIORITY 2: Widget Reordering =====
        // Ctrl+Arrow keys for visual widget reordering
        if (e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control) && !e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            FocusManager.VisualReorderDirection? direction = null;

            switch (e.KeyInfo.Key)
            {
                case ConsoleKey.LeftArrow:
                    direction = FocusManager.VisualReorderDirection.VisualLeft;
                    break;

                case ConsoleKey.RightArrow:
                    direction = FocusManager.VisualReorderDirection.VisualRight;
                    break;

                case ConsoleKey.UpArrow:
                    direction = FocusManager.VisualReorderDirection.VisualUp;
                    break;

                case ConsoleKey.DownArrow:
                    direction = FocusManager.VisualReorderDirection.VisualDown;
                    break;
            }

            if (direction.HasValue && _focusManager != null && _config != null && _configPath != null)
            {
                HandleWidgetReorderVisual(direction.Value);
                e.Handled = true;
                return;
            }
            // If not an arrow key, fall through to other handlers
        }

        // ===== PRIORITY 2.5: Widget Resizing =====
        // Ctrl+Shift+Arrow keys for widget resizing
        if (e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control) && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            FocusManager.ResizeDirection? direction = null;

            switch (e.KeyInfo.Key)
            {
                case ConsoleKey.LeftArrow:
                    direction = FocusManager.ResizeDirection.WidthDecrease;
                    break;

                case ConsoleKey.RightArrow:
                    direction = FocusManager.ResizeDirection.WidthIncrease;
                    break;

                case ConsoleKey.UpArrow:
                    direction = FocusManager.ResizeDirection.HeightDecrease;
                    break;

                case ConsoleKey.DownArrow:
                    direction = FocusManager.ResizeDirection.HeightIncrease;
                    break;
            }

            if (direction.HasValue && _focusManager != null && _config != null && _configPath != null)
            {
                HandleWidgetResize(direction.Value);
                e.Handled = true;
                return;
            }
            // If not an arrow key, fall through to other handlers
        }

        // ===== PRIORITY 3: Existing Application Shortcuts =====
        if (e.KeyInfo.Key == ConsoleKey.Q && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            _windowSystem?.Shutdown(0);
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F5)
        {
            // Check if config file changed on disk
            if (_configPath != null && File.Exists(_configPath))
            {
                var currentModTime = File.GetLastWriteTime(_configPath);
                if (currentModTime > _lastConfigLoadTime)
                {
                    // Config changed - full reload
                    ReloadConfigAndRebuildDashboard();
                }
                else
                {
                    // Config unchanged - just refresh widget data
                    RefreshAllWidgets();
                }
            }
            else
            {
                // No config path - just refresh
                RefreshAllWidgets();
            }
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.Spacebar)
        {
            // Toggle pause mode
            TogglePause();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F1 || e.KeyInfo.KeyChar == '?')
        {
            // Show help overlay
            ShowHelpOverlay();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F2)
        {
            // Show widget configuration dialog
            ShowConfigDialog();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F3)
        {
            // Show marketplace browser
            ShowMarketplaceBrowser();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.P && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            // Show command palette
            ShowCommandPalette();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Shows the widget configuration dialog for adding, removing, editing, and reordering widgets.
    /// </summary>
    private static void ShowConfigDialog()
    {
        if (_windowSystem == null || _config == null || _configPath == null)
            return;

        WidgetConfigDialog.Show(
            _windowSystem,
            _configPath,
            _config,
            onConfigChanged: () =>
            {
                ReloadConfigAndRebuildDashboard();
            }
        );
    }

    /// <summary>
    /// Shows the marketplace browser dialog for browsing and installing marketplace widgets.
    /// </summary>
    private static void ShowMarketplaceBrowser()
    {
        if (_windowSystem == null || _configPath == null)
            return;

        // Determine marketplace install path (respects --widgets-path)
        var installPath = WidgetPaths.GetMarketplaceInstallPath();

        MarketplaceBrowserDialog.Show(
            _windowSystem,
            installPath,
            _configPath,
            onWidgetInstalled: () =>
            {
                // Reload configuration after widget installation
                if (_config != null && _configPath != null)
                {
                    var configMgr = new ConfigManager();
                    _config = configMgr.LoadConfig(_configPath);
                    _lastConfigLoadTime = File.GetLastWriteTime(_configPath);

                    // Get storage service instance if available
                    Storage.StorageService? storageService = null;
                    try
                    {
                        storageService = Storage.StorageService.Instance;
                    }
                    catch (InvalidOperationException)
                    {
                        // Storage service not initialized - that's OK
                    }

                    // Reinitialize refresh service with new config
                    if (_executor != null && _parser != null)
                    {
                        _refreshService = new WidgetRefreshService(_executor, _parser, _config, storageService);
                    }

                    // Restart widget timers with new configuration
                    StopWidgetRefreshTimers();
                    StartWidgetRefreshTimers();

                    // Rebuild the layout
                    RebuildLayout();

                    // Update status
                    _statusBarManager?.ShowSuccess("Widget installed and configuration reloaded");
                }
            }
        );
    }

    /// <summary>
    /// Reloads configuration from disk and rebuilds dashboard
    /// </summary>
    private static void ReloadConfigAndRebuildDashboard()
    {
        if (_config == null || _configPath == null)
            return;

        try
        {
            // Reload configuration from disk
            var configMgr = new ConfigManager();
            _config = configMgr.LoadConfig(_configPath);
            _lastConfigLoadTime = File.GetLastWriteTime(_configPath);

            // Get storage service instance if available
            Storage.StorageService? storageService = null;
            try
            {
                storageService = Storage.StorageService.Instance;
            }
            catch (InvalidOperationException)
            {
                // Storage service not initialized - that's OK
            }

            // Reinitialize refresh service with new config
            if (_executor != null && _parser != null)
            {
                _refreshService = new WidgetRefreshService(_executor, _parser, _config, storageService);
            }

            // Restart widget timers with new configuration
            StopWidgetRefreshTimers();
            StartWidgetRefreshTimers();

            // Rebuild the layout
            RebuildLayout();

            // Update status
            _statusBarManager?.ShowSuccess("Configuration reloaded from disk");
        }
        catch (Exception ex)
        {
            if (_windowSystem != null)
            {
                _windowSystem.NotificationStateService.ShowNotification(
                    "Config Reload Failed",
                    ex.Message,
                    NotificationSeverity.Danger,
                    timeout: 5000
                );
            }
        }
    }

    /// <summary>
    /// Handles visual widget reordering and persistence
    /// </summary>
    private static void HandleWidgetReorderVisual(FocusManager.VisualReorderDirection direction)
    {
        if (_focusManager == null || _config == null || _configPath == null)
            return;

        // Get current focused widget before reorder
        var focusedWidgetId = _focusManager.GetFocusedWidgetId();
        if (focusedWidgetId == null)
            return;

        // Attempt reorder
        bool moved = _focusManager.ReorderWidgetVisual(
            direction,
            _config,
            onReorder: (widgetId, dir) =>
            {
                // Save config immediately
                var configMgr = new ConfigManager();
                try
                {
                    configMgr.SaveConfig(_config!, _configPath!);

                    // Update status bar with feedback
                    var directionText = dir switch
                    {
                        FocusManager.VisualReorderDirection.VisualLeft => "left",
                        FocusManager.VisualReorderDirection.VisualRight => "right",
                        FocusManager.VisualReorderDirection.VisualUp => "up",
                        FocusManager.VisualReorderDirection.VisualDown => "down",
                        _ => "unknown"
                    };

                    _statusBarManager?.ShowInfo($"Swapped '{widgetId}' {directionText}");
                }
                catch (Exception ex)
                {
                    _statusBarManager?.ShowError($"Failed to save config: {ex.Message}", 5000);
                }
            }
        );

        if (!moved)
        {
            // At boundary or other issue - provide feedback
            var message = direction switch
            {
                FocusManager.VisualReorderDirection.VisualLeft => "No widget to the left",
                FocusManager.VisualReorderDirection.VisualRight => "No widget to the right",
                FocusManager.VisualReorderDirection.VisualUp => "No widget above",
                FocusManager.VisualReorderDirection.VisualDown => "No widget below",
                _ => "Cannot reorder"
            };

            _statusBarManager?.ShowWarning(message, 2000);
            return;
        }

        // Rebuild layout to reflect new order
        RebuildLayout();
    }

    /// <summary>
    /// Handles widget resizing and persistence
    /// </summary>
    private static void HandleWidgetResize(FocusManager.ResizeDirection direction)
    {
        if (_focusManager == null || _config == null || _configPath == null)
            return;

        var focusedWidgetId = _focusManager.GetFocusedWidgetId();
        if (focusedWidgetId == null)
            return;

        bool resized = _focusManager.ResizeWidget(
            direction,
            _config,
            onResize: (widgetId, dir, newValue) =>
            {
                var configMgr = new ConfigManager();
                try
                {
                    configMgr.SaveConfig(_config!, _configPath!);

                    var propertyName = (dir == FocusManager.ResizeDirection.WidthDecrease ||
                                        dir == FocusManager.ResizeDirection.WidthIncrease)
                        ? "width"
                        : "height";

                    _statusBarManager?.ShowInfo($"Resized '{widgetId}' {propertyName} to {newValue}");
                }
                catch (Exception ex)
                {
                    _statusBarManager?.ShowError($"Failed to save config: {ex.Message}", 5000);
                }
            }
        );

        if (!resized)
        {
            var message = direction switch
            {
                FocusManager.ResizeDirection.WidthDecrease => "Minimum width reached",
                FocusManager.ResizeDirection.WidthIncrease => "Maximum width reached",
                FocusManager.ResizeDirection.HeightDecrease => "Minimum height reached",
                FocusManager.ResizeDirection.HeightIncrease => "Maximum height reached",
                _ => "Cannot resize"
            };

            _statusBarManager?.ShowWarning(message, 2000);
            return;
        }

        RebuildLayout();
    }

    private static void StartWidgetRefreshTimers()
    {
        if (_config == null)
            return;

        // Count enabled widgets for initial load tracking
        var enabledWidgets = _config.Widgets.Where(w => w.Value.Enabled).ToList();
        lock (_initialLoadLock)
        {
            _initialLoadPendingCount = enabledWidgets.Count;
        }

        foreach (var (widgetId, widgetConfig) in _config.Widgets)
        {
            // Skip disabled widgets - no timers, no initial fetch
            if (!widgetConfig.Enabled)
                continue;

            // Initial fetch (marked as initial load)
            _ = RefreshWidgetAsync(widgetId, widgetConfig, force: false, isInitialLoad: true);

            // Create PeriodicTimer for this widget
            var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(widgetConfig.Refresh));
            _widgetTimers[widgetId] = periodicTimer;

            // Start background task with PeriodicTimer
            var widgetTask = Task.Run(async () =>
            {
                try
                {
                    var token = _cancellationTokenSource.Token;
                    while (await periodicTimer.WaitForNextTickAsync(token))
                    {
                        try
                        {
                            await RefreshWidgetAsync(widgetId, widgetConfig);
                        }
                        catch (Exception ex)
                        {
                            Utils.Logger.Error($"Widget refresh error: {widgetId} - {ex.Message}", ex, "WidgetRefresh");
                            // Continue loop despite error - widget will retry on next tick
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Utils.Logger.Debug($"Widget refresh cancelled: {widgetId}", "WidgetRefresh");
                }
                finally
                {
                    periodicTimer.Dispose();
                }
            }, _cancellationTokenSource.Token);

            _widgetTasks[widgetId] = widgetTask;
        }
    }

    private static void StopWidgetRefreshTimers()
    {
        // Cancel all widget refresh tasks
        _cancellationTokenSource.Cancel();

        // Wait for tasks to complete (with timeout)
        var tasksArray = _widgetTasks.Values.ToArray();
        if (tasksArray.Length > 0)
        {
            try
            {
                // Use synchronous wait since this is called from non-async context
                Task.WaitAll(tasksArray, TimeSpan.FromSeconds(5));
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
            {
                // Expected when cancelling - all tasks were cancelled cleanly
                Utils.Logger.Debug("All widget tasks cancelled successfully", "Shutdown");
            }
            catch (Exception ex)
            {
                Utils.Logger.Warning($"Widget tasks did not complete cleanly: {ex.Message}", "Shutdown");
            }
        }

        // Clear dictionaries
        _widgetTasks.Clear();
        _widgetTimers.Clear();

        // Dispose and recreate cancellation token source for next use (e.g., config reload)
        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
    }

    private static void RefreshAllWidgets()
    {
        if (_config == null)
            return;

        foreach (var (widgetId, widgetConfig) in _config.Widgets)
        {
            // Skip disabled widgets
            if (!widgetConfig.Enabled)
                continue;

            _ = RefreshWidgetAsync(widgetId, widgetConfig);
        }
    }

    private static async Task RefreshWidgetAsync(string widgetId, WidgetConfig widgetConfig, bool force = false, bool isInitialLoad = false)
    {
        Utils.Logger.Debug($"RefreshWidgetAsync called for widget: {widgetId}", "WidgetExecution");
        try
        {
            if (_executor == null || _parser == null || _mainWindow == null)
            {
                Utils.Logger.Warning($"RefreshWidgetAsync: Missing dependencies for {widgetId}", "WidgetExecution");
                return;
            }

            // Skip if widget no longer exists in current configuration (race condition during reload)
            if (_config?.Widgets.ContainsKey(widgetId) == false)
                return;

            // Skip refresh when paused (unless forced)
            if (!force && _isPaused)
                return;

            // Skip all widget refreshes when sudo password dialog is open
            // This prevents ANSI mouse sequence leaks during password entry
            if (ServerHub.UI.SudoPasswordDialog.IsOpen)
                return;

            // Skip if modal is open for this widget - modal handles its own refresh
            // UNLESS this is a forced refresh (e.g., after action completion)
            if (!force && _openModalWidgetId == widgetId)
                return;

            // Mark as refreshing (check again before accessing dictionary)
            if (_config?.Widgets.ContainsKey(widgetId) == false)
                return;
            _isRefreshing[widgetId] = true;

            try
            {
                // Resolve widget path
                var scriptPath = WidgetPaths.ResolveWidgetPath(widgetConfig.Path, widgetConfig.Location);
                if (scriptPath == null)
                {
                    _consecutiveErrors.TryGetValue(widgetId, out var errorCount);
                    _consecutiveErrors[widgetId] = errorCount + 1;

                    var lastUpdate = _lastSuccessfulUpdate.TryGetValue(widgetId, out var lastTime)
                        ? $"Last update: {FormatRelativeTime(lastTime)}"
                        : "Never updated successfully";

                    var errorData = new WidgetData
                    {
                        Title = widgetId,
                        Error = $"Widget script not found: {widgetConfig.Path}",
                        Timestamp = DateTime.Now,
                        Rows = new List<WidgetRow>
                        {
                            new()
                            {
                                Content = $"[red]Error:[/] Widget script not found",
                                Status = new() { State = StatusState.Error },
                            },
                            new() { Content = $"[grey70]Path: {widgetConfig.Path}[/]" },
                            new() { Content = "" },
                            new() { Content = $"[grey70]{lastUpdate}[/]" },
                            new() { Content = $"[grey70]Next retry: {widgetConfig.Refresh}s[/]" },
                            new() { Content = $"[grey70]Consecutive errors: {errorCount + 1}[/]" },
                        },
                    };

                    _widgetDataCache[widgetId] = errorData;

                    // Clear refreshing state BEFORE updating UI
                    _isRefreshing[widgetId] = false;

                    UpdateWidgetUI(widgetId, errorData);
                    return;
                }

                // Execute script using WidgetRefreshService (handles storage persistence)
                WidgetData widgetData;
                if (_refreshService != null)
                {
                    widgetData = await _refreshService.RefreshAsync(widgetId);

                    if (!widgetData.HasError)
                    {
                        // Track successful update
                        _lastSuccessfulUpdate[widgetId] = DateTime.Now;
                        _consecutiveErrors[widgetId] = 0;
                    }
                    else
                    {
                        // Track error and enhance error display
                        _consecutiveErrors.TryGetValue(widgetId, out var errorCount);
                        _consecutiveErrors[widgetId] = errorCount + 1;

                        var lastUpdate = _lastSuccessfulUpdate.TryGetValue(widgetId, out var lastTime)
                            ? $"Last update: {FormatRelativeTime(lastTime)}"
                            : "Never updated successfully";

                        // Enhance error widget with tracking information
                        widgetData.Rows.Add(new WidgetRow { Content = "" });
                        widgetData.Rows.Add(new WidgetRow { Content = $"[grey70]{lastUpdate}[/]" });
                        widgetData.Rows.Add(new WidgetRow { Content = $"[grey70]Next retry: {widgetConfig.Refresh}s[/]" });
                        widgetData.Rows.Add(new WidgetRow { Content = $"[grey70]Consecutive errors: {errorCount + 1}[/]" });
                    }
                }
                else
                {
                    // Fallback if WidgetRefreshService is not available
                    widgetData = new WidgetData
                    {
                        Title = widgetId,
                        Error = "Widget refresh service not available",
                        Timestamp = DateTime.Now,
                    };
                }

                _widgetDataCache[widgetId] = widgetData;

                // Clear refreshing state BEFORE updating UI
                _isRefreshing[widgetId] = false;

                UpdateWidgetUI(widgetId, widgetData);
            }
            catch (Exception ex)
            {
                // Skip error handling if widget no longer exists (race condition during reload)
                if (_config?.Widgets.ContainsKey(widgetId) == false)
                {
                    // Clear refreshing state if it exists and exit silently
                    if (_isRefreshing.ContainsKey(widgetId))
                        _isRefreshing[widgetId] = false;
                    return;
                }

                // Track exception error
                _consecutiveErrors.TryGetValue(widgetId, out var errorCount);
                _consecutiveErrors[widgetId] = errorCount + 1;

                var lastUpdate = _lastSuccessfulUpdate.TryGetValue(widgetId, out var lastTime)
                    ? $"Last update: {FormatRelativeTime(lastTime)}"
                    : "Never updated successfully";

                var errorData = new WidgetData
                {
                    Title = widgetId,
                    Error = $"Exception: {ex.Message}",
                    Timestamp = DateTime.Now,
                    Rows = new List<WidgetRow>
                    {
                        new()
                        {
                            Content = $"[red]Exception[/]",
                            Status = new() { State = StatusState.Error },
                        },
                        new() { Content = "" },
                        new() { Content = $"[grey70]{ex.Message}[/]" },
                        new() { Content = "" },
                        new() { Content = $"[grey70]{lastUpdate}[/]" },
                        new() { Content = $"[grey70]Next retry: {widgetConfig.Refresh}s[/]" },
                        new() { Content = $"[grey70]Consecutive errors: {errorCount + 1}[/]" },
                    },
                };

                _widgetDataCache[widgetId] = errorData;

                // Clear refreshing state BEFORE updating UI
                _isRefreshing[widgetId] = false;

                UpdateWidgetUI(widgetId, errorData);
            }
        }
        finally
        {
            // Track initial load completion
            if (isInitialLoad)
            {
                CompleteInitialLoad();
            }
        }
    }

    /// <summary>
    /// Called when each widget completes its initial load
    /// </summary>
    private static void CompleteInitialLoad()
    {
        lock (_initialLoadLock)
        {
            _initialLoadPendingCount--;

            // All widgets loaded - show success message
            if (_initialLoadPendingCount == 0 && _config != null)
            {
                var enabledCount = _config.Widgets.Values.Count(w => w.Enabled);
                _statusBarManager?.ShowSuccess($"{enabledCount} widget{(enabledCount != 1 ? "s" : "")} loaded", 3000);
            }
        }
    }

    private static void UpdateWidgetUI(string widgetId, WidgetData widgetData)
    {
        if (_mainWindow == null || _renderer == null)
            return;

        // Skip if widget no longer exists (race condition during reload)
        if (_config?.Widgets.ContainsKey(widgetId) == false)
            return;

        // Create a display copy with spinner if currently refreshing
        var displayData = widgetData;
        if (_isRefreshing.TryGetValue(widgetId, out var isRefreshing) && isRefreshing)
        {
            // Create a temporary copy for display only (HasError is computed from Error)
            displayData = new WidgetData
            {
                Title = widgetData.Title,
                Rows = widgetData.Rows,
                Error = widgetData.Error,
                Timestamp = widgetData.Timestamp,
            };
        }

        var control = _mainWindow.FindControl<IWindowControl>($"widget_{widgetId}");
        if (control != null)
        {
            // Get max lines: widget-specific > global > default 20
            var widgetConfig = _config?.Widgets.GetValueOrDefault(widgetId);
            int maxLines = widgetConfig?.MaxLines ?? _config?.MaxLinesPerWidget ?? 20;
            bool showIndicator = _config?.ShowTruncationIndicator ?? true;

            _renderer.UpdateWidgetPanel(control, displayData, maxLines, showIndicator);

            // Update full widget data for expansion dialog (use original data, not spinner version)
            _fullWidgetData[widgetId] = widgetData;
        }
    }

    /// <summary>
    /// Shows a modal dialog with the full widget content.
    /// The modal owns the widget while open - main dialog skips refresh for this widget.
    /// </summary>
    private static void ShowWidgetDialog(string widgetId)
    {
        // CRITICAL FIX: Prevent duplicate modal dialogs from race condition
        // When scroll happens between double-click events, multiple hit tests can find different
        // controls, causing the same double-click to be dispatched to multiple widgets.
        // This guard prevents opening a second modal if one is already open.
        if (_openModalWidgetId != null)
        {
            return;
        }

        if (!_fullWidgetData.TryGetValue(widgetId, out var widgetData))
            return;

        if (_windowSystem == null || _mainWindow == null || _renderer == null || _refreshService == null)
            return;

        // Set the modal tracking variable - main dialog will skip this widget
        _openModalWidgetId = widgetId;

        // Show self-contained dialog with its own refresh loop
        WidgetExpansionDialog.Show(
            widgetId,
            widgetData,
            _windowSystem,
            _renderer,
            _refreshService,
            onClose: () =>
            {
                var closedWidgetId = _openModalWidgetId;
                _openModalWidgetId = null;

                // Refresh the widget now that modal is closed
                if (closedWidgetId != null)
                {
                    var widgetConfig = _config?.Widgets.GetValueOrDefault(closedWidgetId);
                    if (widgetConfig != null)
                    {
                        _ = RefreshWidgetAsync(closedWidgetId, widgetConfig);
                    }
                }
            },
            onMainWidgetRefresh: async () =>
            {
                // Refresh main dashboard widget immediately after action completes
                // Use force=true to bypass the modal-open check
                var widgetConfig = _config?.Widgets.GetValueOrDefault(widgetId);
                if (widgetConfig != null)
                {
                    await RefreshWidgetAsync(widgetId, widgetConfig, force: true);
                }
            }
        );
    }

    private static string FormatRelativeTime(DateTime dateTime)
    {
        var elapsed = DateTime.Now - dateTime;

        if (elapsed.TotalSeconds < 60)
            return $"{(int)elapsed.TotalSeconds}s ago";
        else if (elapsed.TotalMinutes < 60)
            return $"{(int)elapsed.TotalMinutes}m ago";
        else if (elapsed.TotalHours < 24)
            return $"{(int)elapsed.TotalHours}h ago";
        else
            return $"{(int)elapsed.TotalDays}d ago";
    }

    private static int GetSystemCPU()
    {
        try
        {
            if (!File.Exists("/proc/loadavg"))
                return 0;

            var loadAvg = File.ReadAllText("/proc/loadavg").Split(' ')[0];
            var load = double.Parse(loadAvg);
            var cpuCores = Environment.ProcessorCount;
            return (int)((load / cpuCores) * 100);
        }
        catch
        {
            return 0;
        }
    }

    private static int GetSystemMemory()
    {
        try
        {
            if (!File.Exists("/proc/meminfo"))
                return 0;

            var memInfo = File.ReadAllLines("/proc/meminfo");
            long memTotal = 0;
            long memAvailable = 0;

            foreach (var line in memInfo)
            {
                if (line.StartsWith("MemTotal:"))
                    memTotal = long.Parse(
                        line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1]
                    );
                else if (line.StartsWith("MemAvailable:"))
                    memAvailable = long.Parse(
                        line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1]
                    );
            }

            if (memTotal == 0)
                return 0;

            var memUsed = memTotal - memAvailable;
            return (int)((memUsed * 100) / memTotal);
        }
        catch
        {
            return 0;
        }
    }

    private static void UpdateStatusBar()
    {
        if (_statusBarManager == null || _config == null)
            return;

        // Count widgets by state
        var totalConfigured = _config.Widgets.Count;
        var disabledCount = _config.Widgets.Values.Count(w => !w.Enabled);
        var enabledCount = totalConfigured - disabledCount;

        var errorWidgets = _widgetDataCache.Values.Count(w => w.HasError);
        var okWidgets = _widgetDataCache.Count - errorWidgets;

        var cpuUsage = GetSystemCPU();
        var memUsage = GetSystemMemory();

        // Update dashboard status (respects dev mode internally)
        _statusBarManager.UpdateDashboardStatus(
            enabledCount,
            disabledCount,
            okWidgets,
            errorWidgets,
            cpuUsage,
            memUsage,
            _isPaused
        );
    }

    private static void DisplayConfigurationError(ConfigurationException ex)
    {
        Console.Clear();

        // Title bar
        AnsiConsole.Write(new Rule($"[red]Configuration Error: {ex.GetType().Name.Replace("Exception", "")}[/]").LeftJustified());
        AnsiConsole.WriteLine();

        // Problem section
        AnsiConsole.MarkupLine($"[yellow]Problem:[/]");
        AnsiConsole.MarkupLine($"  {Markup.Escape(ex.Problem)}");
        AnsiConsole.WriteLine();

        // Additional info (if any)
        if (ex.AdditionalInfo.Count > 0)
        {
            foreach (var info in ex.AdditionalInfo)
            {
                if (info.EndsWith(":"))
                {
                    AnsiConsole.MarkupLine($"[cyan]{Markup.Escape(info)}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[grey]{Markup.Escape(info)}[/]");
                }
            }
            AnsiConsole.WriteLine();
        }

        // How to fix section
        AnsiConsole.MarkupLine($"[green]How to fix:[/]");
        AnsiConsole.MarkupLine($"  {Markup.Escape(ex.HowToFix)}");
        AnsiConsole.WriteLine();

        // Config file location
        if (!string.IsNullOrEmpty(ex.ConfigPath))
        {
            AnsiConsole.MarkupLine($"[grey]Config file:[/] {Markup.Escape(ex.ConfigPath)}");
            AnsiConsole.WriteLine();
        }

        // Footer
        AnsiConsole.MarkupLine("[grey]Press F2 in ServerHub to edit configuration, or edit the file manually.[/]");
    }

    private static void DisplayGenericError(Exception ex)
    {
        Console.Clear();

        AnsiConsole.Write(new Rule("[red]Error[/]").LeftJustified());
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine($"[yellow]An error occurred:[/]");
        AnsiConsole.MarkupLine($"  {Markup.Escape(ex.Message)}");
        AnsiConsole.WriteLine();

        if (ex.InnerException != null)
        {
            AnsiConsole.MarkupLine($"[grey]Caused by:[/]");
            AnsiConsole.MarkupLine($"  {Markup.Escape(ex.InnerException.Message)}");
            AnsiConsole.WriteLine();
        }

        // Show full exception in verbose mode or for unexpected errors
        AnsiConsole.MarkupLine("[grey]For detailed error information, see below:[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    }

    private static void ShowEmptyDashboardMessage()
    {
        if (_mainWindow == null || _config == null)
            return;

        var enabledCount = _config.Widgets.Values.Count(w => w.Enabled);
        var disabledCount = _config.Widgets.Count - enabledCount;

        string message;
        if (_config.Widgets.Count == 0)
        {
            message = @"[cyan]Welcome to ServerHub![/]

No widgets configured yet.

[yellow]Press F2[/] to open the configuration dialog and add widgets.
You can discover available widgets and add them to your dashboard.";
        }
        else if (enabledCount == 0)
        {
            message = $@"[yellow]All {disabledCount} widgets are disabled[/]

[yellow]Press F2[/] to open the configuration dialog.
Select a disabled widget and check 'Enabled' to show it on the dashboard.";
        }
        else
        {
            return; // Don't show message if there are enabled widgets
        }

        var panel = Controls.Markup()
            .AddLine("")
            .AddLine(message)
            .AddLine("")
            .WithMargin(4, 2, 4, 2)
            .Build();

        _mainWindow.AddControl(panel);
    }

    private static int GetColumnCountFromWidth(int terminalWidth)
    {
        // Use config breakpoints if available, otherwise use defaults
        var breakpoints =
            _config?.Breakpoints
            ?? new BreakpointConfig
            {
                Double = 100,
                Triple = 160,
                Quad = 220,
            };

        if (terminalWidth < breakpoints.Double)
            return 1;
        else if (terminalWidth < breakpoints.Triple)
            return 2;
        else if (terminalWidth < breakpoints.Quad)
            return 3;
        else
            return 4;
    }

    // Utility methods moved to DefaultCommand

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

    /// <summary>
    /// Register system command callbacks for command palette
    /// </summary>
    private static void RegisterCommandPaletteCallbacks()
    {
        if (_commandPaletteService == null)
            return;

        _commandPaletteService.RegisterSystemCommand("system-refresh-all", RefreshAllWidgets);
        _commandPaletteService.RegisterSystemCommand("system-toggle-pause", TogglePause);
        _commandPaletteService.RegisterSystemCommand("system-configure", ShowConfigDialog);
        _commandPaletteService.RegisterSystemCommand("system-marketplace", ShowMarketplaceBrowser);
        _commandPaletteService.RegisterSystemCommand("system-help", ShowHelpOverlay);
    }

    /// <summary>
    /// Show command palette dialog
    /// </summary>
    private static void ShowCommandPalette()
    {
        if (_windowSystem == null || _commandPaletteService == null)
            return;

        // Gather all commands
        var commands = _commandPaletteService.GetAllCommands(_widgetDataCache, _focusManager);

        // Show command palette
        CommandPaletteDialog.Show(_windowSystem, selectedCommand =>
        {
            if (selectedCommand != null)
            {
                ExecutePaletteCommand(selectedCommand);
            }
        }, commands);
    }

    /// <summary>
    /// Execute a command selected from the palette
    /// </summary>
    private static void ExecutePaletteCommand(PaletteCommand command)
    {
        if (_windowSystem == null)
            return;

        switch (command.Type)
        {
            case CommandType.System:
                // Execute system command (refresh, pause, configure, etc.)
                command.Execute?.Invoke();
                break;

            case CommandType.Navigation:
                // Focus widget
                if (command.WidgetId != null && _focusManager != null)
                {
                    _focusManager.FocusWidget(command.WidgetId);
                }
                break;

            case CommandType.WidgetAction:
                // Execute widget action
                if (command.Action != null)
                {
                    // Show action execution dialog (no parent, returns to dashboard)
                    ActionExecutionDialog.Show(
                        command.Action,
                        _windowSystem,
                        parentWindow: null,  // No parent - returns to dashboard
                        onComplete: async (result) =>
                        {
                            // Refresh widget if action requests it
                            if (command.Action.RefreshAfterSuccess && result.ExitCode == 0 && command.WidgetId != null && _config != null)
                            {
                                var widgetConfig = _config.Widgets.GetValueOrDefault(command.WidgetId);
                                if (widgetConfig != null)
                                {
                                    await RefreshWidgetAsync(command.WidgetId, widgetConfig, force: true);
                                }
                            }
                        }
                    );
                }
                break;

            case CommandType.Recent:
                // Re-execute recent command
                command.Execute?.Invoke();
                break;
        }
    }

}
