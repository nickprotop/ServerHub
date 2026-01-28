// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

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

class Program
{
    private static ConsoleWindowSystem? _windowSystem;
    private static Window? _mainWindow;
    private static ServerHubConfig? _config;
    private static readonly Dictionary<string, WidgetData> _widgetDataCache = new();
    private static readonly Dictionary<string, Timer> _widgetTimers = new();
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

    static async Task<int> Main(string[] args)
    {
        try
        {
            // Parse command-line arguments
            var options = ParseArguments(args);

            if (options.ShowHelp)
            {
                ShowHelp();
                return 0;
            }

            if (options.ShowVersion)
            {
                Console.WriteLine("ServerHub v0.1.0");
                return 0;
            }

            // Handle marketplace commands
            if (options.MarketplaceArgs != null && options.MarketplaceArgs.Length > 0)
            {
                // Determine installation path using centralized logic
                var marketplaceInstallPath = WidgetPaths.GetMarketplaceInstallPath();

                // Determine config path: custom config or default
                var marketplaceConfigPath = options.ConfigPath ?? ConfigManager.GetDefaultConfigPath();

                var marketplaceCmd = new MarketplaceCommand(marketplaceInstallPath, marketplaceConfigPath);
                return await marketplaceCmd.ExecuteAsync(options.MarketplaceArgs);
            }

            // Set custom widgets path if provided (before other operations)
            if (!string.IsNullOrEmpty(options.WidgetsPath))
            {
                if (!Directory.Exists(options.WidgetsPath))
                {
                    Console.Error.WriteLine(
                        $"Error: Widgets path does not exist: {options.WidgetsPath}"
                    );
                    return 1;
                }
                WidgetPaths.SetCustomWidgetsPath(options.WidgetsPath);
                Console.WriteLine($"Using custom widgets path: {options.WidgetsPath}");
            }

            if (options.Discover)
            {
                return await DiscoverWidgetsAsync();
            }

            if (options.VerifyChecksums)
            {
                return await VerifyChecksumsAsync(options.ConfigPath);
            }

            // Handle --init-config command
            if (!string.IsNullOrEmpty(options.InitConfig))
            {
                return await InitConfigAsync(options.InitConfig, options.WidgetsPath);
            }

            // Ensure directories exist
            WidgetPaths.EnsureDirectoriesExist();

            // Load configuration
            var configPath = options.ConfigPath ?? ConfigManager.GetDefaultConfigPath();
            _configPath = configPath;
            var configMgr = new ConfigManager();

            // Auto-create ONLY the default config path for first-time users
            var isDefaultPath = configPath == ConfigManager.GetDefaultConfigPath();

            if (!File.Exists(configPath))
            {
                if (isDefaultPath)
                {
                    // First-time user: silent auto-create with production template
                    // Uses existing CreateDefaultConfig() - just bundled widgets, no custom widget scanning
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
                    if (!string.IsNullOrEmpty(options.WidgetsPath))
                        Console.Error.WriteLine($"            --widgets-path {options.WidgetsPath}");
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Or use the default configuration:");
                    Console.Error.WriteLine("  serverhub");
                    return 1;
                }
            }

            _config = configMgr.LoadConfig(configPath);
            _lastConfigLoadTime = File.GetLastWriteTime(configPath);

            // Store dev mode state for use throughout the application
            _devMode = options.DevMode;

            // Initialize services
            var validator = new ScriptValidator(devMode: options.DevMode);
            _executor = new ScriptExecutor(validator);
            _parser = new WidgetProtocolParser();
            _renderer = new WidgetRenderer();
            _layoutEngine = new LayoutEngine();
            _refreshService = new WidgetRefreshService(_executor, _parser, _config);

            // Initialize ConsoleEx window system
            _windowSystem = new ConsoleWindowSystem(
                new NetConsoleDriver(RenderMode.Buffer),
                options: new ConsoleWindowSystemOptions(
                    StatusBarOptions: new StatusBarOptions(
                        ShowTaskBar: false,
                        ShowBottomStatus: true
                    )
                ))
            {
                // Layer 1: Top status bar warning in dev mode
                TopStatus = _devMode
                    ? "DEV MODE - Custom widget checksums DISABLED"
                    : "ServerHub - Server Monitoring Dashboard",
                BottomStatus = "Press Ctrl+Q to quit | F5 to refresh | ? for help",
            };

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
        _focusManager.FocusFirst(); // Focus first widget on startup

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
            else
            {
                _focusManager?.FocusFirst();
            }

            // Update status
            _windowSystem.BottomStatus =
                $"Layout rebuilt for {terminalWidth} cols | Ctrl+Q to quit | F5 to refresh | ? for help";
        }
        catch (Exception ex)
        {
            // If rebuild fails, just log it - don't crash the app
            if (_windowSystem != null)
            {
                _windowSystem.BottomStatus = $"Layout rebuild error: {ex.Message}";
            }
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

        // Calculate base column width
        const int spacingBetweenWidgets = 1;
        int totalSpacing = Math.Max(0, (columnCount - 1) * spacingBetweenWidgets);
        int availableWidth = terminalWidth - totalSpacing;
        int baseColumnWidth = availableWidth / columnCount;

        // Group placements by row
        var rowGroups = placements.GroupBy(p => p.Row).OrderBy(g => g.Key);

        // Alternating background colors for widgets
        var widgetColors = new[]
        {
            Spectre.Console.Color.Grey15,
            Spectre.Console.Color.Grey19,
            Spectre.Console.Color.Grey23,
        };
        int widgetColorIndex = 0;

        // Build rows
        bool firstRow = true;
        foreach (var rowGroup in rowGroups)
        {
            // Add vertical spacing between rows (not before first row)
            if (!firstRow)
            {
                var spacer = Controls
                    .Markup("")
                    .WithBackgroundColor(Spectre.Console.Color.Grey11)
                    .WithMargin(0, 0, 0, 0)
                    .Build();
                _mainWindow.AddControl(spacer);
            }
            firstRow = false;

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

                // Get alternating background color
                var bgColor = widgetColors[widgetColorIndex % widgetColors.Length];
                widgetColorIndex++;
                widgetBgColors.Add(bgColor);
                widgetIdByColumnIndex.Add(placement.WidgetId); // Track widget for this column

                // Get max lines: widget-specific > global > default 20
                var widgetConfig = _config?.Widgets.GetValueOrDefault(placement.WidgetId);
                int maxLines = widgetConfig?.MaxLines ?? _config?.MaxLinesPerWidget ?? 20;
                bool showIndicator = _config?.ShowTruncationIndicator ?? true;

                var widgetPanel = _renderer.CreateWidgetPanel(
                    placement.WidgetId,
                    widgetData,
                    placement.IsPinned,
                    bgColor,
                    widgetId => _focusManager?.FocusWidget(widgetId),
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

                // Wire up click and double-click events for widget columns (skip spacer columns)
                if (i < widgetIdByColumnIndex.Count && widgetIdByColumnIndex[i] != null)
                {
                    var widgetId = widgetIdByColumnIndex[i]!;

                    // Single click: focus widget
                    builtRowGrid.Columns[i].MouseClick += (sender, e) =>
                    {
                        _focusManager?.FocusWidget(widgetId);
                    };

                    // Double-click: open expansion dialog
                    builtRowGrid.Columns[i].MouseDoubleClick += (sender, e) =>
                    {
                        ShowWidgetDialog(widgetId);
                    };
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
            var status = _isPaused
                ? "[yellow]PAUSED[/] - Press Space to resume | Ctrl+Q to quit | ? for help"
                : $"Press Ctrl+Q to quit | F5 to refresh | Space to pause | ? for help | {DateTime.Now:HH:mm:ss}";

            _windowSystem.BottomStatus = status;
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
        var dialogWidth = Math.Min(80, terminalWidth - 10);
        var dialogHeight = Math.Min(22, terminalHeight - 6);

        // Center the dialog
        var dialogX = (terminalWidth - dialogWidth) / 2;
        var dialogY = (terminalHeight - dialogHeight) / 2;

        // Create help window without borders, centered with explicit bounds
        var helpWindow = new WindowBuilder(_windowSystem)
            .WithName("HelpOverlay")
            .WithBounds(dialogX, dialogY, dialogWidth, dialogHeight)
            .Borderless()
            .WithColors(Color.Grey11, Color.Grey93)
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
  [bold cyan1]╔══════════════════════════════════════════════════════════════════╗[/]
  [bold cyan1]║[/]  [bold yellow]ServerHub - Server Monitoring Dashboard[/]                         [bold cyan1]║[/]
  [bold cyan1]╚══════════════════════════════════════════════════════════════════╝[/]

  [bold white]━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━[/]

  [bold cyan1]Navigation[/]
  [grey]─────────────────────────────────────────────────────────────────────[/]
    [cyan1]Tab / Shift+Tab[/]      [white]Move between widgets[/]
    [cyan1]Arrow keys[/]           [white]Scroll within widget[/]
    [cyan1]Ctrl+←/→[/]             [white]Swap widget left/right in row[/]
    [cyan1]Ctrl+↑/↓[/]             [white]Swap widget with row above/below[/]
    [cyan1]Ctrl+Shift+←/→[/]       [white]Decrease/increase widget width[/]
    [cyan1]Ctrl+Shift+↑/↓[/]       [white]Decrease/increase widget height[/]

  [bold cyan1]Actions[/]
  [grey]─────────────────────────────────────────────────────────────────────[/]
    [cyan1]F2[/]                   [white]Configure widgets (add/remove/edit)[/]
    [cyan1]F3[/]                   [white]Browse marketplace (install widgets)[/]
    [cyan1]F5[/]                   [white]Refresh all widgets[/]
    [cyan1]Space[/]                [white]Pause / resume widget refresh[/]
    [cyan1]? or F1[/]              [white]Show this help dialog[/]

  [bold cyan1]Exit[/]
  [grey]─────────────────────────────────────────────────────────────────────[/]
    [cyan1]Ctrl+Q[/]               [white]Exit ServerHub[/]

  [bold white]━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━[/]
  [grey]Press ESC, Enter, ?, or F1 to close[/]
";

        var helpBuilder = Controls
            .Markup()
            .WithBackgroundColor(Color.Grey11)
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
            // Show in bottom status bar (persistent warning)
            _windowSystem.BottomStatus =
                $"{unconfigured.Count} unconfigured script(s) found. Run --discover to review.";
        }
    }

    private static void HandleKeyPress(object? sender, KeyPressedEventArgs e)
    {
        // ===== PRIORITY 0: Widget Expansion =====
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
            }

            e.Handled = true;
            return;
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
            }

            e.Handled = true;
            return;
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

                    // Reinitialize refresh service with new config
                    if (_executor != null && _parser != null)
                    {
                        _refreshService = new WidgetRefreshService(_executor, _parser, _config);
                    }

                    // Restart widget timers with new configuration
                    StopWidgetRefreshTimers();
                    StartWidgetRefreshTimers();

                    // Rebuild the layout
                    RebuildLayout();

                    // Update status
                    if (_windowSystem != null)
                    {
                        _windowSystem.BottomStatus = "Widget installed and configuration reloaded";
                        Task.Delay(3000).ContinueWith(_ => UpdateStatusBar());
                    }
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

            // Reinitialize refresh service with new config
            if (_executor != null && _parser != null)
            {
                _refreshService = new WidgetRefreshService(_executor, _parser, _config);
            }

            // Restart widget timers with new configuration
            StopWidgetRefreshTimers();
            StartWidgetRefreshTimers();

            // Rebuild the layout
            RebuildLayout();

            // Update status
            if (_windowSystem != null)
            {
                _windowSystem.BottomStatus = "Configuration reloaded from disk";
                Task.Delay(3000).ContinueWith(_ => UpdateStatusBar());
            }
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

                    if (_windowSystem != null)
                    {
                        _windowSystem.BottomStatus = $"Swapped '{widgetId}' {directionText}";
                    }

                    // Restore normal status after 3 seconds
                    Task.Delay(3000).ContinueWith(_ => UpdateStatusBar());
                }
                catch (Exception ex)
                {
                    if (_windowSystem != null)
                    {
                        _windowSystem.BottomStatus = $"Failed to save config: {ex.Message}";
                    }
                }
            }
        );

        if (!moved)
        {
            // At boundary or other issue - provide feedback
            if (_windowSystem != null)
            {
                var message = direction switch
                {
                    FocusManager.VisualReorderDirection.VisualLeft => "No widget to the left",
                    FocusManager.VisualReorderDirection.VisualRight => "No widget to the right",
                    FocusManager.VisualReorderDirection.VisualUp => "No widget above",
                    FocusManager.VisualReorderDirection.VisualDown => "No widget below",
                    _ => "Cannot reorder"
                };

                _windowSystem.BottomStatus = message;
                Task.Delay(2000).ContinueWith(_ => UpdateStatusBar());
            }
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

                    if (_windowSystem != null)
                    {
                        _windowSystem.BottomStatus = $"Resized '{widgetId}' {propertyName} to {newValue}";
                    }

                    Task.Delay(3000).ContinueWith(_ => UpdateStatusBar());
                }
                catch (Exception ex)
                {
                    if (_windowSystem != null)
                    {
                        _windowSystem.BottomStatus = $"Failed to save config: {ex.Message}";
                    }
                }
            }
        );

        if (!resized)
        {
            if (_windowSystem != null)
            {
                var message = direction switch
                {
                    FocusManager.ResizeDirection.WidthDecrease => "Minimum width reached",
                    FocusManager.ResizeDirection.WidthIncrease => "Maximum width reached",
                    FocusManager.ResizeDirection.HeightDecrease => "Minimum height reached",
                    FocusManager.ResizeDirection.HeightIncrease => "Maximum height reached",
                    _ => "Cannot resize"
                };

                _windowSystem.BottomStatus = message;
                Task.Delay(2000).ContinueWith(_ => UpdateStatusBar());
            }
            return;
        }

        RebuildLayout();
    }

    private static void StartWidgetRefreshTimers()
    {
        if (_config == null)
            return;

        foreach (var (widgetId, widgetConfig) in _config.Widgets)
        {
            // Skip disabled widgets - no timers, no initial fetch
            if (!widgetConfig.Enabled)
                continue;

            // Initial fetch
            _ = RefreshWidgetAsync(widgetId, widgetConfig);

            // Setup periodic refresh
            var timer = new Timer(
                async _ => await RefreshWidgetAsync(widgetId, widgetConfig),
                null,
                TimeSpan.FromSeconds(widgetConfig.Refresh),
                TimeSpan.FromSeconds(widgetConfig.Refresh)
            );

            _widgetTimers[widgetId] = timer;
        }
    }

    private static void StopWidgetRefreshTimers()
    {
        foreach (var timer in _widgetTimers.Values)
        {
            timer.Dispose();
        }
        _widgetTimers.Clear();
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

    private static async Task RefreshWidgetAsync(string widgetId, WidgetConfig widgetConfig, bool force = false)
    {
        if (_executor == null || _parser == null || _mainWindow == null)
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

        // Mark as refreshing
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

            // Execute script
            var result = await _executor.ExecuteAsync(scriptPath, null, widgetConfig.Sha256);

            WidgetData widgetData;
            if (result.IsSuccess)
            {
                // Parse output
                widgetData = _parser.Parse(result.Output ?? "");
                widgetData.Timestamp = DateTime.Now;

                // Track successful update
                _lastSuccessfulUpdate[widgetId] = DateTime.Now;
                _consecutiveErrors[widgetId] = 0;
            }
            else
            {
                // Track error
                _consecutiveErrors.TryGetValue(widgetId, out var errorCount);
                _consecutiveErrors[widgetId] = errorCount + 1;

                var lastUpdate = _lastSuccessfulUpdate.TryGetValue(widgetId, out var lastTime)
                    ? $"Last update: {FormatRelativeTime(lastTime)}"
                    : "Never updated successfully";

                // Create enhanced error widget
                widgetData = new WidgetData
                {
                    Title = widgetId,
                    Error = result.ErrorMessage ?? "Unknown error",
                    Timestamp = DateTime.Now,
                    Rows = new List<WidgetRow>
                    {
                        new()
                        {
                            Content = $"[red]Widget Error[/]",
                            Status = new() { State = StatusState.Error },
                        },
                        new() { Content = "" },
                        new() { Content = $"[grey70]{result.ErrorMessage ?? "Unknown error"}[/]" },
                        new() { Content = "" },
                        new() { Content = $"[grey70]{lastUpdate}[/]" },
                        new() { Content = $"[grey70]Next retry: {widgetConfig.Refresh}s[/]" },
                        new() { Content = $"[grey70]Consecutive errors: {errorCount + 1}[/]" },
                    },
                };
            }

            _widgetDataCache[widgetId] = widgetData;

            // Clear refreshing state BEFORE updating UI
            _isRefreshing[widgetId] = false;

            UpdateWidgetUI(widgetId, widgetData);
        }
        catch (Exception ex)
        {
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

    private static void UpdateWidgetUI(string widgetId, WidgetData widgetData)
    {
        if (_mainWindow == null || _renderer == null)
            return;

        // Create a display copy with spinner if currently refreshing
        var displayData = widgetData;
        if (_isRefreshing.TryGetValue(widgetId, out var isRefreshing) && isRefreshing)
        {
            // Create a temporary copy for display only (HasError is computed from Error)
            displayData = new WidgetData
            {
                Title = $"{widgetData.Title} {_spinnerFrames[_spinnerFrame % 4]}",
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
        if (_windowSystem == null || _config == null)
            return;

        // Count widgets by state
        var totalConfigured = _config.Widgets.Count;
        var disabledCount = _config.Widgets.Values.Count(w => !w.Enabled);
        var enabledCount = totalConfigured - disabledCount;

        var errorWidgets = _widgetDataCache.Values.Count(w => w.HasError);
        var okWidgets = _widgetDataCache.Count - errorWidgets;

        var cpuUsage = GetSystemCPU();
        var memUsage = GetSystemMemory();

        var status = _isPaused
            ? $"[yellow]PAUSED[/] | {enabledCount} widgets ({disabledCount} disabled) | Press Space to resume | [dim]F2: Config  F1: Help[/]"
            : $"ServerHub | {enabledCount} widgets ({okWidgets} ok, {errorWidgets} error{(disabledCount > 0 ? $", {disabledCount} disabled" : "")}) | CPU {cpuUsage}% MEM {memUsage}% | {DateTime.Now:HH:mm:ss} | [dim]F2: Config  F1: Help[/]";

        _windowSystem.BottomStatus = status;
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

    private static CommandLineOptions ParseArguments(string[] args)
    {
        var options = new CommandLineOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "marketplace":
                    // Capture all remaining args for marketplace command
                    options.MarketplaceArgs = args.Skip(i + 1).ToArray();
                    return options;

                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;

                case "--version":
                case "-v":
                    options.ShowVersion = true;
                    break;

                case "--widgets-path":
                    if (i + 1 < args.Length)
                    {
                        options.WidgetsPath = args[++i];
                    }
                    break;

                case "--discover":
                    options.Discover = true;
                    break;

                case "--verify-checksums":
                    options.VerifyChecksums = true;
                    break;

                case "--dev-mode":
                    options.DevMode = true;
                    break;

                case "--init-config":
                    if (i + 1 < args.Length)
                    {
                        options.InitConfig = args[++i];
                    }
                    else
                    {
                        Console.Error.WriteLine("Error: --init-config requires a path argument");
                        return options;
                    }
                    break;

                default:
                    if (!args[i].StartsWith("--") && options.ConfigPath == null)
                    {
                        options.ConfigPath = args[i];
                    }
                    break;
            }
        }

        return options;
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
                    Console.WriteLine($"    {i + 1, 3}  {lines[i]}");
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

    /// <summary>
    /// Creates a new configuration file by discovering all available widgets
    /// Starts with production config (keeps all bundled widgets as-is) and adds custom widgets
    /// </summary>
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

            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error saving config: {ex.Message}");
            return Task.FromResult(1);
        }
    }

    /// <summary>
    /// Generates a unique widget ID by adding suffix if needed
    /// </summary>
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
        var bundledChecksums = Config.BundledWidgets.Checksums;
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
                var rel = Path.GetRelativePath(bundledPath, resolved);
                bundledChecksums.TryGetValue(rel, out expected);
                source = "bundled";
            }

            if (expected == null)
            {
                AnsiConsole.MarkupLine($"  {id,-20} [yellow]NO CHECKSUM[/]");
                Console.WriteLine($"      Run --discover or manually verify before adding checksum");
                missing++;
            }
            else if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"  {id,-20} [green]VALID[/] ({source})");
                passed++;
            }
            else
            {
                AnsiConsole.MarkupLine($"  {id,-20} [red]MISMATCH[/] ({source})");
                Console.WriteLine($"      Expected: {expected}");
                Console.WriteLine($"      Actual:   {actual}");
                failed++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Results: {passed} valid, {failed} mismatch, {missing} missing/no-checksum");

        return Task.FromResult(failed > 0 ? 1 : 0);
    }


    private static bool IsTextFile(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".sh" or ".bash" or ".py" or ".rb" or ".pl" or ".js" or ".txt")
                return true;

            // Read first 1KB and check for null bytes
            var buffer = new byte[1024];
            using var fs = File.OpenRead(path);
            var read = fs.Read(buffer, 0, buffer.Length);
            return !buffer.Take(read).Contains((byte)0);
        }
        catch
        {
            return false;
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("ServerHub - Server Monitoring Dashboard");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  serverhub [options] [config.yaml]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --help                       Show this help message");
        Console.WriteLine("  -v, --version                    Show version information");
        Console.WriteLine("  --widgets-path <path>            Load widgets from custom directory");
        Console.WriteLine(
            "                                   Searches this path first, before default paths"
        );
        Console.WriteLine("  --init-config <path>             Initialize a new configuration file by");
        Console.WriteLine(
            "                                   discovering available widgets"
        );
        Console.WriteLine("  --discover                       Find and add new custom widgets to config");
        Console.WriteLine(
            "  --verify-checksums               Verify checksums for all configured widgets"
        );
        Console.WriteLine(
            "  --dev-mode                       Skip checksum validation for custom widgets"
        );
        Console.WriteLine(
            "                                   WARNING: Bundled widgets are still validated"
        );
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine(
            "  serverhub                                    Use default config (~/.config/serverhub/config.yaml)"
        );
        Console.WriteLine("  serverhub myconfig.yaml                      Use custom config file");
        Console.WriteLine(
            "  serverhub --init-config config.yaml          Create config.yaml by discovering all available widgets"
        );
        Console.WriteLine(
            "  serverhub --init-config config.dev.yaml --widgets-path ./widgets/"
        );
        Console.WriteLine(
            "                                               Create config with bundled + custom + ./widgets/ directory widgets"
        );
        Console.WriteLine(
            "  serverhub --widgets-path ./dev-widgets       Load widgets from ./dev-widgets"
        );
        Console.WriteLine(
            "  serverhub --dev-mode --widgets-path ./dev    Development mode with custom path"
        );
        Console.WriteLine(
            "  serverhub --discover                         Discover and add new widgets to config"
        );
        Console.WriteLine(
            "  serverhub --verify-checksums                 Verify all widget checksums"
        );
        Console.WriteLine();
        Console.WriteLine("Marketplace:");
        Console.WriteLine(
            "  serverhub marketplace search <query>         Search for community widgets"
        );
        Console.WriteLine(
            "  serverhub marketplace list                   List all available widgets"
        );
        Console.WriteLine(
            "  serverhub marketplace info <widget-id>       Show widget details"
        );
        Console.WriteLine(
            "  serverhub marketplace install <widget-id>    Install widget from marketplace"
        );
        Console.WriteLine();
        Console.WriteLine("Widget Search Paths (in priority order):");
        Console.WriteLine("  1. Custom path (if --widgets-path specified)");
        Console.WriteLine("  2. ~/.config/serverhub/widgets/              User custom widgets");
        Console.WriteLine("  3. ~/.local/share/serverhub/widgets/         Bundled widgets");
        Console.WriteLine();
        Console.WriteLine("Security:");
        Console.WriteLine("  All custom widgets require sha256 checksums in config.yaml.");
        Console.WriteLine("  Use --discover to add widgets with checksums automatically.");
        Console.WriteLine("  Use --dev-mode only for development (disables custom widget checks).");
        Console.WriteLine();
        Console.WriteLine("Keyboard Shortcuts:");
        Console.WriteLine("  Ctrl+Q    Quit");
        Console.WriteLine("  F5        Refresh all widgets");
    }

    private class CommandLineOptions
    {
        public bool ShowHelp { get; set; }
        public bool ShowVersion { get; set; }
        public string? WidgetsPath { get; set; }
        public string? ConfigPath { get; set; }
        public bool Discover { get; set; }
        public bool VerifyChecksums { get; set; }
        public bool DevMode { get; set; }
        public string? InitConfig { get; set; }
        public string[]? MarketplaceArgs { get; set; }
    }
}
