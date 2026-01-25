// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Config;
using ServerHub.Exceptions;
using ServerHub.Models;
using ServerHub.Services;
using ServerHub.Utils;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using Spectre.Console;

namespace ServerHub.UI;

/// <summary>
/// Represents a widget entry in the dialog's list.
/// </summary>
public class WidgetListEntry
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string? FullPath { get; set; }
    public WidgetConfigStatus Status { get; set; }
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
    private static bool _isPopulating = false; // Flag to prevent applying settings during initialization
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
    private static PromptControl? _expandedRefreshInput;
    private static CheckboxControl? _pinnedCheckbox;
    private static CheckboxControl? _enabledCheckbox;
    private static DropdownControl? _columnSpanDropdown;
    private static PromptControl? _maxLinesInput;
    private static DropdownControl? _locationDropdown;

    // Global settings controls
    private static PromptControl? _defaultRefreshInput;
    private static PromptControl? _globalMaxLinesInput;
    private static CheckboxControl? _showTruncationCheckbox;
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
        Action? onConfigChanged = null
    )
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
            .Resizable(true)
            .Movable(true)
            .Minimizable(false)
            .Maximizable(true)
            .WithColors(Color.Grey15, Color.Grey93)
            .Build();

        // Build the UI
        BuildUI(_dialogWindow, modalWidth, modalHeight, onConfigChanged);

        // Update detail panel to show initial selection
        UpdateDetailPanel();

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
        var mainGrid = Controls
            .HorizontalGrid()
            .WithName("main_grid")
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .Build();

        // LEFT COLUMN - Widget List (35% width)
        var listColumn = new ColumnContainer(mainGrid)
        {
            Width = (int)(width * 0.35),
            VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Fill,
        };

        BuildWidgetList(listColumn);
        mainGrid.AddColumn(listColumn);

        // MIDDLE - Vertical separator
        var separatorColumn = new ColumnContainer(mainGrid)
        {
            Width = 1,
            VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Fill,
        };
        var separator = new SeparatorControl
        {
            ForegroundColor = Color.Grey35,
            VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Fill,
        };
        separatorColumn.AddContent(separator);
        mainGrid.AddColumn(separatorColumn);

        // RIGHT COLUMN - Details Panel
        var detailColumn = new ColumnContainer(mainGrid)
        {
            VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment.Fill,
        };

        BuildDetailPanel(detailColumn);
        mainGrid.AddColumn(detailColumn);

        dialog.AddControl(mainGrid);

        // Footer separator
        dialog.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23).StickyBottom().Build());

        // Footer with buttons
        var saveButton = Controls
            .Button(" Save ")
            .OnClick((s, e) => SaveConfig(onConfigChanged))
            .Build();

        var cancelButton = Controls
            .Button(" Cancel ")
            .WithMargin(2, 0, 0, 0)
            .OnClick((s, e) => HandleClose(onConfigChanged))
            .Build();

        var footerGrid = HorizontalGridControl.ButtonRow(saveButton, cancelButton);
        footerGrid.HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment.Center;
        footerGrid.StickyPosition = StickyPosition.Bottom;
        dialog.AddControl(footerGrid);

        // Footer instructions
        dialog.AddControl(
            Controls
                .Markup()
                .AddLine("[grey70]Tab: Switch panels | ↑↓: Navigate | F2: Save | Esc: Close[/]")
                .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
                .StickyBottom()
                .Build()
        );
    }

    private static void BuildWidgetList(ColumnContainer container)
    {
        _widgetList = Controls
            .List()
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
        if (_widgetList == null)
            return;

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
        var missing = _allEntries
            .Where(e => !e.IsGlobalSettings && e.Status == WidgetConfigStatus.Missing)
            .ToList();
        if (missing.Any())
        {
            _widgetList.AddItem(new ListItem("[red bold]⚠ MISSING:[/]") { IsEnabled = false });
            foreach (var entry in missing)
            {
                var locationHint = entry.Config?.Location switch
                {
                    WidgetLocation.Bundled => " [grey50](bundled)[/]",
                    WidgetLocation.Custom => " [grey50](custom)[/]",
                    _ => "",
                };
                var item = new ListItem($"[red]✗[/] {entry.Id}{locationHint}") { Tag = entry };
                _widgetList.AddItem(item);
            }
        }

        // Configured widgets (enabled only)
        var configured = _allEntries
            .Where(e =>
                !e.IsGlobalSettings
                && e.Status == WidgetConfigStatus.Configured
                && e.Config?.Enabled == true
            )
            .ToList();
        if (configured.Any())
        {
            _widgetList.AddItem(new ListItem("[green bold]✓ CONFIGURED:[/]") { IsEnabled = false });
            foreach (var entry in configured)
            {
                var locationHint = entry.Config?.Location switch
                {
                    WidgetLocation.Bundled => " [grey50](bundled)[/]",
                    WidgetLocation.Custom => " [grey50](custom)[/]",
                    _ => "",
                };
                var item = new ListItem($"[green]✓[/] {entry.Id}{locationHint}") { Tag = entry };
                _widgetList.AddItem(item);
            }
        }

        // Disabled widgets (configured but disabled)
        var disabled = _allEntries
            .Where(e =>
                !e.IsGlobalSettings
                && e.Status == WidgetConfigStatus.Configured
                && e.Config?.Enabled == false
            )
            .ToList();
        if (disabled.Any())
        {
            _widgetList.AddItem(new ListItem("[grey70 bold]○ DISABLED:[/]") { IsEnabled = false });
            foreach (var entry in disabled)
            {
                var locationHint = entry.Config?.Location switch
                {
                    WidgetLocation.Bundled => " [grey50](bundled)[/]",
                    WidgetLocation.Custom => " [grey50](custom)[/]",
                    _ => "",
                };
                var item = new ListItem($"[grey70]○[/] {entry.Id}{locationHint}") { Tag = entry };
                _widgetList.AddItem(item);
            }
        }

        // Available widgets (scripts not in config)
        var available = _allEntries
            .Where(e => !e.IsGlobalSettings && e.Status == WidgetConfigStatus.Available)
            .ToList();
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
        _detailHeader = Controls
            .Markup()
            .WithName("detail_header")
            .AddLine("[cyan1 bold]Widget Details[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 1, 1, 0)
            .Build();
        container.AddContent(_detailHeader);

        // Separator
        container.AddContent(Controls.RuleBuilder().WithColor(Color.Grey23).Build());

        // Scrollable detail panel
        _detailPanel = Controls
            .ScrollablePanel()
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
        _upButton = Controls.Button(" ▲ Up ").OnClick((s, e) => MoveWidgetUp()).Build();

        _downButton = Controls
            .Button(" ▼ Down ")
            .WithMargin(1, 0, 0, 0)
            .OnClick((s, e) => MoveWidgetDown())
            .Build();

        _addRemoveButton = Controls
            .Button(" Remove ")
            .WithMargin(2, 0, 0, 0)
            .OnClick((s, e) => HandleAddRemove())
            .Build();

        var buttonGrid = HorizontalGridControl.ButtonRow(_upButton, _downButton, _addRemoveButton);
        buttonGrid.Margin = new Margin(1, 1, 1, 0);
        container.AddContent(buttonGrid);
    }

    private static void CreateDetailControls()
    {
        if (_detailPanel == null)
            return;

        // Widget-specific settings
        _refreshInput = new PromptControl { Prompt = "Refresh (sec):", InputWidth = 8 };
        _refreshInput.InputChanged += (s, text) => OnSettingChanged();

        _expandedRefreshInput = new PromptControl
        {
            Prompt = "Expanded Refresh (sec):",
            InputWidth = 8
        };
        _expandedRefreshInput.InputChanged += (s, text) => OnSettingChanged();

        _pinnedCheckbox = new CheckboxControl("Pinned to top", false);
        _pinnedCheckbox.CheckedChanged += (s, isChecked) => OnSettingChanged();

        _enabledCheckbox = new CheckboxControl("Enabled (visible & refreshing)", true);
        _enabledCheckbox.CheckedChanged += (s, isChecked) => OnSettingChanged();

        _columnSpanDropdown = new DropdownControl("Column Span:");
        _columnSpanDropdown.AddItem("1");
        _columnSpanDropdown.AddItem("2");
        _columnSpanDropdown.AddItem("3");
        _columnSpanDropdown.AddItem("4");
        _columnSpanDropdown.SelectedIndexChanged += (s, idx) => OnSettingChanged();

        _maxLinesInput = new PromptControl { Prompt = "Max Lines:", InputWidth = 8 };
        _maxLinesInput.InputChanged += (s, text) => OnSettingChanged();

        _locationDropdown = new DropdownControl("Location:");
        _locationDropdown.AddItem("Auto (search order)"); // Index 0 = Auto
        _locationDropdown.AddItem("Custom widgets"); // Index 1 = Custom
        _locationDropdown.AddItem("Bundled widgets"); // Index 2 = Bundled
        _locationDropdown.SelectedIndexChanged += (s, idx) => OnSettingChanged();

        // Global settings
        _defaultRefreshInput = new PromptControl
        {
            Prompt = "Default Refresh (sec):",
            InputWidth = 8,
        };
        _defaultRefreshInput.InputChanged += (s, text) => OnSettingChanged();

        _globalMaxLinesInput = new PromptControl
        {
            Prompt = "Max Lines per Widget:",
            InputWidth = 8,
        };
        _globalMaxLinesInput.InputChanged += (s, text) => OnSettingChanged();

        _showTruncationCheckbox = new CheckboxControl("Show truncation indicator", true);
        _showTruncationCheckbox.CheckedChanged += (s, isChecked) => OnSettingChanged();

        _breakpointDoubleInput = new PromptControl
        {
            Prompt = "2 Columns (chars):",
            InputWidth = 8,
        };
        _breakpointDoubleInput.InputChanged += (s, text) => OnSettingChanged();

        _breakpointTripleInput = new PromptControl
        {
            Prompt = "3 Columns (chars):",
            InputWidth = 8,
        };
        _breakpointTripleInput.InputChanged += (s, text) => OnSettingChanged();

        _breakpointQuadInput = new PromptControl { Prompt = "4 Columns (chars):", InputWidth = 8 };
        _breakpointQuadInput.InputChanged += (s, text) => OnSettingChanged();
    }

    private static void ClearDetailPanel()
    {
        if (_detailPanel == null)
            return;

        // Remove all children from the scrollable panel
        foreach (var child in _detailPanel.Children.ToList())
        {
            _detailPanel.RemoveControl(child);
        }
    }

    private static void UpdateDetailPanel()
    {
        if (_detailPanel == null || _selectedEntry == null || _workingConfig == null)
            return;

        // Clear existing controls
        ClearDetailPanel();

        if (_selectedEntry.IsGlobalSettings)
        {
            ShowGlobalSettings();
        }
        else if (_selectedEntry.Status == WidgetConfigStatus.Configured)
        {
            ShowConfiguredWidgetSettings();
        }
        else if (_selectedEntry.Status == WidgetConfigStatus.Available)
        {
            ShowAvailableWidgetPreview();
        }
        else if (_selectedEntry.Status == WidgetConfigStatus.Missing)
        {
            ShowMissingWidgetInfo();
        }

        UpdateButtons();
        UpdateTitle();
    }

    private static void ShowGlobalSettings()
    {
        if (_detailPanel == null || _workingConfig == null)
            return;

        // Set flag to prevent InputChanged events from applying settings while we populate
        _isPopulating = true;

        var header = Controls
            .Markup()
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
        var breakpointHeader = Controls
            .Markup()
            .AddLine("")
            .AddLine("[grey70]Responsive Breakpoints (terminal width):[/]")
            .WithMargin(1, 1, 1, 0)
            .Build();
        _detailPanel.AddControl(breakpointHeader);

        var breakpoints = _workingConfig.Breakpoints ?? new BreakpointConfig();

        _breakpointDoubleInput!.Input = breakpoints.Double.ToString();
        _breakpointDoubleInput.Margin = new Margin(1, 0, 1, 0);
        _detailPanel.AddControl(_breakpointDoubleInput);

        _breakpointTripleInput!.Input = breakpoints.Triple.ToString();
        _breakpointTripleInput.Margin = new Margin(1, 0, 1, 0);
        _detailPanel.AddControl(_breakpointTripleInput);

        _breakpointQuadInput!.Input = breakpoints.Quad.ToString();
        _breakpointQuadInput.Margin = new Margin(1, 0, 1, 0);
        _detailPanel.AddControl(_breakpointQuadInput);

        // Done populating - re-enable change events
        _isPopulating = false;
    }

    private static void ShowConfiguredWidgetSettings()
    {
        if (_detailPanel == null || _selectedEntry == null || _workingConfig == null)
            return;

        var config = _selectedEntry.Config;
        if (config == null)
            return;

        // Determine actual widget type for checksum validation
        // For Auto location, resolve to find out if it's actually bundled
        var (resolvedPath, actualLocation) = WidgetPaths.ResolveWidgetPathWithLocation(
            config.Path,
            config.Location
        );

        // If explicit location, use that; if Auto, use actual resolved location
        var effectiveLocation = config.Location ?? actualLocation;
        bool isBundled = effectiveLocation == WidgetLocation.Bundled;

        // Build header with status
        var headerBuilder = Controls
            .Markup()
            .AddLine($"[cyan1 bold]{_selectedEntry.Id}[/]")
            .AddLine($"[grey70]Path: {config.Path}[/]");

        // Show configured location and actual resolved location if different
        var configuredLocationText = config.Location switch
        {
            WidgetLocation.Bundled => "bundled",
            WidgetLocation.Custom => "custom",
            _ => "auto",
        };

        if (config.Location == null && actualLocation != null)
        {
            var actualLocationText = actualLocation switch
            {
                WidgetLocation.Bundled => "bundled",
                WidgetLocation.Custom => "custom",
                _ => "unknown",
            };
            headerBuilder.AddLine(
                $"[grey70]Location: {configuredLocationText} (resolved to {actualLocationText})[/]"
            );
        }
        else
        {
            headerBuilder.AddLine($"[grey70]Location: {configuredLocationText}[/]");
        }

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
                    checksumMismatch = !string.Equals(
                        actualChecksum,
                        expectedChecksum,
                        StringComparison.OrdinalIgnoreCase
                    );
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
                var errorBuilder = Controls
                    .Markup()
                    .WithMargin(1, 0, 1, 0)
                    .WithBackgroundColor(Color.Maroon);

                errorBuilder.AddLine(
                    "[white bold]⛔ SECURITY ALERT: Bundled widget has been modified![/]"
                );
                errorBuilder.AddLine("");
                errorBuilder.AddLine($"[white]Expected: {expectedChecksum}[/]");
                errorBuilder.AddLine($"[white]Actual:   {actualChecksum}[/]");
                errorBuilder.AddLine("");
                errorBuilder.AddLine("[yellow]This widget will NOT run until reinstalled.[/]");
                errorBuilder.AddLine("[grey70]Reinstall ServerHub to restore bundled widgets.[/]");

                _detailPanel.AddControl(errorBuilder.Build());

                _detailPanel.AddControl(
                    Controls.RuleBuilder().WithColor(Color.Grey23).WithMargin(1, 1, 1, 0).Build()
                );
            }
            else
            {
                // Bundled widget verified - show hardcoded checksum
                var infoBuilder = Controls
                    .Markup()
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
                var warningBuilder = Controls
                    .Markup()
                    .WithMargin(1, 0, 1, 0)
                    .WithBackgroundColor(Color.Grey19);

                if (checksumMismatch)
                {
                    warningBuilder.AddLine(
                        "[yellow]⚠ Script has been modified since last trust![/]"
                    );
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
                    warningBuilder.AddLine(
                        "[grey70]Widget will fail validation without checksum.[/]"
                    );
                }

                _detailPanel.AddControl(warningBuilder.Build());

                // Update checksum button (only for custom widgets with actual file)
                if (actualChecksum != null)
                {
                    var updateChecksumButton = Controls
                        .Button(" Update Checksum ")
                        .WithMargin(1, 1, 1, 0)
                        .OnClick(
                            (s, e) =>
                            {
                                if (actualChecksum != null && config != null)
                                {
                                    config.Sha256 = actualChecksum;
                                    _isDirty = true;
                                    UpdateTitle();
                                    // Refresh to show updated status
                                    UpdateDetailPanel();
                                }
                            }
                        )
                        .Build();
                    _detailPanel.AddControl(updateChecksumButton);
                }

                // Show script preview for widgets needing review
                if (_selectedEntry.FullPath != null && File.Exists(_selectedEntry.FullPath))
                {
                    try
                    {
                        var previewHeader = Controls
                            .Markup()
                            .AddLine("")
                            .AddLine("[grey70]Script Preview (first 20 lines):[/]")
                            .WithMargin(1, 0, 1, 0)
                            .Build();
                        _detailPanel.AddControl(previewHeader);

                        var lines = File.ReadLines(_selectedEntry.FullPath).Take(20);
                        var highlightedLines = ApplySyntaxHighlighting(lines);

                        var previewBuilder = Controls
                            .Markup()
                            .WithMargin(1, 0, 1, 0)
                            .WithBackgroundColor(Color.Grey19);

                        foreach (var line in highlightedLines)
                        {
                            previewBuilder.AddLine(line);
                        }

                        _detailPanel.AddControl(previewBuilder.Build());

                        // View Full File button
                        var viewFullFileButton = Controls
                            .Button(" View Full File ")
                            .WithMargin(1, 1, 1, 0)
                            .OnClick((s, e) => ShowFullFileViewer(_selectedEntry.FullPath!))
                            .Build();
                        _detailPanel.AddControl(viewFullFileButton);
                    }
                    catch (Exception ex)
                    {
                        var error = Controls
                            .Markup()
                            .AddLine($"[red]Error reading script: {ex.Message}[/]")
                            .WithMargin(1, 0, 1, 0)
                            .Build();
                        _detailPanel.AddControl(error);
                    }
                }

                _detailPanel.AddControl(
                    Controls.RuleBuilder().WithColor(Color.Grey23).WithMargin(1, 1, 1, 0).Build()
                );
            }
            else
            {
                // Custom widget with valid checksum
                var checksumInfo = Controls
                    .Markup()
                    .AddLine($"[grey70]SHA256: {config.Sha256}[/]")
                    .AddLine($"[green]Status: Verified[/]")
                    .WithMargin(1, 0, 1, 0)
                    .Build();
                _detailPanel.AddControl(checksumInfo);

                // View Full File button for verified widgets (optional viewing)
                if (_selectedEntry.FullPath != null && File.Exists(_selectedEntry.FullPath))
                {
                    var viewFullFileButton = Controls
                        .Button(" View Full File ")
                        .WithMargin(1, 1, 1, 0)
                        .OnClick((s, e) => ShowFullFileViewer(_selectedEntry.FullPath!))
                        .Build();
                    _detailPanel.AddControl(viewFullFileButton);
                }

                _detailPanel.AddControl(
                    Controls.RuleBuilder().WithColor(Color.Grey23).WithMargin(1, 1, 1, 0).Build()
                );
            }
        }

        ShowWidgetSettingsControls(config);
    }

    private static void ShowWidgetSettingsControls(WidgetConfig config)
    {
        // Set flag to prevent InputChanged events from applying settings while we populate
        _isPopulating = true;

        var settingsHeader = Controls
            .Markup()
            .AddLine("[grey70]Settings:[/]")
            .WithMargin(1, 1, 1, 0)
            .Build();
        _detailPanel!.AddControl(settingsHeader);

        // Enabled checkbox (first setting)
        _enabledCheckbox!.Checked = config.Enabled;
        _enabledCheckbox.Margin = new Margin(1, 0, 1, 0);
        _detailPanel.AddControl(_enabledCheckbox);

        var enabledHint = Controls
            .Markup()
            .AddLine(
                config.Enabled
                    ? "[grey70]Widget is visible and refreshing[/]"
                    : "[yellow]Widget is hidden and paused[/]"
            )
            .WithMargin(1, 0, 1, 1)
            .Build();
        _detailPanel.AddControl(enabledHint);

        // Refresh
        _refreshInput!.Input = config.Refresh.ToString();
        _refreshInput.Margin = new Margin(1, 0, 1, 0);
        _detailPanel.AddControl(_refreshInput);

        // Expanded refresh (optional)
        _expandedRefreshInput!.Input = config.ExpandedRefresh?.ToString() ?? "";
        _expandedRefreshInput.Margin = new Margin(1, 0, 1, 0);
        _detailPanel.AddControl(_expandedRefreshInput);

        // Pinned
        _pinnedCheckbox!.Checked = config.Pinned;
        _pinnedCheckbox.Margin = new Margin(1, 1, 1, 0);
        _detailPanel.AddControl(_pinnedCheckbox);

        // Location
        _locationDropdown!.SelectedIndex = config.Location switch
        {
            WidgetLocation.Custom => 1,
            WidgetLocation.Bundled => 2,
            _ => 0, // Auto or null
        };
        _locationDropdown.Margin = new Margin(1, 1, 1, 0);
        _detailPanel.AddControl(_locationDropdown);

        // Column span
        int columnSpanIdx = (config.ColumnSpan ?? 1) - 1;
        _columnSpanDropdown!.SelectedIndex = Math.Clamp(columnSpanIdx, 0, 3);
        _columnSpanDropdown.Margin = new Margin(1, 1, 1, 0);
        _detailPanel.AddControl(_columnSpanDropdown);

        // Max lines
        _maxLinesInput!.Input = config.MaxLines?.ToString() ?? "";
        _maxLinesInput.Margin = new Margin(1, 1, 1, 0);
        _detailPanel.AddControl(_maxLinesInput);

        // Done populating - re-enable change events
        _isPopulating = false;
    }

    private static void ShowAvailableWidgetPreview()
    {
        if (_detailPanel == null || _selectedEntry == null)
            return;

        var header = Controls
            .Markup()
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
                var checksumInfo = Controls
                    .Markup()
                    .AddLine($"[grey70]SHA256: {checksum}[/]")
                    .AddLine("")
                    .WithMargin(1, 0, 1, 0)
                    .Build();
                _detailPanel.AddControl(checksumInfo);

                var previewHeader = Controls
                    .Markup()
                    .AddLine("[grey70]Script Preview (first 20 lines):[/]")
                    .WithMargin(1, 0, 1, 0)
                    .Build();
                _detailPanel.AddControl(previewHeader);

                var lines = File.ReadLines(_selectedEntry.FullPath).Take(20);
                var highlightedLines = ApplySyntaxHighlighting(lines);

                var previewBuilder = Controls
                    .Markup()
                    .WithMargin(1, 0, 1, 0)
                    .WithBackgroundColor(Color.Grey19);

                foreach (var line in highlightedLines)
                {
                    previewBuilder.AddLine(line);
                }

                _detailPanel.AddControl(previewBuilder.Build());

                // View Full File button
                var viewFullFileButton = Controls
                    .Button(" View Full File ")
                    .WithMargin(1, 1, 1, 0)
                    .OnClick((s, e) => ShowFullFileViewer(_selectedEntry.FullPath!))
                    .Build();
                _detailPanel.AddControl(viewFullFileButton);
            }
            catch (Exception ex)
            {
                var error = Controls
                    .Markup()
                    .AddLine($"[red]Error reading script: {ex.Message}[/]")
                    .WithMargin(1, 0, 1, 0)
                    .Build();
                _detailPanel.AddControl(error);
            }
        }

        var instructions = Controls
            .Markup()
            .AddLine("")
            .AddLine("[grey70]Click 'Add to Config' to configure this widget.[/]")
            .WithMargin(1, 1, 1, 0)
            .Build();
        _detailPanel.AddControl(instructions);
    }

    private static void ShowMissingWidgetInfo()
    {
        if (_detailPanel == null || _selectedEntry == null)
            return;

        var config = _selectedEntry.Config;
        if (config == null)
            return;

        // Determine widget type based on location setting
        bool isBundled = config.Location == WidgetLocation.Bundled;

        // Build header
        var headerBuilder = Controls
            .Markup()
            .AddLine($"[cyan1 bold]{_selectedEntry.Id}[/]")
            .AddLine($"[grey70]Path: {config.Path}[/]");

        // Show location
        var locationText = config.Location switch
        {
            WidgetLocation.Bundled => "bundled",
            WidgetLocation.Custom => "custom",
            _ => "auto",
        };
        headerBuilder.AddLine($"[grey70]Location: {locationText}[/]");

        var locationHint = config.Location switch
        {
            WidgetLocation.Bundled => "bundled widgets directory",
            WidgetLocation.Custom => "custom widgets directories",
            _ => "any widget directory",
        };
        headerBuilder.AddLine($"[red bold]Status: MISSING[/]");
        headerBuilder.AddLine("");
        var missingHeader = headerBuilder.WithMargin(1, 0, 1, 0).Build();
        _detailPanel.AddControl(missingHeader);

        // Show error message
        var errorBuilder = Controls
            .Markup()
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
        _detailPanel.AddControl(
            Controls.RuleBuilder().WithColor(Color.Grey23).WithMargin(1, 1, 1, 0).Build()
        );

        // Show settings even for missing widgets (allow changing location)
        ShowWidgetSettingsControls(config);
    }

    private static void UpdateButtons()
    {
        if (_selectedEntry == null)
            return;

        bool isConfigured = _selectedEntry.Status == WidgetConfigStatus.Configured;
        bool isAvailable = _selectedEntry.Status == WidgetConfigStatus.Available;
        bool isMissing = _selectedEntry.Status == WidgetConfigStatus.Missing;
        bool isGlobal = _selectedEntry.IsGlobalSettings;

        // Up/Down buttons only for configured widgets (not global settings)
        if (_upButton != null)
        {
            _upButton.IsEnabled = isConfigured && !isGlobal;
            _upButton.Visible = isConfigured && !isGlobal;
        }
        if (_downButton != null)
        {
            _downButton.IsEnabled = isConfigured && !isGlobal;
            _downButton.Visible = isConfigured && !isGlobal;
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
        if (_dialogWindow == null)
            return;

        string title = _isDirty ? "Configure Widgets [*]" : "Configure Widgets";
        _dialogWindow.Title = title;
    }

    private static void OnSettingChanged()
    {
        // Skip if we're currently populating controls
        if (_isPopulating)
            return;

        if (_selectedEntry == null || _workingConfig == null)
            return;

        // Apply changes based on current selection
        if (_selectedEntry.IsGlobalSettings)
        {
            ApplyGlobalSettings();
        }
        else if (
            _selectedEntry.Status == WidgetConfigStatus.Configured
            && _selectedEntry.Config != null
        )
        {
            ApplyWidgetSettings(_selectedEntry.Config);
        }

        _isDirty = true;
        UpdateTitle();
    }

    private static void ApplyGlobalSettings()
    {
        if (_workingConfig == null)
            return;

        if (
            int.TryParse(_defaultRefreshInput?.Input, out int defaultRefresh)
            && defaultRefresh >= 1
        )
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

        // Apply expanded_refresh (optional)
        if (string.IsNullOrWhiteSpace(_expandedRefreshInput?.Input))
        {
            config.ExpandedRefresh = null;  // Clear if empty
        }
        else if (int.TryParse(_expandedRefreshInput?.Input, out int expandedRefresh) && expandedRefresh >= 1)
        {
            config.ExpandedRefresh = expandedRefresh;
        }

        if (_pinnedCheckbox != null)
            config.Pinned = _pinnedCheckbox.Checked;

        if (_enabledCheckbox != null)
            config.Enabled = _enabledCheckbox.Checked;

        if (_columnSpanDropdown != null)
            config.ColumnSpan = _columnSpanDropdown.SelectedIndex + 1;

        config.MaxLines = ParseNullableInt(_maxLinesInput?.Input);

        if (_locationDropdown != null)
        {
            config.Location = _locationDropdown.SelectedIndex switch
            {
                1 => WidgetLocation.Custom,
                2 => WidgetLocation.Bundled,
                _ => null, // Auto (omit from YAML)
            };
        }
    }

    private static int? ParseNullableInt(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        return int.TryParse(input, out int value) ? value : null;
    }

    /// <summary>
    /// Finds the nearest enabled widget in the specified direction from a given index.
    /// Returns -1 if no enabled widget found.
    /// </summary>
    private static int FindNearestEnabledWidget(List<string> order, int currentIndex, int direction)
    {
        if (_workingConfig == null)
            return -1;

        int searchIndex = currentIndex + direction;
        while (searchIndex >= 0 && searchIndex < order.Count)
        {
            var widgetId = order[searchIndex];
            if (_workingConfig.Widgets.TryGetValue(widgetId, out var config) && config.Enabled)
            {
                return searchIndex;
            }
            searchIndex += direction;
        }

        return -1;
    }

    private static void MoveWidgetUp()
    {
        if (
            _selectedEntry == null
            || _workingConfig == null
            || _selectedEntry.Status != WidgetConfigStatus.Configured
        )
            return;

        var order = _workingConfig.Layout?.Order;
        if (order == null)
            return;

        int idx = order.IndexOf(_selectedEntry.Id);
        if (idx <= 0)
            return;

        // Find nearest enabled widget above
        int targetIdx = FindNearestEnabledWidget(order, idx, -1);
        if (targetIdx >= 0)
        {
            // Swap with target
            (order[idx], order[targetIdx]) = (order[targetIdx], order[idx]);
            _isDirty = true;

            // Refresh list and reselect
            RefreshWidgetList(_selectedEntry.Id);
            UpdateTitle();
        }
    }

    private static void MoveWidgetDown()
    {
        if (
            _selectedEntry == null
            || _workingConfig == null
            || _selectedEntry.Status != WidgetConfigStatus.Configured
        )
            return;

        var order = _workingConfig.Layout?.Order;
        if (order == null)
            return;

        int idx = order.IndexOf(_selectedEntry.Id);
        if (idx < 0 || idx >= order.Count - 1)
            return;

        // Find nearest enabled widget below
        int targetIdx = FindNearestEnabledWidget(order, idx, 1);
        if (targetIdx >= 0)
        {
            // Swap with target
            (order[idx], order[targetIdx]) = (order[targetIdx], order[idx]);
            _isDirty = true;

            // Refresh list and reselect
            RefreshWidgetList(_selectedEntry.Id);
            UpdateTitle();
        }
    }

    private static void HandleAddRemove()
    {
        if (_selectedEntry == null || _workingConfig == null)
            return;

        if (_selectedEntry.Status == WidgetConfigStatus.Available)
        {
            AddWidget();
        }
        else if (
            _selectedEntry.Status == WidgetConfigStatus.Configured
            || _selectedEntry.Status == WidgetConfigStatus.Missing
        )
        {
            RemoveWidget();
        }
    }

    private static void AddWidget()
    {
        if (_selectedEntry == null || _workingConfig == null || _selectedEntry.FullPath == null)
            return;

        // Calculate checksum
        var checksum = ScriptValidator.CalculateChecksum(_selectedEntry.FullPath);

        // Strip " (bundled)" or " (custom)" suffix from available widget ID
        var widgetId = _selectedEntry.Id;
        if (widgetId.EndsWith(" (bundled)"))
            widgetId = widgetId.Substring(0, widgetId.Length - " (bundled)".Length);
        else if (widgetId.EndsWith(" (custom)"))
            widgetId = widgetId.Substring(0, widgetId.Length - " (custom)".Length);

        // Create widget config (set location based on original entry's config)
        var widgetConfig = new WidgetConfig
        {
            Path = _selectedEntry.Path,
            Location = _selectedEntry.Config?.Location,
            Sha256 = checksum,
            Refresh = _workingConfig.DefaultRefresh,
            Pinned = false,
        };

        // Add to config
        _workingConfig.Widgets[widgetId] = widgetConfig;

        // Add to layout order
        _workingConfig.Layout ??= new LayoutConfig { Order = new List<string>() };
        _workingConfig.Layout.Order ??= new List<string>();
        if (!_workingConfig.Layout.Order.Contains(widgetId))
        {
            _workingConfig.Layout.Order.Add(widgetId);
        }

        _isDirty = true;
        RefreshWidgetList(widgetId);
        UpdateTitle();
    }

    private static void RemoveWidget()
    {
        if (_selectedEntry == null || _workingConfig == null)
            return;

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
        if (_workingConfig == null || _configPath == null)
            return;

        try
        {
            var configManager = new ConfigManager();
            configManager.SaveConfig(_workingConfig, _configPath);
            _isDirty = false;
            UpdateTitle();

            onConfigChanged?.Invoke();

            _dialogWindow?.Close();
        }
        catch (ConfigurationException configEx)
        {
            ShowConfigurationErrorDialog(configEx);
        }
        catch (Exception ex)
        {
            // Generic errors use simple dialog
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
        if (_windowSystem == null || _dialogWindow == null)
            return;

        var confirmDialog = new WindowBuilder(_windowSystem)
            .WithTitle("Unsaved Changes")
            .WithSize(50, 10)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithColors(Color.Grey15, Color.Grey93)
            .HideCloseButton()
            .Build();

        confirmDialog.AddControl(
            Controls
                .Markup()
                .AddLine("")
                .AddLine("  You have unsaved changes.")
                .AddLine("  Do you want to save before closing?")
                .AddLine("")
                .Build()
        );

        var saveButton = Controls
            .Button(" Save & Close ")
            .OnClick(
                (s, e) =>
                {
                    _windowSystem.CloseWindow(confirmDialog);
                    SaveConfig(onConfigChanged);
                }
            )
            .Build();

        var discardButton = Controls
            .Button(" Discard ")
            .WithMargin(1, 0, 0, 0)
            .OnClick(
                (s, e) =>
                {
                    _windowSystem.CloseWindow(confirmDialog);
                    _dialogWindow?.Close();
                }
            )
            .Build();

        var cancelButton = Controls
            .Button(" Cancel ")
            .WithMargin(1, 0, 0, 0)
            .OnClick(
                (s, e) =>
                {
                    _windowSystem.CloseWindow(confirmDialog);
                }
            )
            .Build();

        var buttonGrid = HorizontalGridControl.ButtonRow(saveButton, discardButton, cancelButton);
        buttonGrid.HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment.Center;
        buttonGrid.StickyPosition = StickyPosition.Bottom;
        confirmDialog.AddControl(buttonGrid);

        _windowSystem.AddWindow(confirmDialog);
    }

    private static void ShowErrorDialog(string message)
    {
        if (_windowSystem == null)
            return;

        var errorDialog = new WindowBuilder(_windowSystem)
            .WithTitle("Error")
            .WithSize(60, 8)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithColors(Color.Grey15, Color.Grey93)
            .Build();

        errorDialog.AddControl(
            Controls
                .Markup()
                .AddLine("")
                .AddLine($"  [red]{Markup.Escape(message)}[/]")
                .AddLine("")
                .Build()
        );

        var okButton = Controls
            .Button("  OK  ")
            .OnClick((s, e) => _windowSystem.CloseWindow(errorDialog))
            .Build();

        var buttonGrid = HorizontalGridControl.ButtonRow(okButton);
        buttonGrid.HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment.Center;
        buttonGrid.StickyPosition = StickyPosition.Bottom;
        errorDialog.AddControl(buttonGrid);

        _windowSystem.AddWindow(errorDialog);
    }

    private static void ShowConfigurationErrorDialog(ConfigurationException ex)
    {
        if (_windowSystem == null)
            return;

        var errorDialog = new WindowBuilder(_windowSystem)
            .WithTitle("Configuration Error")
            .WithSize(70, 18)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithColors(Color.Grey15, Color.Grey93)
            .Build();

        // Problem
        var problemPanel = Controls
            .Markup()
            .AddLine("")
            .AddLine($"[yellow bold]Problem:[/]")
            .AddLine($"  {Markup.Escape(ex.Problem)}")
            .AddLine("")
            .WithMargin(1, 0, 1, 0)
            .Build();
        errorDialog.AddControl(problemPanel);

        // Additional info (scrollable if many items)
        if (ex.AdditionalInfo.Count > 0)
        {
            var infoBuilder = Controls.Markup().WithMargin(1, 0, 1, 0);

            foreach (var info in ex.AdditionalInfo.Take(8))
            {
                if (info.EndsWith(":"))
                    infoBuilder.AddLine($"[cyan]{Markup.Escape(info)}[/]");
                else
                    infoBuilder.AddLine($"[grey70]{Markup.Escape(info)}[/]");
            }

            if (ex.AdditionalInfo.Count > 8)
            {
                infoBuilder.AddLine($"[grey50]... and {ex.AdditionalInfo.Count - 8} more[/]");
            }

            infoBuilder.AddLine("");
            errorDialog.AddControl(infoBuilder.Build());
        }

        // How to fix
        var fixPanel = Controls
            .Markup()
            .AddLine($"[green bold]How to fix:[/]")
            .AddLine($"  {Markup.Escape(ex.HowToFix)}")
            .AddLine("")
            .WithMargin(1, 0, 1, 0)
            .Build();
        errorDialog.AddControl(fixPanel);

        // Config path
        if (!string.IsNullOrEmpty(ex.ConfigPath))
        {
            var pathPanel = Controls
                .Markup()
                .AddLine($"[grey70]Config: {Markup.Escape(ex.ConfigPath)}[/]")
                .WithMargin(1, 0, 1, 0)
                .Build();
            errorDialog.AddControl(pathPanel);
        }

        // Buttons
        var okButton = Controls
            .Button("  OK  ")
            .OnClick((s, e) => _windowSystem.CloseWindow(errorDialog))
            .Build();

        var buttonGrid = HorizontalGridControl.ButtonRow(okButton);
        buttonGrid.HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment.Center;
        buttonGrid.StickyPosition = StickyPosition.Bottom;
        buttonGrid.Margin = new Margin(0, 0, 0, 1);
        errorDialog.AddControl(buttonGrid);

        _windowSystem.AddWindow(errorDialog);
    }

    private static List<WidgetListEntry> DiscoverWidgets(ServerHubConfig config)
    {
        var discovered = WidgetConfigurationHelper.DiscoverAllWidgets(config);

        // Convert to UI-specific WidgetListEntry
        var entries = discovered
            .Select(d => {
                var entry = new WidgetListEntry
                {
                    Id = d.DisplayId,
                    Path = d.RelativePath,
                    FullPath = d.FullPath,
                    Status = d.Status,
                    Config = d.Config,
                    IsGlobalSettings = false,
                };
                return entry;
            })
            .ToList();

        return entries;
    }

    /// <summary>
    /// Applies syntax highlighting to file lines with line numbers.
    /// </summary>
    /// <param name="lines">Lines to highlight</param>
    /// <param name="startLineNumber">Starting line number (default: 1)</param>
    /// <returns>List of formatted markup strings</returns>
    private static List<string> ApplySyntaxHighlighting(
        IEnumerable<string> lines,
        int startLineNumber = 1
    )
    {
        var result = new List<string>();
        int lineNum = startLineNumber;

        foreach (var line in lines)
        {
            var escapedLine = Markup.Escape(line);
            result.Add($"[grey50]{lineNum, 3}[/] {escapedLine}");
            lineNum++;
        }

        return result;
    }

    /// <summary>
    /// Shows a modal window displaying the complete file contents.
    /// </summary>
    /// <param name="filePath">Full path to the file to display</param>
    private static void ShowFullFileViewer(string filePath)
    {
        if (_windowSystem == null || _dialogWindow == null || !File.Exists(filePath))
            return;

        var fileName = Path.GetFileName(filePath);
        int modalWidth = Math.Min((int)(Console.WindowWidth * 0.9), 150);
        int modalHeight = Math.Min((int)(Console.WindowHeight * 0.9), 45);

        // Create modal window
        var modal = new WindowBuilder(_windowSystem)
            .WithTitle(fileName)
            .WithSize(modalWidth, modalHeight)
            .Centered()
            .AsModal()
            .WithParent(_dialogWindow)
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Grey35)
            .Resizable(true)
            .Movable(true)
            .Minimizable(false)
            .Maximizable(true)
            .WithColors(Color.Grey15, Color.Grey93)
            .Build();

        try
        {
            // Header with file path
            var header = Controls
                .Markup()
                .AddLine($"[grey70]Full path: {Markup.Escape(filePath)}[/]")
                .WithMargin(1, 1, 1, 0)
                .Build();
            modal.AddControl(header);

            // Separator
            modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23).Build());

            // Read and highlight all lines
            var allLines = File.ReadLines(filePath);
            var highlightedLines = ApplySyntaxHighlighting(allLines);

            // Create scrollable content
            var contentBuilder = Controls.Markup().WithBackgroundColor(Color.Grey19);

            foreach (var line in highlightedLines)
            {
                contentBuilder.AddLine(line);
            }

            var scrollPanel = Controls
                .ScrollablePanel()
                .WithVerticalScroll(ScrollMode.Scroll)
                .WithScrollbar(true)
                .WithScrollbarPosition(ScrollbarPosition.Right)
                .WithMouseWheel(true)
                .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
                .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
                .WithBackgroundColor(Color.Grey15)
                .AddControl(contentBuilder.Build())
                .Build();

            modal.AddControl(scrollPanel);

            // Footer separator
            modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23).StickyBottom().Build());

            // Footer with instructions
            modal.AddControl(
                Controls
                    .Markup()
                    .AddLine("[grey70]↑↓: Scroll | Mouse Wheel: Scroll | Esc/Enter: Close[/]")
                    .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
                    .StickyBottom()
                    .Build()
            );

            // Handle keyboard shortcuts
            modal.KeyPressed += (s, e) =>
            {
                if (e.KeyInfo.Key == ConsoleKey.Escape || e.KeyInfo.Key == ConsoleKey.Enter)
                {
                    _windowSystem.CloseWindow(modal);
                    e.Handled = true;
                }
            };

            _windowSystem.AddWindow(modal);
            _windowSystem.SetActiveWindow(modal);
            scrollPanel.SetFocus(true, FocusReason.Programmatic);
        }
        catch (Exception ex)
        {
            // Close the modal if it was created
            if (modal != null)
            {
                _windowSystem.CloseWindow(modal);
            }
            ShowFileViewerErrorDialog(ex.Message, fileName);
        }
    }

    /// <summary>
    /// Shows an error dialog when file viewing fails.
    /// </summary>
    /// <param name="errorMessage">Error message to display</param>
    /// <param name="fileName">Name of the file that failed to load</param>
    private static void ShowFileViewerErrorDialog(string errorMessage, string fileName)
    {
        if (_windowSystem == null)
            return;

        var errorDialog = new WindowBuilder(_windowSystem)
            .WithTitle("Error Opening File")
            .WithSize(70, 10)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Red)
            .WithColors(Color.Grey15, Color.Grey93)
            .Build();

        errorDialog.AddControl(
            Controls
                .Markup()
                .AddLine("")
                .AddLine($"  [red bold]Failed to open file: {Markup.Escape(fileName)}[/]")
                .AddLine("")
                .AddLine($"  [grey70]{Markup.Escape(errorMessage)}[/]")
                .AddLine("")
                .Build()
        );

        var okButton = Controls
            .Button("  OK  ")
            .OnClick((s, e) => _windowSystem.CloseWindow(errorDialog))
            .Build();

        var buttonGrid = HorizontalGridControl.ButtonRow(okButton);
        buttonGrid.HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment.Center;
        buttonGrid.StickyPosition = StickyPosition.Bottom;
        errorDialog.AddControl(buttonGrid);

        // Handle Esc key
        errorDialog.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape || e.KeyInfo.Key == ConsoleKey.Enter)
            {
                _windowSystem.CloseWindow(errorDialog);
                e.Handled = true;
            }
        };

        _windowSystem.AddWindow(errorDialog);
    }
}
