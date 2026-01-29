// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Marketplace.Models;
using ServerHub.Marketplace.Services;
using ServerHub.Services;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using Spectre.Console;
namespace ServerHub.UI;

/// <summary>
/// Marketplace browser dialog for searching, browsing, and installing widgets
/// </summary>
public static class MarketplaceBrowserDialog
{
    private static ConsoleWindowSystem? _windowSystem;
    private static Window? _dialogWindow;
    private static MarketplaceManager? _manager;
    private static string? _configPath;
    private static Action? _onWidgetInstalled;

    // Current state
    private static List<MarketplaceManager.MarketplaceWidgetInfo> _allWidgets = new();
    private static List<MarketplaceManager.MarketplaceWidgetInfo> _filteredWidgets = new();
    private static MarketplaceManager.MarketplaceWidgetInfo? _selectedWidget;
    private static WidgetManifest? _selectedManifest;
    private static List<DependencyChecker.DependencyCheckResult>? _dependencyResults;

    // UI Controls
    private static PromptControl? _searchInput;
    private static DropdownControl? _categoryDropdown;
    private static DropdownControl? _statusDropdown;
    private static ListControl? _widgetList;
    private static MarkupControl? _detailHeader;
    private static ScrollablePanelControl? _detailPanel;
    private static MarkupControl? _loadingIndicator;
    private static ColumnContainer? _detailColumn;
    private static RuleControl? _actionButtonSeparator;
    private static HorizontalGridControl? _actionButtonGrid;

    // Search debounce
    private static System.Timers.Timer? _searchDebounceTimer;
    private const int SearchDebounceMs = 300;

    /// <summary>
    /// Shows the marketplace browser dialog
    /// </summary>
    public static void Show(
        ConsoleWindowSystem windowSystem,
        string installPath,
        string configPath,
        Action? onWidgetInstalled = null)
    {
        _windowSystem = windowSystem;
        _configPath = configPath;
        _manager = new MarketplaceManager(installPath, configPath);
        _onWidgetInstalled = onWidgetInstalled;

        // Calculate modal size: 90% of screen
        int screenWidth = Console.WindowWidth;
        int screenHeight = Console.WindowHeight;
        int modalWidth = Math.Min((int)(screenWidth * 0.9), 150);
        int modalHeight = Math.Min((int)(screenHeight * 0.9), 40);

        // Create modal dialog
        _dialogWindow = new WindowBuilder(windowSystem)
            .WithTitle("Marketplace Browser")
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

        // Build UI
        BuildUI(_dialogWindow, modalWidth, modalHeight, onWidgetInstalled);

        // Handle keyboard shortcuts
        _dialogWindow.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                _dialogWindow.Close();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.F5)
            {
                RefreshMarketplace();
                e.Handled = true;
            }
            else if ((e.KeyInfo.Key == ConsoleKey.F && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control)) ||
                     e.KeyInfo.Key == ConsoleKey.Divide)
            {
                _searchInput?.SetFocus(true, FocusReason.Programmatic);
                e.Handled = true;
            }
        };

        // Show modal
        windowSystem.AddWindow(_dialogWindow);
        windowSystem.SetActiveWindow(_dialogWindow);

        // Start initial data load
        _ = LoadMarketplaceDataAsync();
    }

    private static void BuildUI(Window dialog, int width, int height, Action? onWidgetInstalled)
    {
        // Search/Filter bar at top
        BuildSearchFilterBar(dialog, width);

        // Separator
        dialog.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23).Build());

        // Main horizontal grid: left (list) and right (details)
        var mainGrid = Controls
            .HorizontalGrid()
            .WithName("main_grid")
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .Build();

        // LEFT COLUMN - Widget List (30% width)
        var listColumn = new ColumnContainer(mainGrid)
        {
            Width = (int)(width * 0.30),
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

        BuildDetailPanel(detailColumn, onWidgetInstalled);
        mainGrid.AddColumn(detailColumn);

        dialog.AddControl(mainGrid);

        // Footer separator
        dialog.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23).StickyBottom().Build());

        // Footer instructions
        dialog.AddControl(
            Controls
                .Markup()
                .AddLine("[grey70]Tab: Switch • F5: Refresh • /: Search • Enter: Action • Esc: Close[/]")
                .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
                .StickyBottom()
                .Build()
        );
    }

    private static void BuildSearchFilterBar(Window dialog, int width)
    {
        var filterGrid = Controls
            .HorizontalGrid()
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .Build();

        // Search input (40% width)
        var searchColumn = new ColumnContainer(filterGrid)
        {
            Width = (int)(width * 0.40),
        };

        _searchInput = new PromptControl
        {
            Prompt = "Search:",
            InputWidth = Math.Max(20, (int)(width * 0.30)),
            Margin = new Margin(1, 1, 0, 0)
        };
        _searchInput.InputChanged += (s, text) => OnSearchChanged();
        searchColumn.AddContent(_searchInput);
        filterGrid.AddColumn(searchColumn);

        // Category dropdown (25% width)
        var categoryColumn = new ColumnContainer(filterGrid)
        {
            Width = (int)(width * 0.25),
        };

        _categoryDropdown = new DropdownControl("Category:")
        {
            Margin = new Margin(2, 1, 0, 0)
        };
        _categoryDropdown.AddItem("All");
        _categoryDropdown.AddItem("Monitoring");
        _categoryDropdown.AddItem("Infrastructure");
        _categoryDropdown.AddItem("Development");
        _categoryDropdown.AddItem("Databases");
        _categoryDropdown.AddItem("Networking");
        _categoryDropdown.AddItem("Security");
        _categoryDropdown.AddItem("Cloud");
        _categoryDropdown.AddItem("Utilities");
        _categoryDropdown.SelectedIndex = 0;
        _categoryDropdown.SelectedIndexChanged += (s, idx) => ApplyFilters();
        categoryColumn.AddContent(_categoryDropdown);
        filterGrid.AddColumn(categoryColumn);

        // Status dropdown (25% width)
        var statusColumn = new ColumnContainer(filterGrid)
        {
            Width = (int)(width * 0.25),
        };

        _statusDropdown = new DropdownControl("Status:")
        {
            Margin = new Margin(2, 1, 0, 0)
        };
        _statusDropdown.AddItem("All");
        _statusDropdown.AddItem("Installed");
        _statusDropdown.AddItem("Available");
        _statusDropdown.AddItem("Updates");
        _statusDropdown.AddItem("Verified");
        _statusDropdown.AddItem("Community");
        _statusDropdown.AddItem("Unverified");
        _statusDropdown.SelectedIndex = 0;
        _statusDropdown.SelectedIndexChanged += (s, idx) => ApplyFilters();
        statusColumn.AddContent(_statusDropdown);
        filterGrid.AddColumn(statusColumn);

        dialog.AddControl(filterGrid);
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

        // Add loading indicator
        _widgetList.AddItem(new ListItem("[grey50]Loading marketplace...[/]") { IsEnabled = false });

        _widgetList.SelectedIndexChanged += async (s, idx) =>
        {
            if (idx >= 0 && idx < _widgetList.Items.Count)
            {
                var item = _widgetList.Items[idx];
                if (item.Tag is MarketplaceManager.MarketplaceWidgetInfo widget)
                {
                    _selectedWidget = widget;
                    await LoadWidgetDetailsAsync(widget);
                }
            }
        };

        container.AddContent(_widgetList);
    }

    private static void BuildDetailPanel(ColumnContainer container, Action? onWidgetInstalled)
    {
        // Store reference to container for adding action buttons later
        _detailColumn = container;

        // Detail header
        _detailHeader = Controls
            .Markup()
            .WithName("detail_header")
            .AddLine("[cyan1 bold]Select a widget to view details[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Left)
            .WithMargin(1, 1, 1, 0)
            .Build();
        container.AddContent(_detailHeader);

        // Separator
        container.AddContent(Controls.RuleBuilder().WithColor(Color.Grey23).Build());

        // Loading indicator (initially hidden)
        _loadingIndicator = Controls
            .Markup()
            .WithName("loading_indicator")
            .AddLine("[cyan1]⟳ Loading widget details...[/]")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(1, 2, 1, 0)
            .Build();
        _loadingIndicator.Visible = false;
        container.AddContent(_loadingIndicator);

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

        container.AddContent(_detailPanel);

        // Separator above action buttons (initially hidden)
        _actionButtonSeparator = Controls.RuleBuilder().WithColor(Color.Grey23).Build();
        _actionButtonSeparator.Visible = false;
        container.AddContent(_actionButtonSeparator);

        // Action buttons placeholder (will be populated when widget is selected)
        // This is placed OUTSIDE the scrollable panel so buttons are always visible
        _actionButtonGrid = Controls
            .HorizontalGrid()
            .WithName("action_buttons")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .WithMargin(1, 0, 1, 1)
            .Build();
        _actionButtonGrid.Visible = false;
        container.AddContent(_actionButtonGrid);
    }

    private static async Task LoadMarketplaceDataAsync()
    {
        if (_manager == null || _widgetList == null)
            return;

        SetLoadingState(true);

        try
        {
            _allWidgets = await _manager.GetAllWidgetsAsync();
            _filteredWidgets = _allWidgets;
            PopulateWidgetList();

            // Select first widget
            if (_widgetList.Items.Count > 0 && _widgetList.Items[0].IsEnabled)
            {
                _widgetList.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            ShowError($"Failed to load marketplace: {ex.Message}");
        }
        finally
        {
            SetLoadingState(false);
        }
    }

    private static void PopulateWidgetList()
    {
        if (_widgetList == null)
            return;

        _widgetList.ClearItems();

        if (_filteredWidgets.Count == 0)
        {
            _widgetList.AddItem(new ListItem("[grey50]No widgets found[/]") { IsEnabled = false });
            return;
        }

        // Group widgets by status
        var installed = _filteredWidgets
            .Where(w => w.Status == MarketplaceManager.WidgetStatus.Installed && !w.HasUpdate)
            .OrderBy(w => w.Name)
            .ToList();

        var updates = _filteredWidgets
            .Where(w => w.HasUpdate)
            .OrderBy(w => w.Name)
            .ToList();

        var available = _filteredWidgets
            .Where(w => w.Status == MarketplaceManager.WidgetStatus.Available)
            .OrderBy(w => w.Name)
            .ToList();

        // Add INSTALLED section
        if (installed.Count > 0)
        {
            _widgetList.AddItem(new ListItem($"[green bold]📦 INSTALLED ({installed.Count})[/]") { IsEnabled = false });
            foreach (var widget in installed)
            {
                var badge = GetVerificationBadge(widget.VerificationLevel);
                var item = new ListItem($"[green]✓[/] {widget.Name} ({widget.InstalledVersion}) {badge}")
                {
                    Tag = widget
                };
                _widgetList.AddItem(item);
            }
            _widgetList.AddItem(new ListItem("") { IsEnabled = false });
        }

        // Add UPDATES section
        if (updates.Count > 0)
        {
            _widgetList.AddItem(new ListItem($"[yellow bold]🔄 UPDATES ({updates.Count})[/]") { IsEnabled = false });
            foreach (var widget in updates)
            {
                var badge = GetVerificationBadge(widget.VerificationLevel);
                var item = new ListItem($"[yellow]⬆[/] {widget.Name} → {widget.LatestVersion} {badge}")
                {
                    Tag = widget
                };
                _widgetList.AddItem(item);
            }
            _widgetList.AddItem(new ListItem("") { IsEnabled = false });
        }

        // Add AVAILABLE section
        if (available.Count > 0)
        {
            _widgetList.AddItem(new ListItem($"[cyan1 bold]🌐 AVAILABLE ({available.Count})[/]") { IsEnabled = false });
            foreach (var widget in available)
            {
                var badge = GetVerificationBadge(widget.VerificationLevel);
                var item = new ListItem($"[cyan1]•[/] {widget.Name} (v{widget.LatestVersion}) {badge}")
                {
                    Tag = widget
                };
                _widgetList.AddItem(item);
            }
        }
    }

    private static async Task LoadWidgetDetailsAsync(MarketplaceManager.MarketplaceWidgetInfo widget)
    {
        if (_manager == null || _detailPanel == null || _detailHeader == null || _loadingIndicator == null)
            return;

        // Show loading indicator
        _loadingIndicator.Visible = true;
        ClearDetailPanel();

        try
        {
            // Fetch manifest (always fresh)
            _selectedManifest = await _manager.GetWidgetManifestAsync(widget.ManifestUrl);
            if (_selectedManifest == null)
            {
                ShowError("Failed to load widget manifest");
                return;
            }

            // Check dependencies
            _dependencyResults = _manager.CheckDependencies(_selectedManifest.Dependencies);

            // Update detail panel
            DisplayWidgetDetails(widget, _selectedManifest, _dependencyResults);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to load widget details: {ex.Message}");
        }
        finally
        {
            _loadingIndicator.Visible = false;
        }
    }

    private static void DisplayWidgetDetails(
        MarketplaceManager.MarketplaceWidgetInfo widget,
        WidgetManifest manifest,
        List<DependencyChecker.DependencyCheckResult> dependencies)
    {
        if (_detailPanel == null || _detailHeader == null)
            return;

        ClearDetailPanel();

        var metadata = manifest.Metadata;
        var badge = GetVerificationBadgeLarge(widget.VerificationLevel);

        // Update header
        _detailHeader.SetContent(new List<string>
        {
            $"[cyan1 bold]{widget.Id}[/]",
            badge
        });

        // Metadata section
        var metadataBuilder = Controls
            .Markup()
            .AddLine($"[grey70]Author:[/] {metadata.Author}")
            .AddLine($"[grey70]Category:[/] {metadata.Category}")
            .AddLine($"[grey70]License:[/] {metadata.License}")
            .AddLine($"[grey70]Latest:[/] {widget.LatestVersion} ({manifest.LatestVersion?.Released:yyyy-MM-dd})")
            .WithMargin(1, 0, 1, 0);

        if (widget.Status == MarketplaceManager.WidgetStatus.Installed)
        {
            metadataBuilder.AddLine($"[grey70]Installed:[/] [green]{widget.InstalledVersion}[/]");
        }

        _detailPanel.AddControl(metadataBuilder.Build());

        // Description section
        var descriptionBuilder = Controls
            .Markup()
            .AddLine("")
            .AddLine("[grey70 bold]Description:[/]")
            .WithMargin(1, 0, 1, 0);

        foreach (var line in metadata.Description.Split('\n'))
        {
            descriptionBuilder.AddLine($"  {Markup.Escape(line)}");
        }

        _detailPanel.AddControl(descriptionBuilder.Build());

        // Dependencies section
        if (dependencies.Count > 0)
        {
            var depsBuilder = Controls
                .Markup()
                .AddLine("")
                .AddLine("[grey70 bold]Dependencies:[/]")
                .WithMargin(1, 0, 1, 0);

            foreach (var dep in dependencies)
            {
                var status = dep.Found
                    ? "[green]✓[/]"
                    : (dep.IsOptional ? "[grey50]○[/]" : "[red]✗[/]");
                var path = dep.Found
                    ? $"[grey50]({dep.Path})[/]"
                    : (dep.IsOptional ? "[grey50](optional, not found)[/]" : "[red](not found)[/]");
                depsBuilder.AddLine($"  {status} {dep.Command} {path}");
            }

            _detailPanel.AddControl(depsBuilder.Build());
        }

        // Versions section (show latest 5)
        if (manifest.Versions.Count > 0)
        {
            var versionsBuilder = Controls
                .Markup()
                .AddLine("")
                .AddLine("[grey70 bold]Versions:[/]")
                .WithMargin(1, 0, 1, 0);

            var versions = manifest.Versions
                .OrderByDescending(v => v.Released)
                .Take(5)
                .ToList();

            foreach (var ver in versions)
            {
                var latest = ver == manifest.LatestVersion ? " [cyan1](latest)[/]" : "";
                versionsBuilder.AddLine($"  {ver.Version} - {ver.Released:yyyy-MM-dd}{latest}");
            }

            if (manifest.Versions.Count > 5)
            {
                versionsBuilder.AddLine($"  [grey50]... and {manifest.Versions.Count - 5} more[/]");
            }

            _detailPanel.AddControl(versionsBuilder.Build());
        }

        // Action buttons (built outside scrollable panel)
        BuildActionButtons(widget, manifest, dependencies);
    }

    private static void BuildActionButtons(
        MarketplaceManager.MarketplaceWidgetInfo widget,
        WidgetManifest manifest,
        List<DependencyChecker.DependencyCheckResult> dependencies)
    {
        if (_actionButtonGrid == null)
            return;

        // Clear existing buttons
        foreach (var child in _actionButtonGrid.Columns.ToList())
        {
            _actionButtonGrid.RemoveColumn(child);
        }

        var buttons = new List<ButtonControl>();

        if (widget.Status == MarketplaceManager.WidgetStatus.Available)
        {
            // Not installed - show Install button
            var installButton = Controls
                .Button(" Install ")
                .OnClick((s, e) => HandleInstall(widget, manifest, dependencies, null))
                .Build();
            buttons.Add(installButton);
        }
        else if (widget.HasUpdate)
        {
            // Update available
            var updateButton = Controls
                .Button($" Update to {widget.LatestVersion} ")
                .OnClick((s, e) => HandleInstall(widget, manifest, dependencies, widget.LatestVersion))
                .Build();
            buttons.Add(updateButton);

            var uninstallButton = Controls
                .Button(" Uninstall ")
                .WithMargin(1, 0, 0, 0)
                .OnClick((s, e) => HandleUninstall(widget))
                .Build();
            buttons.Add(uninstallButton);
        }
        else if (widget.Status == MarketplaceManager.WidgetStatus.Installed)
        {
            // Installed, no update
            var reinstallButton = Controls
                .Button(" Reinstall ")
                .OnClick((s, e) => HandleInstall(widget, manifest, dependencies, widget.InstalledVersion))
                .Build();
            buttons.Add(reinstallButton);

            var uninstallButton = Controls
                .Button(" Uninstall ")
                .WithMargin(1, 0, 0, 0)
                .OnClick((s, e) => HandleUninstall(widget))
                .Build();
            buttons.Add(uninstallButton);
        }

        // View Source button (always shown)
        var viewSourceButton = Controls
            .Button("View Source")
            .WithWidth(18)
            .WithMargin(1, 0, 0, 0)
            .OnClick((s, e) =>
            {
                var latestVersion = manifest.LatestVersion;
                if (latestVersion != null && latestVersion.Artifacts.Count > 0)
                {
                    var artifact = latestVersion.Artifacts[0];
                    SourceViewerDialog.ShowFromUrl(_windowSystem!, artifact.Name, artifact.Url, _dialogWindow);
                }
            })
            .Build();
        buttons.Add(viewSourceButton);

        // Add buttons to the action grid (outside scrollable panel)
        foreach (var button in buttons)
        {
            var column = new ColumnContainer(_actionButtonGrid);
            column.AddContent(button);
            _actionButtonGrid.AddColumn(column);
        }

        // Show the separator and action buttons
        if (_actionButtonSeparator != null)
        {
            _actionButtonSeparator.Visible = true;
        }
        _actionButtonGrid.Visible = true;
    }

    private static void HandleInstall(
        MarketplaceManager.MarketplaceWidgetInfo widget,
        WidgetManifest manifest,
        List<DependencyChecker.DependencyCheckResult> dependencies,
        string? version)
    {
        if (_windowSystem == null || _dialogWindow == null)
            return;

        // Check for unverified widgets
        if (widget.VerificationLevel == VerificationLevel.Unverified)
        {
            UnverifiedWarningDialog.Show(
                _windowSystem,
                widget,
                manifest,
                _dialogWindow,
                (accepted) =>
                {
                    if (accepted)
                    {
                        ContinueInstall(widget, manifest, dependencies, version);
                    }
                }
            );
        }
        else
        {
            ContinueInstall(widget, manifest, dependencies, version);
        }
    }

    private static void ContinueInstall(
        MarketplaceManager.MarketplaceWidgetInfo widget,
        WidgetManifest manifest,
        List<DependencyChecker.DependencyCheckResult> dependencies,
        string? version)
    {
        if (_windowSystem == null || _dialogWindow == null || _manager == null || _configPath == null)
            return;

        // Check for missing dependencies
        var missing = dependencies.Where(d => !d.Found && !d.IsOptional).ToList();
        if (missing.Count > 0)
        {
            ShowMissingDependenciesDialog(missing, _dialogWindow);
            return;
        }

        // Show installation dialog
        InstallationDialog.Show(
            _windowSystem,
            _manager,
            widget,
            manifest,
            version,
            _dialogWindow,
            _configPath,
            (success) =>
            {
                if (success)
                {
                    // Refresh marketplace to show installed widget
                    RefreshMarketplace();

                    // Trigger dashboard reload callback
                    _onWidgetInstalled?.Invoke();
                }
            }
        );
    }

    private static void HandleUninstall(MarketplaceManager.MarketplaceWidgetInfo widget)
    {
        // TODO: Implement uninstall functionality
        // For now, just show a message
        if (_windowSystem != null)
        {
            _windowSystem.NotificationStateService.ShowNotification(
                "Not Implemented",
                "Uninstall functionality coming soon. Please remove from config manually.",
                NotificationSeverity.Info,
                timeout: 5000
            );
        }
    }

    private static void ShowMissingDependenciesDialog(
        List<DependencyChecker.DependencyCheckResult> missing,
        Window? parentWindow = null)
    {
        if (_windowSystem == null)
            return;

        var builder = new WindowBuilder(_windowSystem)
            .WithTitle("Missing Dependencies")
            .WithSize(70, 15)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Red)
            .Resizable(false)
            .Movable(false)
            .Minimizable(false)
            .Maximizable(false)
            .WithColors(Color.Grey15, Color.Grey93);

        if (parentWindow != null)
        {
            builder = builder.WithParent(parentWindow);
        }

        var errorDialog = builder.Build();

        var messageBuilder = Controls
            .Markup()
            .AddLine("")
            .AddLine("[red bold]Missing required dependencies:[/]")
            .AddLine("");

        foreach (var dep in missing)
        {
            messageBuilder.AddLine($"  [red]✗[/] {dep.Command}");
        }

        messageBuilder.AddLine("")
            .AddLine("[grey70]Please install these dependencies before installing this widget.[/]")
            .AddLine("");

        errorDialog.AddControl(messageBuilder.WithMargin(1, 0, 1, 0).Build());

        var okButton = Controls
            .Button("  OK  ")
            .OnClick((s, e) => _windowSystem.CloseWindow(errorDialog))
            .Build();

        var buttonGrid = HorizontalGridControl.ButtonRow(okButton);
        buttonGrid.HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment.Center;
        buttonGrid.StickyPosition = StickyPosition.Bottom;
        errorDialog.AddControl(buttonGrid);

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

    private static void OnSearchChanged()
    {
        // Debounce search input
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer = new System.Timers.Timer(SearchDebounceMs);
        _searchDebounceTimer.Elapsed += (s, e) =>
        {
            _searchDebounceTimer.Stop();
            ApplyFilters();
        };
        _searchDebounceTimer.AutoReset = false;
        _searchDebounceTimer.Start();
    }

    private static void ApplyFilters()
    {
        if (_manager == null || _searchInput == null || _categoryDropdown == null || _statusDropdown == null)
            return;

        // Apply search filter
        var query = _searchInput.Input;
        _filteredWidgets = string.IsNullOrWhiteSpace(query)
            ? _allWidgets
            : _allWidgets.Where(w =>
                w.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                w.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                w.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                w.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        // Apply category filter
        var category = _categoryDropdown.Items[_categoryDropdown.SelectedIndex].Text;
        _filteredWidgets = _manager.FilterByCategory(_filteredWidgets, category);

        // Apply status filter
        var status = _statusDropdown.Items[_statusDropdown.SelectedIndex].Text;
        _filteredWidgets = _manager.FilterByStatus(_filteredWidgets, status);

        PopulateWidgetList();

        // Auto-select first item
        if (_widgetList != null && _widgetList.Items.Count > 0)
        {
            for (int i = 0; i < _widgetList.Items.Count; i++)
            {
                if (_widgetList.Items[i].IsEnabled)
                {
                    _widgetList.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private static void RefreshMarketplace()
    {
        _ = LoadMarketplaceDataAsync();
    }

    private static void ClearDetailPanel()
    {
        if (_detailPanel == null)
            return;

        foreach (var child in _detailPanel.Children.ToList())
        {
            _detailPanel.RemoveControl(child);
        }

        // Hide action buttons and separator when clearing
        if (_actionButtonSeparator != null)
        {
            _actionButtonSeparator.Visible = false;
        }
        if (_actionButtonGrid != null)
        {
            _actionButtonGrid.Visible = false;
        }
    }

    private static void SetLoadingState(bool loading)
    {
        if (_widgetList == null)
            return;

        if (loading)
        {
            _widgetList.ClearItems();
            _widgetList.AddItem(new ListItem("[grey50]Loading marketplace...[/]") { IsEnabled = false });
        }
    }

    private static void ShowError(string message)
    {
        if (_widgetList == null)
            return;

        _widgetList.ClearItems();
        _widgetList.AddItem(new ListItem($"[red]Error: {Markup.Escape(message)}[/]") { IsEnabled = false });
    }

    private static string GetVerificationBadge(VerificationLevel level)
    {
        return level switch
        {
            VerificationLevel.Verified => "[green]✓[/]",
            VerificationLevel.Community => "[yellow]⚡[/]",
            VerificationLevel.Unverified => "[red]⚠[/]",
            _ => ""
        };
    }

    private static string GetVerificationBadgeLarge(VerificationLevel level)
    {
        return level switch
        {
            VerificationLevel.Verified => "[green]✓ Verified by ServerHub[/]",
            VerificationLevel.Community => "[yellow]⚡ Community Widget[/]",
            VerificationLevel.Unverified => "[red]⚠ Unverified Widget[/]",
            _ => ""
        };
    }
}
