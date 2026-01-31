// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Spectre.Console;

namespace ServerHub.Config;

/// <summary>
/// Central configuration constants for the ServerHub dashboard UI and behavior.
/// Modify these values to customize dashboard appearance and performance.
/// </summary>
public static class DashboardConstants
{
    // ============================================================
    // Render & Performance
    // ============================================================

    /// <summary>
    /// Interval (in milliseconds) for batching widget render updates.
    /// Lower values = more responsive but more CPU usage.
    /// Higher values = smoother batching but higher latency.
    /// Recommended: 250-1000ms
    /// </summary>
    public const int RenderBatchIntervalMs = 500;

    /// <summary>
    /// Maximum time (in milliseconds) to wait for spinner animation updates.
    /// Controls how often the loading spinner refreshes while widgets are updating.
    /// </summary>
    public const int SpinnerUpdateIntervalMs = 100;

    // ============================================================
    // UI Layout
    // ============================================================

    /// <summary>
    /// Spacing (in characters) between widgets in the grid layout.
    /// </summary>
    public const int WidgetSpacing = 0;

    /// <summary>
    /// Default maximum lines to display per widget (can be overridden per widget).
    /// </summary>
    public const int DefaultMaxLinesPerWidget = 20;

    // ============================================================
    // UI Elements
    // ============================================================

    /// <summary>
    /// Spinner animation frames shown while widgets are refreshing.
    /// Unicode characters that create a rotating animation effect.
    /// </summary>
    public static readonly string[] SpinnerFrames = { "◐", "◓", "◑", "◒" };

    /// <summary>
    /// Double-click threshold in milliseconds for widget interactions.
    /// </summary>
    public const int DoubleClickThresholdMs = 500;

    // ============================================================
    // Error Handling
    // ============================================================

    /// <summary>
    /// Number of consecutive errors before considering a widget permanently failed.
    /// </summary>
    public const int MaxConsecutiveErrors = 10;

    /// <summary>
    /// Time (in seconds) to wait before retrying a failed widget.
    /// </summary>
    public const int ErrorRetryDelaySeconds = 5;

    // ============================================================
    // Widget Refresh & Execution
    // ============================================================

    /// <summary>
    /// Default widget refresh interval (in seconds) if not specified in config.
    /// </summary>
    public const int DefaultWidgetRefreshSeconds = 5;

    /// <summary>
    /// Minimum allowed widget refresh interval (in seconds).
    /// Prevents widgets from updating too frequently and consuming excessive resources.
    /// </summary>
    public const int MinWidgetRefreshSeconds = 1;

    /// <summary>
    /// Default timeout (in seconds) for widget script execution.
    /// Scripts that take longer than this will be terminated.
    /// </summary>
    public const int DefaultScriptTimeoutSeconds = 10;

    // ============================================================
    // Dialog Dimensions
    // ============================================================

    /// <summary>
    /// Maximum width for modal dialogs (in characters).
    /// </summary>
    public const int MaxDialogWidth = 150;

    /// <summary>
    /// Maximum height for modal dialogs (in characters).
    /// </summary>
    public const int MaxDialogHeight = 40;

    /// <summary>
    /// Percentage of screen size to use for modal dialogs (0.0 to 1.0).
    /// </summary>
    public const double DialogScreenRatio = 0.9;

    /// <summary>
    /// Width of the help dialog (in characters).
    /// </summary>
    public const int HelpDialogWidth = 78;

    /// <summary>
    /// Minimum padding from screen edge for dialogs.
    /// </summary>
    public const int DialogEdgePadding = 10;

    // ============================================================
    // Color Scheme
    // ============================================================

    /// <summary>
    /// Background color for the main window.
    /// </summary>
    public static readonly Color WindowBackgroundColor = Color.Grey11;

    /// <summary>
    /// Foreground color for the main window.
    /// </summary>
    public static readonly Color WindowForegroundColor = Color.Grey93;

    /// <summary>
    /// Background color for widgets.
    /// </summary>
    public static readonly Color WidgetBackgroundColor = Color.Grey11;

    /// <summary>
    /// Border color for inactive widgets.
    /// </summary>
    public static readonly Color InactiveWidgetBorderColor = Color.Grey35;

    /// <summary>
    /// Border color for active/focused widgets.
    /// </summary>
    public static readonly Color ActiveWidgetBorderColor = Color.Cyan1;

    /// <summary>
    /// Border color for widgets when window is active.
    /// </summary>
    public static readonly Color WindowActiveBorderColor = Color.Orange1;

    /// <summary>
    /// Primary accent color used throughout the UI.
    /// </summary>
    public static readonly Color AccentColor = Color.Cyan1;

    /// <summary>
    /// Color for help dialog backgrounds.
    /// </summary>
    public static readonly Color HelpDialogBackgroundColor = Color.Grey15;

    // ============================================================
    // Text & Formatting
    // ============================================================

    /// <summary>
    /// Application version string.
    /// </summary>
    public const string AppVersion = "v0.1.0";

    /// <summary>
    /// Application title.
    /// </summary>
    public const string AppTitle = "ServerHub";

    /// <summary>
    /// Application subtitle/description.
    /// </summary>
    public const string AppSubtitle = "Server Monitoring Dashboard";
}
