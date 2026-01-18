// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Models;
using ServerHub.Services;
using ServerHub.UI;
using ServerHub.Utils;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;
using Spectre.Console;

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

            if (options.ComputeChecksums)
            {
                return await ComputeChecksumsAsync(options.ConfigPath);
            }

            // Ensure directories exist
            WidgetPaths.EnsureDirectoriesExist();

            // Load configuration
            var configPath = options.ConfigPath ?? ConfigManager.GetDefaultConfigPath();
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Configuration file not found: {configPath}");
                Console.WriteLine("Creating default configuration...");

                var configManager = new ConfigManager();
                configManager.CreateDefaultConfig(configPath);

                Console.WriteLine($"Default configuration created at: {configPath}");
                Console.WriteLine("Edit the configuration file and run ServerHub again.");
                return 0;
            }

            var configMgr = new ConfigManager();
            _config = configMgr.LoadConfig(configPath);

            // Initialize services
            var validator = new ScriptValidator();
            _executor = new ScriptExecutor(validator);
            _parser = new WidgetProtocolParser();
            _renderer = new WidgetRenderer();
            _layoutEngine = new LayoutEngine();

            // Initialize ConsoleEx window system
            _windowSystem = new ConsoleWindowSystem(new NetConsoleDriver(RenderMode.Buffer))
            {
                TopStatus = "ServerHub - Server Monitoring Dashboard",
                ShowTaskBar = false,
                ShowBottomStatus = true,
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

            // Start widget refresh timers
            StartWidgetRefreshTimers();

            // Run the application
            await Task.Run(() => _windowSystem.Run());

            // Cleanup
            StopWidgetRefreshTimers();

            return 0;
        }
        catch (Exception ex)
        {
            Console.Clear();
            AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    private static void CreateMainWindow()
    {
        if (_windowSystem == null || _config == null)
            return;

        _mainWindow = new WindowBuilder(_windowSystem)
            .WithTitle("ServerHub")
            .WithName("MainWindow")
            .WithColors(Spectre.Console.Color.Grey11, Spectre.Console.Color.Grey93)
            .Borderless()
            .Maximized()
            .WithAsyncWindowThread(UpdateDashboardAsync)
            .OnKeyPressed(HandleKeyPress)
            .Build();

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

                var widgetPanel = _renderer.CreateWidgetPanel(
                    placement.WidgetId,
                    widgetData,
                    placement.IsPinned,
                    bgColor,
                    widgetId => _focusManager?.FocusWidget(widgetId)
                );

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

                // Wire up click event for widget columns (skip spacer columns)
                if (i < widgetIdByColumnIndex.Count && widgetIdByColumnIndex[i] != null)
                {
                    var widgetId = widgetIdByColumnIndex[i]!;
                    builtRowGrid.Columns[i].MouseClick += (sender, e) =>
                    {
                        _focusManager?.FocusWidget(widgetId);
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

                await Task.Delay(1000, ct);
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

  [bold cyan1]Actions[/]
  [grey]─────────────────────────────────────────────────────────────────────[/]
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

    private static void HandleKeyPress(object? sender, KeyPressedEventArgs e)
    {
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

        // ===== PRIORITY 2: Reserved for Future Features =====
        // Ctrl+Arrow keys reserved for widget reordering
        if (e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            switch (e.KeyInfo.Key)
            {
                case ConsoleKey.UpArrow:
                case ConsoleKey.DownArrow:
                case ConsoleKey.LeftArrow:
                case ConsoleKey.RightArrow:
                    // Future: _focusManager?.ReorderWidget(direction);
                    e.Handled = true; // Reserve keybinding
                    return;
            }
        }

        // ===== PRIORITY 3: Existing Application Shortcuts =====
        if (e.KeyInfo.Key == ConsoleKey.Q && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            _windowSystem?.Shutdown(0);
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F5)
        {
            // Refresh all widgets
            RefreshAllWidgets();
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
    }

    private static void StartWidgetRefreshTimers()
    {
        if (_config == null)
            return;

        foreach (var (widgetId, widgetConfig) in _config.Widgets)
        {
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
            _ = RefreshWidgetAsync(widgetId, widgetConfig);
        }
    }

    private static async Task RefreshWidgetAsync(string widgetId, WidgetConfig widgetConfig)
    {
        if (_executor == null || _parser == null || _mainWindow == null)
            return;

        // Skip refresh when paused
        if (_isPaused)
            return;

        // Mark as refreshing
        _isRefreshing[widgetId] = true;

        try
        {
            // Resolve widget path
            var scriptPath = WidgetPaths.ResolveWidgetPath(widgetConfig.Path);
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
            _renderer.UpdateWidgetPanel(control, displayData);
        }
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
        if (_windowSystem == null)
            return;

        var totalWidgets = _widgetDataCache.Count;
        var errorWidgets = _widgetDataCache.Values.Count(w => w.HasError);
        var okWidgets = totalWidgets - errorWidgets;

        var cpuUsage = GetSystemCPU();
        var memUsage = GetSystemMemory();

        var status = _isPaused
            ? $"[yellow]PAUSED[/] | {totalWidgets} widgets | Press Space to resume"
            : $"ServerHub | {totalWidgets} widgets ({okWidgets} ok, {errorWidgets} error) | CPU {cpuUsage}% MEM {memUsage}% | {DateTime.Now:HH:mm:ss}";

        _windowSystem.BottomStatus = status;
    }

    private static int GetColumnCountFromWidth(int terminalWidth)
    {
        // Use config breakpoints if available, otherwise use defaults
        var breakpoints =
            _config?.Breakpoints
            ?? new BreakpointConfig
            {
                Single = 0,
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

                case "--compute-checksums":
                    options.ComputeChecksums = true;
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

    private static async Task<int> DiscoverWidgetsAsync()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var customWidgetsPath = Path.Combine(home, ".config", "serverhub", "widgets");
        var configPath =
            _config != null
                ? ConfigManager.GetDefaultConfigPath()
                : Path.Combine(home, ".config", "serverhub", "config.yaml");

        if (!Directory.Exists(customWidgetsPath))
        {
            Console.WriteLine($"No custom widgets directory found: {customWidgetsPath}");
            Console.WriteLine("Create it and add your widget scripts there.");
            return 0;
        }

        // Load existing config to know which widgets are already configured
        var configuredPaths = new HashSet<string>();
        if (File.Exists(configPath))
        {
            var configManager = new ConfigManager();
            try
            {
                var config = configManager.LoadConfig(configPath);
                foreach (var widget in config.Widgets.Values)
                {
                    var resolved = WidgetPaths.ResolveWidgetPath(widget.Path);
                    if (resolved != null)
                    {
                        configuredPaths.Add(Path.GetFullPath(resolved));
                    }
                }
            }
            catch
            { /* Ignore config load errors */
            }
        }

        // Find all executables in custom widgets directory
        var files = Directory
            .GetFiles(customWidgetsPath)
            .Where(f => IsExecutable(f) && !configuredPaths.Contains(Path.GetFullPath(f)))
            .ToList();

        if (files.Count == 0)
        {
            Console.WriteLine("No unconfigured widgets found.");
            return 0;
        }

        Console.WriteLine($"Found {files.Count} unconfigured widget(s):\n");

        var approved = new List<string>();

        foreach (var file in files)
        {
            var filename = Path.GetFileName(file);
            var fileInfo = new FileInfo(file);
            var checksum = ScriptValidator.CalculateChecksum(file);

            Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.WriteLine($"Widget: {filename}");
            Console.WriteLine($"Path:   {file}");
            Console.WriteLine($"Size:   {fileInfo.Length} bytes");
            Console.WriteLine($"SHA256: {checksum}");
            Console.WriteLine();

            // Show preview (first 50 lines for text files)
            if (IsTextFile(file))
            {
                Console.WriteLine("Preview:");
                var lines = File.ReadLines(file).Take(50).ToArray();
                for (int i = 0; i < lines.Length; i++)
                {
                    Console.WriteLine($"    {i + 1, 3}  {lines[i]}");
                }
                if (File.ReadLines(file).Count() > 50)
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
                approved.Add(file);
            }
        }

        if (approved.Count == 0)
        {
            Console.WriteLine("\nNo widgets added.");
            return 0;
        }

        // Generate YAML snippets
        Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("Add to config.yaml:");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

        foreach (var file in approved)
        {
            var id = Path.GetFileNameWithoutExtension(file);
            var checksum = ScriptValidator.CalculateChecksum(file);
            var filename = Path.GetFileName(file);

            Console.WriteLine($"  {id}:");
            Console.WriteLine($"    path: {filename}");
            Console.WriteLine($"    sha256: {checksum}");
            Console.WriteLine($"    refresh: 5");
            Console.WriteLine();
        }

        Console.WriteLine("Don't forget to add widget IDs to your layout!");

        return 0;
    }

    private static async Task<int> ComputeChecksumsAsync(string? configPath)
    {
        configPath ??= ConfigManager.GetDefaultConfigPath();

        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Config file not found: {configPath}");
            return 1;
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
            return 1;
        }

        Console.WriteLine("Widget Checksums:");
        Console.WriteLine("─────────────────────────────────────");

        foreach (var (id, widget) in config.Widgets)
        {
            var resolved = WidgetPaths.ResolveWidgetPath(widget.Path);
            if (resolved != null && File.Exists(resolved))
            {
                var checksum = ScriptValidator.CalculateChecksum(resolved);
                Console.WriteLine($"{id, -15} {checksum}");
                Console.WriteLine($"                {resolved}");
            }
            else
            {
                Console.WriteLine($"{id, -15} [NOT FOUND]");
            }
        }

        return 0;
    }

    private static bool IsExecutable(string path)
    {
#if WINDOWS
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".exe" or ".cmd" or ".bat" or ".sh";
#else
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || (file.Attributes & FileAttributes.Directory) != 0)
                return false;

            // Check if file has execute permission
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return (file.UnixFileMode & UnixFileMode.UserExecute) != 0;
            }

            return true;
        }
        catch
        {
            return false;
        }
#endif
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
        Console.WriteLine("  --discover                       Find and add new custom widgets");
        Console.WriteLine(
            "  --compute-checksums              Print checksums for configured widgets"
        );
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine(
            "  serverhub                                    Use default config (~/.config/serverhub/config.yaml)"
        );
        Console.WriteLine("  serverhub myconfig.yaml                      Use custom config file");
        Console.WriteLine(
            "  serverhub --widgets-path ./dev-widgets       Load widgets from ./dev-widgets"
        );
        Console.WriteLine("  serverhub --widgets-path ~/widgets myconf.yaml");
        Console.WriteLine(
            "  serverhub --discover                         Discover new widgets in ~/.config/serverhub/widgets/"
        );
        Console.WriteLine(
            "  serverhub --compute-checksums                Show checksums for all configured widgets"
        );
        Console.WriteLine();
        Console.WriteLine("Widget Search Paths (in priority order):");
        Console.WriteLine("  1. Custom path (if --widgets-path specified)");
        Console.WriteLine("  2. ~/.config/serverhub/widgets/              User custom widgets");
        Console.WriteLine("  3. ~/.local/share/serverhub/widgets/         Bundled widgets");
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
        public bool ComputeChecksums { get; set; }
    }
}
