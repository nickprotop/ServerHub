// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Config;
using ServerHub.Models;
using ServerHub.Services;
using ServerHub.Utils;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using Spectre.Console;

namespace ServerHub.UI;

/// <summary>
/// Represents a widget item in the configuration dialog with its status.
/// </summary>
public enum WidgetStatus
{
    /// <summary>Widget is in config and script exists</summary>
    Configured,
    /// <summary>Script exists but not in config</summary>
    Available,
    /// <summary>In config but script not found</summary>
    Missing
}

/// <summary>
/// Represents a widget entry in the dialog's list.
/// </summary>
public class WidgetListEntry
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string? FullPath { get; set; }
    public WidgetStatus Status { get; set; }
    public WidgetConfig? Config { get; set; }
    public bool IsGlobalSettings { get; set; }
}

/// <summary>
/// Modal dialog for configuring widgets - add, remove, edit, and reorder.
/// Replaces manual YAML editing and the CLI --discover command.
/// </summary>
public static class WidgetConfigDialog
{
    private static bool _isDirty = false;
    private static ServerHubConfig? _workingConfig;
    private static string? _configPath;
    private static List<WidgetListEntry> _allEntries = new();
    private static WidgetListEntry? _selectedEntry;

    // UI Controls references
    private static ListControl? _widgetList;
    private static MarkupControl? _detailHeader;
    private static ScrollablePanelControl? _detailPanel;
    private static Window? _dialogWindow;
    private static ConsoleWindowSystem? _windowSystem;

    // Detail panel controls
    private static PromptControl? _refreshInput;
    private static CheckboxControl? _pinnedCheckbox;
    private static CheckboxControl? _fullRowCheckbox;
    private static DropdownControl? _columnSpanDropdown;
    private static PromptControl? _maxLinesInput;
    private static PromptControl? _maxHeightInput;
    private static PromptControl? _minHeightInput;
    private static DropdownControl? _priorityDropdown;
    private static DropdownControl? _locationDropdown;

    // Global settings controls
    private static PromptControl? _defaultRefreshInput;
    private static PromptControl? _globalMaxLinesInput;
    private static CheckboxControl? _showTruncationCheckbox;
    private static PromptControl? _breakpointSingleInput;
    private static PromptControl? _breakpointDoubleInput;
    private static PromptControl? _breakpointTripleInput;
    private static PromptControl? _breakpointQuadInput;

    // Buttons
    private static ButtonControl? _upButton;
    private static ButtonControl? _downButton;
    private static ButtonControl? _addRemoveButton;

    /// <summary>
    /// Shows the widget configuration dialog.
    /// </summary>
    /// <param name="windowSystem">Console window system</param>
    /// <param name="configPath">Path to the config file</param>
    /// <param name="currentConfig">Current loaded configuration</param>
    /// <param name="onConfigChanged">Callback when config is saved</param>
    public static void Show(
        ConsoleWindowSystem windowSystem,
        string configPath,
        ServerHubConfig currentConfig,
        Action? onConfigChanged = null)
    {
        _windowSystem = windowSystem;
        _configPath = configPath;
        _isDirty = false;

        // Create a working copy of the config using deep cloning
        _workingConfig = DeepCloner.Clone(currentConfig);

        // Discover all widgets
        _allEntries = DiscoverWidgets(_workingConfig);
        _selectedEntry = null;

        // Calculate modal size: 90% of screen
        int screenWidth = Console.WindowWidth;
        int screenHeight = Console.WindowHeight;
        int modalWidth = Math.Min((int)(screenWidth * 0.9), 120);
        int modalHeight = Math.Min((int)(screenHeight * 0.9), 35);

        // Create modal dialog
        _dialogWindow = new WindowBuilder(windowSystem)
            .WithTitle("Configure Widgets")
            .WithSize(modalWidth, modalHeight)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Grey35)
            .Resizable(false)
            .Movable(false)
            .Minimizable(false)
            .Maximizable(false)
            .WithColors(Color.Grey15, Color.Grey93)
            .Build();

        // Build the UI
        BuildUI(_dialogWindow, modalWidth, modalHeight, onConfigChanged);

        // Handle keyboard shortcuts
        _dialogWindow.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                HandleClose(onConfigChanged);
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.F2)
            {
                // F2 to save
                SaveConfig(onConfigChanged);
                e.Handled = true;
            }
        };

        // Show modal
        windowSystem.AddWindow(_dialogWindow);
        windowSystem.SetActiveWindow(_dialogWindow);
        _widgetList?.SetFocus(true, FocusReason.Programmatic);
    }

    private static void BuildUI(Window dialog, int width, int height, Action? onConfigChanged)
    {
        // Main horizontal grid: left (list) and right (details)
        var mainGrid = Controls.HorizontalGrid()
            .WithName("main_grid")
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .Build();

        // LEFT COLUMN - Widget List (35% width)
        var listColumn = new ColumnContainer(mainGrid)
        {
            Width = (int)(width * 0.35),
            VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Fill
        };

        BuildWidgetList(listColumn);
        mainGrid.AddColumn(listColumn);

        // MIDDLE - Vertical separator
        var separatorColumn = new ColumnContainer(mainGrid)
        {
            Width = 1,
            VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Fill
        };
        var separator = new SeparatorControl
        {
            ForegroundColor = Color.Grey35,
            VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Fill
        };
        separatorColumn.AddContent(separator);
        mainGrid.AddColumn(separatorColumn);

        // RIGHT COLUMN - Details Panel
        var detailColumn = new ColumnContainer(mainGrid)
        {
            VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Fill
        };

        BuildDetailPanel(detailColumn);
        mainGrid.AddColumn(detailColumn);

        dialog.AddControl(mainGrid);

        // Footer separator
        dialog.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .StickyBottom()
            .Build());

        // Footer with buttons
        var saveButton = Controls.Button(" Save ")
            .OnClick((s, e) => SaveConfig(onConfigChanged))
            .Build();

        var cancelButton = Controls.Button(" Cancel ")
            .WithMargin(2, 0, 0, 0)
            .OnClick((s, e) => HandleClose(onConfigChanged))
            .Build();

        var footerGrid = HorizontalGridControl.ButtonRow(saveButton, cancelButton);
        footerGrid.HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment.Center;
        footerGrid.StickyPosition = StickyPosition.Bottom;
        dialog.AddControl(footerGrid);

        // Footer instructions
        dialog.AddControl(Controls.Markup()
            .AddLine("[grey70]Tab: Switch panels | ↑↓: Navigate | F2: Save | Esc: Close[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .StickyBottom()
            .Build());
    }

    private static void BuildWidgetList(ColumnContainer container)
    {
        _widgetList = Controls.List()
            .WithName("widget_list")
            .WithTitle("")
            .SimpleMode()
            .WithColors(Color.Grey19, Color.Grey93)
            .WithFocusedColors(Color.Grey19, Color.Grey93)
            .WithHighlightColors(Color.Grey35, Color.White)
            .WithMargin(1, 1, 1, 0)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .Build();

        PopulateWidgetList();

        _widgetList.SelectedIndexChanged += (s, idx) =>
        {
            if (idx >= 0 && idx < _widgetList.Items.Count)
            {
                var item = _widgetList.Items[idx];
                if (item.Tag is WidgetListEntry entry)
                {
                    _selectedEntry = entry;
                    UpdateDetailPanel();
                }
            }
        };

        container.AddContent(_widgetList);
    }

    private static void PopulateWidgetList()
    {
        if (_widgetList == null) return;

        _widgetList.ClearItems();

        // Remove existing global settings entry from _allEntries
        _allEntries.RemoveAll(e => e.IsGlobalSettings);

        // First item: Global Settings
        var globalEntry = new WidgetListEntry { IsGlobalSettings = true, Id = "Global Settings" };
        _allEntries.Insert(0, globalEntry);

        var globalItem = new ListItem("[cyan1]⚙[/] Global Settings") { Tag = globalEntry };
        _widgetList.AddItem(globalItem);

        // Separator
        _widgetList.AddItem(new ListItem("[grey35]───────────────────[/]") { IsEnabled = false });

        // Missing widgets FIRST (in config but script not found)
        var missing = _allEntries.Where(e => !e.IsGlobalSettings && e.Status == WidgetStatus.Missing).ToList();
        if (missing.Any())
        {
            _widgetList.AddItem(new ListItem("[red bold]⚠ MISSING:[/]") { IsEnabled = false });
            foreach (var entry in missing)
            {
                var locationHint = entry.Config?.Location switch
                {
                    WidgetLocation.Bundled => " [grey50](bundled)[/]",
                    WidgetLocation.Custom => " [grey50](custom)[/]",
                    _ => ""
                };
                var item = new ListItem($"[red]✗[/] {entry.Id}{locationHint}") { Tag = entry };
                _widgetList.AddItem(item);
            }
        }

        // Configured widgets
        var configured = _allEntries.Where(e => !e.IsGlobalSettings && e.Status == WidgetStatus.Configured).ToList();
        if (configured.Any())
        {
            _widgetList.AddItem(new ListItem("[green bold]✓ CONFIGURED:[/]") { IsEnabled = false });
            foreach (var entry in configured)
            {
                var locationHint = entry.Config?.Location switch
                {
                    WidgetLocation.Bundled => " [grey50](bundled)[/]",
                    WidgetLocation.Custom => " [grey50](custom)[/]",
                    _ => ""
                };
                var item = new ListItem($"[green]✓[/] {entry.Id}{locationHint}") { Tag = entry };
                _widgetList.AddItem(item);
            }
        }

        // Available widgets (scripts not in config)
        var available = _allEntries.Where(e => !e.IsGlobalSettings && e.Status == WidgetStatus.Available).ToList();
        if (available.Any())
        {
            _widgetList.AddItem(new ListItem("[cyan1 bold]+ AVAILABLE:[/]") { IsEnabled = false });
            foreach (var entry in available)
            {
                // Available widgets already have location in their ID from discovery
                var item = new ListItem($"[cyan1]+[/] {entry.Id}") { Tag = entry };
                _widgetList.AddItem(item);
            }
        }

        // Select first selectable item
        for (int i = 0; i < _widgetList.Items.Count; i++)
        {
            if (_widgetList.Items[i].IsEnabled)
            {
                _widgetList.SelectedIndex = i;
                if (_widgetList.Items[i].Tag is WidgetListEntry entry)
                {
                    _selectedEntry = entry;
                }
                break;
            }
        }
    }

    private static void BuildDetailPanel(ColumnContainer container)
    {
        // Detail header
        _detailHeader = Controls.Markup()
            .WithName("detail_header")
            .AddLine("[cyan1 bold]Widget Details[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 1, 1, 0)
            .Build();
        container.AddContent(_detailHeader);

        // Separator
        container.AddContent(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .Build());

        // Scrollable detail panel
        _detailPanel = Controls.ScrollablePanel()
            .WithName("detail_scroll")
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithScrollbarPosition(ScrollbarPosition.Right)
            .WithMouseWheel(true)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithBackgroundColor(Color.Grey15)
            .Build();

        // Create all the input controls (they'll be shown/hidden based on selection)
        CreateDetailControls();

        container.AddContent(_detailPanel);

        // Action buttons row
        _upButton = Controls.Button(" ▲ Up ")
            .OnClick((s, e) => MoveWidgetUp())
            .Build();

        _downButton = Controls.Button(" ▼ Down ")
            .WithMargin(1, 0, 0, 0)
            .OnClick((s, e) => MoveWidgetDown())
            .Build();

        _addRemoveButton = Controls.Button(" Remove ")
            .WithMargin(2, 0, 0, 0)
            .OnClick((s, e) => HandleAddRemove())
            .Build();

        var buttonGrid = HorizontalGridControl.ButtonRow(_upButton, _downButton, _addRemoveButton);
        buttonGrid.Margin = new Margin(1, 1, 1, 0);
        container.AddContent(buttonGrid);
    }

    private static void CreateDetailControls()
    {
        if (_detailPanel == null) return;

        // Widget-specific settings
        _refreshInput = new PromptControl { Prompt = "Refresh (sec):", InputWidth = 8 };
        _refreshInput.InputChanged += (s, text) => OnSettingChanged();

        _pinnedCheckbox = new CheckboxControl("Pinned to top", false);
        _pinnedCheckbox.CheckedChanged += (s, isChecked) => OnSettingChanged();

        _fullRowCheckbox = new CheckboxControl("Full row width", false);
        _fullRowCheckbox.CheckedChanged += (s, isChecked) => OnSettingChanged();

        _columnSpanDropdown = new DropdownControl("Column Span:");
        _columnSpanDropdown.AddItem("1");
        _columnSpanDropdown.AddItem("2");
        _columnSpanDropdown.AddItem("3");
        _columnSpanDropdown.AddItem("4");
        _columnSpanDropdown.SelectedIndexChanged += (s, idx) => OnSettingChanged();

        _maxLinesInput = new PromptControl { Prompt = "Max Lines:", InputWidth = 8 };
        _maxLinesInput.InputChanged += (s, text) => OnSettingChanged();

        _maxHeightInput = new PromptControl { Prompt = "Max Height:", InputWidth = 8 };
        _maxHeightInput.InputChanged += (s, text) => OnSettingChanged();

        _minHeightInput = new PromptControl { Prompt = "Min Height:", InputWidth = 8 };
        _minHeightInput.InputChanged += (s, text) => OnSettingChanged();

        _priorityDropdown = new DropdownControl("Priority:");
        _priorityDropdown.AddItem("1 - Critical");
        _priorityDropdown.AddItem("2 - Normal");
        _priorityDropdown.AddItem("3 - Low");
        _priorityDropdown.SelectedIndexChanged += (s, idx) => OnSettingChanged();

        _locationDropdown = new DropdownControl("Location:");
        _locationDropdown.AddItem("Auto (priority order)");   // Index 0 = Auto
        _locationDropdown.AddItem("Custom widgets");          // Index 1 = Custom
        _locationDropdown.AddItem("Bundled widgets");         // Index 2 = Bundled
        _locationDropdown.SelectedIndexChanged += (s, idx) => OnSettingChanged();

        // Global settings
        _defaultRefreshInput = new PromptControl { Prompt = "Default Refresh (sec):", InputWidth = 8 };
        _defaultRefreshInput.InputChanged += (s, text) => OnSettingChanged();

        _globalMaxLinesInput = new PromptControl { Prompt = "Max Lines per Widget:", InputWidth = 8 };
        _globalMaxLinesInput.InputChanged += (s, text) => OnSettingChanged();

        _showTruncationCheckbox = new CheckboxControl("Show truncation indicator", true);
        _showTruncationCheckbox.CheckedChanged += (s, isChecked) => OnSettingChanged();

        _breakpointSingleInput = new PromptControl { Prompt = "1 Column (chars):", InputWidth = 8 };
        _breakpointSingleInput.InputChanged += (s, text) => OnSettingChanged();

        _breakpointDoubleInput = new PromptControl { Prompt = "2 Columns (chars):", InputWidth = 8 };
        _breakpointDoubleInput.InputChanged += (s, text) => OnSettingChanged();

        _breakpointTripleInput = new PromptControl { Prompt = "3 Columns (chars):", InputWidth = 8 };
        _breakpointTripleInput.InputChanged += (s, text) => OnSettingChanged();

        _breakpointQuadInput = new PromptControl { Prompt = "4 Columns (chars):", InputWidth = 8 };
        _breakpointQuadInput.InputChanged += (s, text) => OnSettingChanged();
    }

    private static void ClearDetailPanel()
    {
        if (_detailPanel == null) return;

        // Remove all children from the scrollable panel
        foreach (var child in _detailPanel.Children.ToList())
        {
            _detailPanel.RemoveControl(child);
        }
    }

    private static void UpdateDetailPanel()
    {
        if (_detailPanel == null || _selectedEntry == null || _workingConfig == null) return;

        // Clear existing controls
        ClearDetailPanel();

        if (_selectedEntry.IsGlobalSettings)
        {
            ShowGlobalSettings();
        }
        else if (_selectedEntry.Status == WidgetStatus.Configured)
        {
            ShowConfiguredWidgetSettings();
        }
        else if (_selectedEntry.Status == WidgetStatus.Available)
        {
            ShowAvailableWidgetPreview();
        }
        else if (_selectedEntry.Status == WidgetStatus.Missing)
        {
            ShowMissingWidgetInfo();
        }

        UpdateButtons();
        UpdateTitle();
    }

    private static void ShowGlobalSettings()
    {
        if (_detailPanel == null || _workingConfig == null) return;

        var header = Controls.Markup()
            .AddLine("[cyan1 bold]Global Settings[/]")
            .AddLine("")
            .WithMargin(1, 0, 1, 0)
            .Build();
        _detailPanel.AddControl(header);

        // Default refresh
        _defaultRefreshInput!.Input = _workingConfig.DefaultRefresh.ToString();
        _defaultRefreshInput.Margin = new Margin(1, 0, 1, 0);
        _detailPanel.AddControl(_defaultRefreshInput);

        // Max lines per widget
        _globalMaxLinesInput!.Input = _workingConfig.MaxLinesPerWidget.ToString();
        _globalMaxLinesInput.Margin = new Margin(1, 1, 1, 0);
        _detailPanel.AddControl(_globalMaxLinesInput);

        // Show truncation indicator
        _showTruncationCheckbox!.Checked = _workingConfig.ShowTruncationIndicator;
        _showTruncationCheckbox.Margin = new Margin(1, 1, 1, 0);
        _detailPanel.AddControl(_showTruncationCheckbox);

        // Breakpoints section
        var breakpointHeader = Controls.Markup()
            .AddLine("")
            .AddLine("[grey70]Responsive Breakpoints (columns):[/]")
            .WithMargin(1, 1, 1, 0)
            .Build();
        _detailPanel.AddControl(breakpointHeader);

        var breakpoints = _workingConfig.Breakpoints ?? new BreakpointConfig();

        _breakpointSingleInput!.Input = breakpoints.Single.ToString();
        _breakpointSingleInput.Margin = new Margin(1, 0, 1, 0);
        _detailPanel.AddControl(_breakpointSingleInput);

        _breakpointDoubleInput!.Input = breakpoints.Double.ToString();
        _breakpointDoubleInput.Margin = new Margin(1, 0, 1, 0);
        _detailPanel.AddControl(_breakpointDoubleInput);

        _breakpointTripleInput!.Input = breakpoints.Triple.ToString();
        _breakpointTripleInput.Margin = new Margin(1, 0, 1, 0);
        _detailPanel.AddControl(_breakpointTripleInput);

        _breakpointQuadInput!.Input = breakpoints.Quad.ToString();
        _breakpointQuadInput.Margin = new Margin(1, 0, 1, 0);
        _detailPanel.AddControl(_breakpointQuadInput);
    }

    private static void ShowConfiguredWidgetSettings()
    {
        if (_detailPanel == null || _selectedEntry == null || _workingConfig == null) return;

        var config = _selectedEntry.Config;
        if (config == null) return;

        // Determine widget type based on location setting (not resolved path)
        bool isBundled = config.Location == WidgetLocation.Bundled;

        // Build header with status
        var headerBuilder = Controls.Markup()
            .AddLine($"[cyan1 bold]{_selectedEntry.Id}[/]")
            .AddLine($"[grey70]Path: {config.Path}[/]");

        // Show location
        var locationText = config.Location switch
        {
            WidgetLocation.Bundled => "bundled",
            WidgetLocation.Custom => "custom",
            _ => "auto"
        };
        headerBuilder.AddLine($"[grey70]Location: {locationText}[/]");

        // Get checksums
        string? expectedChecksum = null;
        string? actualChecksum = null;
        bool checksumMismatch = false;

        if (isBundled)
        {
            // Bundled widgets use hardcoded checksums
            var filename = Path.GetFileName(_selectedEntry.FullPath ?? "");
            BundledWidgets.Checksums.TryGetValue(filename, out expectedChecksum);
        }
        else
        {
            // Custom widgets use config checksum
            expectedChecksum = config.Sha256;
        }

        if (_selectedEntry.FullPath != null && File.Exists(_selectedEntry.FullPath))
        {
            try
            {
                actualChecksum = ScriptValidator.CalculateChecksum(_selectedEntry.FullPath);
                if (!string.IsNullOrEmpty(expectedChecksum))
                {
                    checksumMismatch = !string.Equals(actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // Ignore checksum calculation errors
            }
        }

        // Add status line
        if (isBundled)
        {
            if (checksumMismatch)
            {
                headerBuilder.AddLine($"[red bold]Status: BUNDLED - TAMPERED![/]");
            }
            else
            {
                headerBuilder.AddLine($"[green]Status: Bundled (verified)[/]");
            }
        }
        else
        {
            bool checksumMissing = string.IsNullOrEmpty(expectedChecksum);
            if (checksumMismatch)
            {
                headerBuilder.AddLine($"[yellow]Status: Configured (checksum outdated)[/]");
            }
            else if (checksumMissing)
            {
                headerBuilder.AddLine($"[yellow]Status: Configured (no checksum)[/]");
            }
            else
            {
                headerBuilder.AddLine($"[green]Status: Configured[/]");
            }
        }

        headerBuilder.AddLine("");
        var header = headerBuilder.WithMargin(1, 0, 1, 0).Build();
        _detailPanel.AddControl(header);

        // Show checksum information based on widget type and status
        if (isBundled)
        {
            if (checksumMismatch)
            {
                // Bundled widget tampered - CRITICAL ERROR
                var errorBuilder = Controls.Markup()
                    .WithMargin(1, 0, 1, 0)
                    .WithBackgroundColor(Color.Maroon);

                errorBuilder.AddLine("[white bold]⛔ SECURITY ALERT: Bundled widget has been modified![/]");
                errorBuilder.AddLine("");
                errorBuilder.AddLine($"[white]Expected: {expectedChecksum}[/]");
                errorBuilder.AddLine($"[white]Actual:   {actualChecksum}[/]");
                errorBuilder.AddLine("");
                errorBuilder.AddLine("[yellow]This widget will NOT run until reinstalled.[/]");
                errorBuilder.AddLine("[grey70]Reinstall ServerHub to restore bundled widgets.[/]");

                _detailPanel.AddControl(errorBuilder.Build());

                _detailPanel.AddControl(Controls.RuleBuilder()
                    .WithColor(Color.Grey23)
                    .WithMargin(1, 1, 1, 0)
                    .Build());
            }
            else
            {
                // Bundled widget verified - show hardcoded checksum
                var infoBuilder = Controls.Markup()
                    .AddLine($"[grey70]Hardcoded SHA256: {expectedChecksum}[/]")
                    .AddLine($"[cyan1]Source: Verified at build time[/]")
                    .WithMargin(1, 0, 1, 0)
                    .Build();
                _detailPanel.AddControl(infoBuilder);
            }
        }
        else
        {
            // Custom widget - show checksum status
            bool checksumMissing = string.IsNullOrEmpty(expectedChecksum);

            if (checksumMismatch || checksumMissing)
            {
                // Custom widget with checksum issue
                var warningBuilder = Controls.Markup()
                    .WithMargin(1, 0, 1, 0)
                    .WithBackgroundColor(Color.Grey19);

                if (checksumMismatch)
                {
                    warningBuilder.AddLine("[yellow]⚠ Script has been modified since last trust![/]");
                    warningBuilder.AddLine($"[grey70]Stored:  {expectedChecksum}[/]");
                    warningBuilder.AddLine($"[grey70]Current: {actualChecksum}[/]");
                    warningBuilder.AddLine("");
                    warningBuilder.AddLine("[grey70]Review the script changes before updating.[/]");
                }
                else
                {
                    warningBuilder.AddLine("[yellow]⚠ No checksum stored for this widget![/]");
                    if (actualChecksum != null)
                    {
                        warningBuilder.AddLine($"[grey70]Current: {actualChecksum}[/]");
                    }
                    warningBuilder.AddLine("");
                    warningBuilder.AddLine("[grey70]Widget will fail validation without checksum.[/]");
                }

                _detailPanel.AddControl(warningBuilder.Build());

                // Update checksum button (only for custom widgets with actual file)
                if (actualChecksum != null)
                {
                    var updateChecksumButton = Controls.Button(" Update Checksum ")
                        .WithMargin(1, 1, 1, 0)
                        .OnClick((s, e) =>
                        {
                            if (actualChecksum != null && config != null)
                            {
                                config.Sha256 = actualChecksum;
                                _isDirty = true;
                                UpdateTitle();
                                // Refresh to show updated status
                                UpdateDetailPanel();
                            }
                        })
                        .Build();
                    _detailPanel.AddControl(updateChecksumButton);
                }

                _detailPanel.AddControl(Controls.RuleBuilder()
                    .WithColor(Color.Grey23)
                    .WithMargin(1, 1, 1, 0)
                    .Build());
            }
            else
            {
                // Custom widget with valid checksum
                var checksumInfo = Controls.Markup()
                    .AddLine($"[grey70]SHA256: {config.Sha256}[/]")
                    .AddLine($"[green]Status: Verified[/]")
                    .WithMargin(1, 0, 1, 0)
                    .Build();
                _detailPanel.AddControl(checksumInfo);
            }
        }

        ShowWidgetSettingsControls(config);
    }

    private static void ShowWidgetSettingsControls(WidgetConfig config)
    {
        var settingsHeader = Controls.Markup()
            .AddLine("[grey70]Settings:[/]")
            .WithMargin(1, 1, 1, 0)
            .Build();
        _detailPanel!.AddControl(settingsHeader);

        // Refresh
        _refreshInput!.Input = config.Refresh.ToString();
        _refreshInput.Margin = new Margin(1, 0, 1, 0);
        _detailPanel.AddControl(_refreshInput);

        // Pinned
        _pinnedCheckbox!.Checked = config.Pinned;
        _pinnedCheckbox.Margin = new Margin(1, 1, 1, 0);
        _detailPanel.AddControl(_pinnedCheckbox);

        // Location
        _locationDropdown!.SelectedIndex = config.Location switch
        {
            WidgetLocation.Custom => 1,
            WidgetLocation.Bundled => 2,
            _ => 0  // Auto or null
        };
        _locationDropdown.Margin = new Margin(1, 1, 1, 0);
        _detailPanel.AddControl(_locationDropdown);

        // Full row
        _fullRowCheckbox!.Checked = config.FullRow;
        _fullRowCheckbox.Margin = new Margin(1, 0, 1, 0);
        _detailPanel.AddControl(_fullRowCheckbox);

        // Column span
        int columnSpanIdx = (config.ColumnSpan ?? 1) - 1;
        _columnSpanDropdown!.SelectedIndex = Math.Clamp(columnSpanIdx, 0, 3);
        _columnSpanDropdown.Margin = new Margin(1, 1, 1, 0);
        _detailPanel.AddControl(_columnSpanDropdown);

        // Max lines
        _maxLinesInput!.Input = config.MaxLines?.ToString() ?? "";
        _maxLinesInput.Margin = new Margin(1, 1, 1, 0);
        _detailPanel.AddControl(_maxLinesInput);

        // Max height
        _maxHeightInput!.Input = config.MaxHeight?.ToString() ?? "";
        _maxHeightInput.Margin = new Margin(1, 1, 1, 0);
        _detailPanel.AddControl(_maxHeightInput);

        // Min height
        _minHeightInput!.Input = config.MinHeight?.ToString() ?? "";
        _minHeightInput.Margin = new Margin(1, 1, 1, 0);
        _detailPanel.AddControl(_minHeightInput);

        // Priority
        _priorityDropdown!.SelectedIndex = Math.Clamp(config.Priority - 1, 0, 2);
        _priorityDropdown.Margin = new Margin(1, 1, 1, 0);
        _detailPanel.AddControl(_priorityDropdown);
    }

    private static void ShowAvailableWidgetPreview()
    {
        if (_detailPanel == null || _selectedEntry == null) return;

        var header = Controls.Markup()
            .AddLine($"[cyan1 bold]{_selectedEntry.Id}[/]")
            .AddLine($"[grey70]Path: {_selectedEntry.Path}[/]")
            .AddLine($"[yellow]Status: Available (not configured)[/]")
            .AddLine("")
            .WithMargin(1, 0, 1, 0)
            .Build();
        _detailPanel.AddControl(header);

        // Show script preview if possible
        if (_selectedEntry.FullPath != null && File.Exists(_selectedEntry.FullPath))
        {
            try
            {
                var checksum = ScriptValidator.CalculateChecksum(_selectedEntry.FullPath);
                var checksumInfo = Controls.Markup()
                    .AddLine($"[grey70]SHA256: {checksum}[/]")
                    .AddLine("")
                    .WithMargin(1, 0, 1, 0)
                    .Build();
                _detailPanel.AddControl(checksumInfo);

                var previewHeader = Controls.Markup()
                    .AddLine("[grey70]Script Preview (first 20 lines):[/]")
                    .WithMargin(1, 0, 1, 0)
                    .Build();
                _detailPanel.AddControl(previewHeader);

                var lines = File.ReadLines(_selectedEntry.FullPath).Take(20);
                var previewBuilder = Controls.Markup()
                    .WithMargin(1, 0, 1, 0)
                    .WithBackgroundColor(Color.Grey19);

                int lineNum = 1;
                foreach (var line in lines)
                {
                    // Escape any Spectre markup in the script content
                    var escapedLine = Markup.Escape(line);
                    previewBuilder.AddLine($"[grey50]{lineNum,3}[/] {escapedLine}");
                    lineNum++;
                }

                _detailPanel.AddControl(previewBuilder.Build());
            }
            catch (Exception ex)
            {
                var error = Controls.Markup()
                    .AddLine($"[red]Error reading script: {ex.Message}[/]")
                    .WithMargin(1, 0, 1, 0)
                    .Build();
                _detailPanel.AddControl(error);
            }
        }

        var instructions = Controls.Markup()
            .AddLine("")
            .AddLine("[grey70]Click 'Add to Config' to configure this widget.[/]")
            .WithMargin(1, 1, 1, 0)
            .Build();
        _detailPanel.AddControl(instructions);
    }

    private static void ShowMissingWidgetInfo()
    {
        if (_detailPanel == null || _selectedEntry == null) return;

        var config = _selectedEntry.Config;
        if (config == null) return;

        // Determine widget type based on location setting
        bool isBundled = config.Location == WidgetLocation.Bundled;

        // Build header
        var headerBuilder = Controls.Markup()
            .AddLine($"[cyan1 bold]{_selectedEntry.Id}[/]")
            .AddLine($"[grey70]Path: {config.Path}[/]");

        // Show location
        var locationText = config.Location switch
        {
            WidgetLocation.Bundled => "bundled",
            WidgetLocation.Custom => "custom",
            _ => "auto"
        };
        headerBuilder.AddLine($"[grey70]Location: {locationText}[/]");

        var locationHint = config.Location switch
        {
            WidgetLocation.Bundled => "bundled widgets directory",
            WidgetLocation.Custom => "custom widgets directories",
            _ => "any widget directory"
        };
        headerBuilder.AddLine($"[red bold]Status: MISSING[/]");
        headerBuilder.AddLine("");
        var missingHeader = headerBuilder.WithMargin(1, 0, 1, 0).Build();
        _detailPanel.AddControl(missingHeader);

        // Show error message
        var errorBuilder = Controls.Markup()
            .WithMargin(1, 0, 1, 0)
            .WithBackgroundColor(Color.Maroon);
        errorBuilder.AddLine($"[white bold]⚠ Widget script not found in {locationHint}[/]");
        errorBuilder.AddLine("");
        errorBuilder.AddLine($"[white]Expected: {config.Path}[/]");
        errorBuilder.AddLine("");

        if (isBundled)
        {
            errorBuilder.AddLine("[yellow]This is a bundled widget. Solutions:[/]");
            errorBuilder.AddLine("[grey70]1. Install bundled widgets (run install script)[/]");
            errorBuilder.AddLine("[grey70]2. Change location to 'custom' or 'auto'[/]");
            errorBuilder.AddLine("[grey70]3. Remove this widget from configuration[/]");
        }
        else
        {
            errorBuilder.AddLine("[yellow]Solutions:[/]");
            errorBuilder.AddLine($"[grey70]1. Add {config.Path} to the widget directory[/]");
            errorBuilder.AddLine("[grey70]2. Change location setting[/]");
            errorBuilder.AddLine("[grey70]3. Remove this widget from configuration[/]");
        }

        _detailPanel.AddControl(errorBuilder.Build());
        _detailPanel.AddControl(Controls.RuleBuilder()
            .WithColor(Color.Grey23)
            .WithMargin(1, 1, 1, 0)
            .Build());

        // Show settings even for missing widgets (allow changing location)
        ShowWidgetSettingsControls(config);
    }

    private static void UpdateButtons()
    {
        if (_selectedEntry == null) return;

        bool isConfigured = _selectedEntry.Status == WidgetStatus.Configured;
        bool isAvailable = _selectedEntry.Status == WidgetStatus.Available;
        bool isMissing = _selectedEntry.Status == WidgetStatus.Missing;
        bool isGlobal = _selectedEntry.IsGlobalSettings;

        // Up/Down buttons only for configured widgets
        if (_upButton != null)
        {
            _upButton.IsEnabled = isConfigured;
            _upButton.Visible = isConfigured;
        }
        if (_downButton != null)
        {
            _downButton.IsEnabled = isConfigured;
            _downButton.Visible = isConfigured;
        }

        // Add/Remove button
        if (_addRemoveButton != null)
        {
            if (isGlobal)
            {
                _addRemoveButton.Visible = false;
            }
            else if (isAvailable)
            {
                _addRemoveButton.Text = " Add to Config ";
                _addRemoveButton.Visible = true;
                _addRemoveButton.IsEnabled = true;
            }
            else if (isConfigured || isMissing)
            {
                _addRemoveButton.Text = " Remove ";
                _addRemoveButton.Visible = true;
                _addRemoveButton.IsEnabled = true;
            }
        }
    }

    private static void UpdateTitle()
    {
        if (_dialogWindow == null) return;

        string title = _isDirty ? "Configure Widgets [*]" : "Configure Widgets";
        _dialogWindow.Title = title;
    }

    private static void OnSettingChanged()
    {
        if (_selectedEntry == null || _workingConfig == null) return;

        // Apply changes based on current selection
        if (_selectedEntry.IsGlobalSettings)
        {
            ApplyGlobalSettings();
        }
        else if (_selectedEntry.Status == WidgetStatus.Configured && _selectedEntry.Config != null)
        {
            ApplyWidgetSettings(_selectedEntry.Config);
        }

        _isDirty = true;
        UpdateTitle();
    }

    private static void ApplyGlobalSettings()
    {
        if (_workingConfig == null) return;

        if (int.TryParse(_defaultRefreshInput?.Input, out int defaultRefresh) && defaultRefresh >= 1)
        {
            _workingConfig.DefaultRefresh = defaultRefresh;
        }

        if (int.TryParse(_globalMaxLinesInput?.Input, out int maxLines) && maxLines >= 1)
        {
            _workingConfig.MaxLinesPerWidget = maxLines;
        }

        if (_showTruncationCheckbox != null)
        {
            _workingConfig.ShowTruncationIndicator = _showTruncationCheckbox.Checked;
        }

        // Breakpoints
        _workingConfig.Breakpoints ??= new BreakpointConfig();

        if (int.TryParse(_breakpointSingleInput?.Input, out int single))
            _workingConfig.Breakpoints.Single = single;
        if (int.TryParse(_breakpointDoubleInput?.Input, out int @double))
            _workingConfig.Breakpoints.Double = @double;
        if (int.TryParse(_breakpointTripleInput?.Input, out int triple))
            _workingConfig.Breakpoints.Triple = triple;
        if (int.TryParse(_breakpointQuadInput?.Input, out int quad))
            _workingConfig.Breakpoints.Quad = quad;
    }

    private static void ApplyWidgetSettings(WidgetConfig config)
    {
        if (int.TryParse(_refreshInput?.Input, out int refresh) && refresh >= 1)
        {
            config.Refresh = refresh;
        }

        if (_pinnedCheckbox != null)
            config.Pinned = _pinnedCheckbox.Checked;

        if (_fullRowCheckbox != null)
            config.FullRow = _fullRowCheckbox.Checked;

        if (_columnSpanDropdown != null)
            config.ColumnSpan = _columnSpanDropdown.SelectedIndex + 1;

        config.MaxLines = ParseNullableInt(_maxLinesInput?.Input);
        config.MaxHeight = ParseNullableInt(_maxHeightInput?.Input);
        config.MinHeight = ParseNullableInt(_minHeightInput?.Input);

        if (_priorityDropdown != null)
            config.Priority = _priorityDropdown.SelectedIndex + 1;

        if (_locationDropdown != null)
        {
            config.Location = _locationDropdown.SelectedIndex switch
            {
                1 => WidgetLocation.Custom,
                2 => WidgetLocation.Bundled,
                _ => null  // Auto (omit from YAML)
            };
        }
    }

    private static int? ParseNullableInt(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        return int.TryParse(input, out int value) ? value : null;
    }

    private static void MoveWidgetUp()
    {
        if (_selectedEntry == null || _workingConfig == null || _selectedEntry.Status != WidgetStatus.Configured)
            return;

        var order = _workingConfig.Layout?.Order;
        if (order == null) return;

        int idx = order.IndexOf(_selectedEntry.Id);
        if (idx > 0)
        {
            // Swap with previous
            (order[idx], order[idx - 1]) = (order[idx - 1], order[idx]);
            _isDirty = true;

            // Refresh list and reselect
            RefreshWidgetList(_selectedEntry.Id);
            UpdateTitle();
        }
    }

    private static void MoveWidgetDown()
    {
        if (_selectedEntry == null || _workingConfig == null || _selectedEntry.Status != WidgetStatus.Configured)
            return;

        var order = _workingConfig.Layout?.Order;
        if (order == null) return;

        int idx = order.IndexOf(_selectedEntry.Id);
        if (idx >= 0 && idx < order.Count - 1)
        {
            // Swap with next
            (order[idx], order[idx + 1]) = (order[idx + 1], order[idx]);
            _isDirty = true;

            // Refresh list and reselect
            RefreshWidgetList(_selectedEntry.Id);
            UpdateTitle();
        }
    }

    private static void HandleAddRemove()
    {
        if (_selectedEntry == null || _workingConfig == null) return;

        if (_selectedEntry.Status == WidgetStatus.Available)
        {
            AddWidget();
        }
        else if (_selectedEntry.Status == WidgetStatus.Configured || _selectedEntry.Status == WidgetStatus.Missing)
        {
            RemoveWidget();
        }
    }

    private static void AddWidget()
    {
        if (_selectedEntry == null || _workingConfig == null || _selectedEntry.FullPath == null) return;

        // Calculate checksum
        var checksum = ScriptValidator.CalculateChecksum(_selectedEntry.FullPath);

        // Create widget config
        var widgetConfig = new WidgetConfig
        {
            Path = _selectedEntry.Path,
            Sha256 = checksum,
            Refresh = _workingConfig.DefaultRefresh,
            Priority = 2,
            Pinned = false
        };

        // Add to config
        _workingConfig.Widgets[_selectedEntry.Id] = widgetConfig;

        // Add to layout order
        _workingConfig.Layout ??= new LayoutConfig { Order = new List<string>() };
        _workingConfig.Layout.Order ??= new List<string>();
        if (!_workingConfig.Layout.Order.Contains(_selectedEntry.Id))
        {
            _workingConfig.Layout.Order.Add(_selectedEntry.Id);
        }

        _isDirty = true;
        RefreshWidgetList(_selectedEntry.Id);
        UpdateTitle();
    }

    private static void RemoveWidget()
    {
        if (_selectedEntry == null || _workingConfig == null) return;

        // Remove from widgets
        _workingConfig.Widgets.Remove(_selectedEntry.Id);

        // Remove from layout order
        _workingConfig.Layout?.Order?.Remove(_selectedEntry.Id);

        _isDirty = true;
        RefreshWidgetList(null);
        UpdateTitle();
    }

    private static void RefreshWidgetList(string? selectId)
    {
        // Re-discover widgets
        _allEntries = DiscoverWidgets(_workingConfig!);

        PopulateWidgetList();

        // Try to select the specified ID
        if (selectId != null && _widgetList != null)
        {
            for (int i = 0; i < _widgetList.Items.Count; i++)
            {
                if (_widgetList.Items[i].Tag is WidgetListEntry entry && entry.Id == selectId)
                {
                    _widgetList.SelectedIndex = i;
                    _selectedEntry = entry;
                    break;
                }
            }
        }

        UpdateDetailPanel();
    }

    private static void SaveConfig(Action? onConfigChanged)
    {
        if (_workingConfig == null || _configPath == null) return;

        try
        {
            var configManager = new ConfigManager();
            configManager.SaveConfig(_workingConfig, _configPath);
            _isDirty = false;
            UpdateTitle();

            onConfigChanged?.Invoke();

            _dialogWindow?.Close();
        }
        catch (Exception ex)
        {
            // Show error dialog
            ShowErrorDialog($"Failed to save config: {ex.Message}");
        }
    }

    private static void HandleClose(Action? onConfigChanged)
    {
        if (_isDirty)
        {
            ShowUnsavedChangesDialog(onConfigChanged);
        }
        else
        {
            _dialogWindow?.Close();
        }
    }

    private static void ShowUnsavedChangesDialog(Action? onConfigChanged)
    {
        if (_windowSystem == null || _dialogWindow == null) return;

        var confirmDialog = new WindowBuilder(_windowSystem)
            .WithTitle("Unsaved Changes")
            .WithSize(50, 10)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithColors(Color.Grey15, Color.Grey93)
            .HideCloseButton()
            .Build();

        confirmDialog.AddControl(Controls.Markup()
            .AddLine("")
            .AddLine("  You have unsaved changes.")
            .AddLine("  Do you want to save before closing?")
            .AddLine("")
            .Build());

        var saveButton = Controls.Button(" Save & Close ")
            .OnClick((s, e) =>
            {
                _windowSystem.CloseWindow(confirmDialog);
                SaveConfig(onConfigChanged);
            })
            .Build();

        var discardButton = Controls.Button(" Discard ")
            .WithMargin(1, 0, 0, 0)
            .OnClick((s, e) =>
            {
                _windowSystem.CloseWindow(confirmDialog);
                _dialogWindow?.Close();
            })
            .Build();

        var cancelButton = Controls.Button(" Cancel ")
            .WithMargin(1, 0, 0, 0)
            .OnClick((s, e) =>
            {
                _windowSystem.CloseWindow(confirmDialog);
            })
            .Build();

        var buttonGrid = HorizontalGridControl.ButtonRow(saveButton, discardButton, cancelButton);
        buttonGrid.HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment.Center;
        buttonGrid.StickyPosition = StickyPosition.Bottom;
        confirmDialog.AddControl(buttonGrid);

        _windowSystem.AddWindow(confirmDialog);
    }

    private static void ShowErrorDialog(string message)
    {
        if (_windowSystem == null) return;

        var errorDialog = new WindowBuilder(_windowSystem)
            .WithTitle("Error")
            .WithSize(60, 8)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithColors(Color.Grey15, Color.Grey93)
            .Build();

        errorDialog.AddControl(Controls.Markup()
            .AddLine("")
            .AddLine($"  [red]{Markup.Escape(message)}[/]")
            .AddLine("")
            .Build());

        var okButton = Controls.Button("  OK  ")
            .OnClick((s, e) => _windowSystem.CloseWindow(errorDialog))
            .Build();

        var buttonGrid = HorizontalGridControl.ButtonRow(okButton);
        buttonGrid.HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment.Center;
        buttonGrid.StickyPosition = StickyPosition.Bottom;
        errorDialog.AddControl(buttonGrid);

        _windowSystem.AddWindow(errorDialog);
    }

    private static List<WidgetListEntry> DiscoverWidgets(ServerHubConfig config)
    {
        var entries = new List<WidgetListEntry>();

        // Track configured (path, location) pairs to avoid showing them as available
        var configuredPathLocations = new HashSet<(string path, WidgetLocation? location)>(
            new PathLocationComparer()
        );

        // First, add all configured widgets in layout order
        var orderedWidgets = new List<string>();
        if (config.Layout?.Order != null)
        {
            orderedWidgets.AddRange(config.Layout.Order);
        }

        // Add any widgets not in the order list
        foreach (var widgetId in config.Widgets.Keys)
        {
            if (!orderedWidgets.Contains(widgetId))
            {
                orderedWidgets.Add(widgetId);
            }
        }

        // Process configured widgets
        foreach (var widgetId in orderedWidgets)
        {
            if (!config.Widgets.TryGetValue(widgetId, out var widgetConfig))
                continue;

            var fullPath = WidgetPaths.ResolveWidgetPath(widgetConfig.Path, widgetConfig.Location);
            var status = fullPath != null && File.Exists(fullPath)
                ? WidgetStatus.Configured
                : WidgetStatus.Missing;

            entries.Add(new WidgetListEntry
            {
                Id = widgetId,
                Path = widgetConfig.Path,
                FullPath = fullPath,
                Status = status,
                Config = widgetConfig
            });

            // Track this path+location as configured (even if missing)
            configuredPathLocations.Add((widgetConfig.Path, widgetConfig.Location));
        }

        // Discover available widgets from bundled directory
        var bundledPath = WidgetPaths.GetBundledWidgetsDirectory();
        if (Directory.Exists(bundledPath))
        {
            foreach (var file in Directory.GetFiles(bundledPath))
            {
                if (!IsExecutable(file)) continue;

                var fileName = Path.GetFileName(file);
                var fullPath = Path.GetFullPath(file);

                // Skip if this path+bundled combination is already configured
                if (configuredPathLocations.Contains((fileName, WidgetLocation.Bundled)))
                    continue;

                var widgetId = Path.GetFileNameWithoutExtension(file);

                entries.Add(new WidgetListEntry
                {
                    Id = $"{widgetId} (bundled)",
                    Path = fileName,
                    FullPath = fullPath,
                    Status = WidgetStatus.Available,
                    Config = new WidgetConfig
                    {
                        Path = fileName,
                        Location = WidgetLocation.Bundled
                    }
                });
            }
        }

        // Discover available widgets from custom directories
        foreach (var searchPath in WidgetPaths.GetSearchPaths())
        {
            if (!Directory.Exists(searchPath)) continue;

            // Skip bundled directory (already processed above)
            if (searchPath == bundledPath) continue;

            foreach (var file in Directory.GetFiles(searchPath))
            {
                if (!IsExecutable(file)) continue;

                var fileName = Path.GetFileName(file);
                var fullPath = Path.GetFullPath(file);

                // Skip if this path+custom combination is already configured
                if (configuredPathLocations.Contains((fileName, WidgetLocation.Custom)))
                    continue;

                var widgetId = Path.GetFileNameWithoutExtension(file);

                entries.Add(new WidgetListEntry
                {
                    Id = $"{widgetId} (custom)",
                    Path = fileName,
                    FullPath = fullPath,
                    Status = WidgetStatus.Available,
                    Config = new WidgetConfig
                    {
                        Path = fileName,
                        Location = WidgetLocation.Custom
                    }
                });
            }
        }

        return entries;
    }

    // Custom comparer for (path, location) tuples
    private class PathLocationComparer : IEqualityComparer<(string path, WidgetLocation? location)>
    {
        public bool Equals((string path, WidgetLocation? location) x, (string path, WidgetLocation? location) y)
        {
            return string.Equals(x.path, y.path, StringComparison.OrdinalIgnoreCase)
                && x.location == y.location;
        }

        public int GetHashCode((string path, WidgetLocation? location) obj)
        {
            return HashCode.Combine(
                obj.path.ToLowerInvariant(),
                obj.location
            );
        }
    }

    private static bool IsExecutable(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || (file.Attributes & FileAttributes.Directory) != 0)
                return false;

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return (file.UnixFileMode & UnixFileMode.UserExecute) != 0;
            }

            // Windows: check extension
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".sh" or ".bash" or ".exe" or ".cmd" or ".bat";
        }
        catch
        {
            return false;
        }
    }

}
