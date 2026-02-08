// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using SharpConsoleUI.Logging;

namespace ServerHub.Utils;

/// <summary>
/// Centralized logging for ServerHub.
/// Uses SharpConsoleUI's LogService with file-based debug logging.
///
/// Enable debug logging:
///   export SHARPCONSOLEUI_DEBUG_LOG=/tmp/serverhub-debug.log
///   export SHARPCONSOLEUI_DEBUG_LEVEL=Debug
/// </summary>
public static class Logger
{
    private static ILogService? _logService;

    /// <summary>
    /// Initializes the logger with a LogService instance.
    /// Should be called once during app startup after ConsoleWindowSystem is created.
    /// </summary>
    public static void Initialize(ILogService logService)
    {
        _logService = logService;
        // Immediate test to verify logger is working
        _logService?.LogInfo("=== ServerHub Logger.Initialize() called ===", "Logger");
        _logService?.LogDebug("Logger test: Debug level is working", "Logger");
    }

    /// <summary>
    /// Logs a debug message (requires Debug level or lower)
    /// </summary>
    public static void Debug(string message, string? category = null)
    {
        if (_logService == null)
        {
            System.Console.Error.WriteLine($"[Logger] ERROR: LogService is null when trying to log: {message}");
            return;
        }
        _logService.LogDebug(message, category);
    }

    /// <summary>
    /// Logs an informational message
    /// </summary>
    public static void Info(string message, string? category = null)
    {
        _logService?.LogInfo(message, category);
    }

    /// <summary>
    /// Logs a warning message
    /// </summary>
    public static void Warning(string message, string? category = null)
    {
        _logService?.LogWarning(message, category);
    }

    /// <summary>
    /// Logs an error message with optional exception
    /// </summary>
    public static void Error(string message, Exception? exception = null, string? category = null)
    {
        _logService?.LogError(message, exception, category);
    }

    /// <summary>
    /// Logs a critical error message with optional exception
    /// </summary>
    public static void Critical(string message, Exception? exception = null, string? category = null)
    {
        _logService?.LogCritical(message, exception, category);
    }
}
