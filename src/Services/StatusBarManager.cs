// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using SharpConsoleUI.Panel;

namespace ServerHub.Services;

/// <summary>
/// Manages status bar messages with support for temporary messages and automatic restoration
/// </summary>
public class StatusBarManager
{
    private readonly StatusTextElement _topStatus;
    private readonly StatusTextElement _bottomStatus;
    private readonly string _defaultBottomStatus;
    private bool _devMode;
    private CancellationTokenSource? _restoreCts;

    public StatusBarManager(StatusTextElement topStatus, StatusTextElement bottomStatus, bool devMode = false)
    {
        _topStatus = topStatus;
        _bottomStatus = bottomStatus;
        _devMode = devMode;
        _defaultBottomStatus = "[dim]F1[/] Help  [dim]F2[/] Config  [dim]F3[/] Marketplace  [dim]Ctrl+P[/] Commands  [dim]F5[/] Refresh  [dim]Space[/] Pause  [dim]Ctrl+Q[/] Quit";

        // Set initial bottom status (shortcuts)
        _bottomStatus.Text = _defaultBottomStatus;

        // Set initial top status (dev mode prefix will be added by UpdateDashboardStatus)
        var initialStatus = "ServerHub - Initializing...";
        _topStatus.Text = devMode
            ? $"[yellow bold]DEV MODE[/] | {initialStatus}"
            : initialStatus;
    }

    /// <summary>
    /// Update top status with widget counts, system stats, and time
    /// </summary>
    public void UpdateDashboardStatus(
        int enabledCount,
        int disabledCount,
        int okWidgets,
        int errorWidgets,
        int cpuUsage,
        int memUsage,
        bool isPaused)
    {
        var topStatus = isPaused
            ? $"[yellow]● PAUSED[/] | {enabledCount} widgets ({disabledCount} disabled) | CPU {cpuUsage}% | MEM {memUsage}% | {DateTime.Now:HH:mm:ss}"
            : $"ServerHub | {enabledCount} widgets ({okWidgets} ok, {errorWidgets} error{(disabledCount > 0 ? $", {disabledCount} disabled" : "")}) | CPU {cpuUsage}% | MEM {memUsage}% | {DateTime.Now:HH:mm:ss}";

        // Prepend dev mode warning if enabled
        if (_devMode)
        {
            topStatus = $"[yellow bold]DEV MODE[/] | {topStatus}";
        }

        _topStatus.Text = topStatus;
    }

    /// <summary>
    /// Show a temporary message in bottom status (auto-restores after delay)
    /// </summary>
    public void ShowTemporaryMessage(string message, int durationMs = 3000)
    {
        // Cancel any pending restore
        _restoreCts?.Cancel();
        _restoreCts = new CancellationTokenSource();

        _bottomStatus.Text = message;

        // Schedule restore
        Task.Delay(durationMs, _restoreCts.Token).ContinueWith(_ =>
        {
            if (!_.IsCanceled)
            {
                _bottomStatus.Text = _defaultBottomStatus;
            }
        });
    }

    /// <summary>
    /// Show a success message (green, temporary)
    /// </summary>
    public void ShowSuccess(string message, int durationMs = 3000)
    {
        ShowTemporaryMessage($"[green]✓[/] {message}", durationMs);
    }

    /// <summary>
    /// Show an error message (red, temporary)
    /// </summary>
    public void ShowError(string message, int durationMs = 3000)
    {
        ShowTemporaryMessage($"[red]✗[/] {message}", durationMs);
    }

    /// <summary>
    /// Show a warning message (yellow, temporary)
    /// </summary>
    public void ShowWarning(string message, int durationMs = 3000)
    {
        ShowTemporaryMessage($"[yellow]⚠[/] {message}", durationMs);
    }

    /// <summary>
    /// Show an info message (cyan, temporary)
    /// </summary>
    public void ShowInfo(string message, int durationMs = 3000)
    {
        ShowTemporaryMessage($"[cyan]ℹ[/] {message}", durationMs);
    }

    /// <summary>
    /// Restore default bottom status (shortcuts)
    /// </summary>
    public void RestoreDefaultStatus()
    {
        _restoreCts?.Cancel();
        _bottomStatus.Text = _defaultBottomStatus;
    }
}
