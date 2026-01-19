// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Models;
using ServerHub.Utils;

namespace ServerHub.Services;

/// <summary>
/// Encapsulates widget refresh logic for use by both main dialog and modal.
/// Handles script execution, parsing, and error handling.
/// </summary>
public class WidgetRefreshService
{
    private readonly ScriptExecutor _executor;
    private readonly WidgetProtocolParser _parser;
    private readonly ServerHubConfig _config;

    public WidgetRefreshService(ScriptExecutor executor, WidgetProtocolParser parser, ServerHubConfig config)
    {
        _executor = executor;
        _parser = parser;
        _config = config;
    }

    /// <summary>
    /// Refreshes a widget by executing its script and parsing the output
    /// </summary>
    /// <param name="widgetId">Widget identifier</param>
    /// <param name="extended">Whether to use --extended argument</param>
    /// <returns>Widget data with parsed content or error information</returns>
    public async Task<WidgetData> RefreshAsync(string widgetId, bool extended = false)
    {
        var widgetConfig = _config.Widgets.GetValueOrDefault(widgetId);
        if (widgetConfig == null)
        {
            return CreateErrorData(widgetId, "Widget not configured");
        }

        var scriptPath = WidgetPaths.ResolveWidgetPath(widgetConfig.Path);
        if (scriptPath == null)
        {
            return CreateErrorData(widgetId, $"Widget script not found: {widgetConfig.Path}");
        }

        try
        {
            var args = extended ? "--extended" : null;
            var result = await _executor.ExecuteAsync(scriptPath, args, widgetConfig.Sha256);

            if (result.IsSuccess)
            {
                var data = _parser.Parse(result.Output ?? "");
                data.Timestamp = DateTime.Now;
                return data;
            }

            return CreateErrorData(widgetId, result.ErrorMessage ?? "Unknown error");
        }
        catch (Exception ex)
        {
            return CreateErrorData(widgetId, $"Exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the refresh interval for a widget
    /// </summary>
    /// <param name="widgetId">Widget identifier</param>
    /// <returns>Refresh interval in seconds</returns>
    public int GetRefreshInterval(string widgetId)
    {
        var widgetConfig = _config.Widgets.GetValueOrDefault(widgetId);
        return widgetConfig?.Refresh ?? _config.DefaultRefresh;
    }

    /// <summary>
    /// Gets the widget configuration
    /// </summary>
    /// <param name="widgetId">Widget identifier</param>
    /// <returns>Widget configuration or null if not found</returns>
    public WidgetConfig? GetWidgetConfig(string widgetId)
    {
        return _config.Widgets.GetValueOrDefault(widgetId);
    }

    private static WidgetData CreateErrorData(string widgetId, string error)
    {
        return new WidgetData
        {
            Title = widgetId,
            Error = error,
            Timestamp = DateTime.Now,
            Rows = new List<WidgetRow>
            {
                new()
                {
                    Content = "[red]Error[/]",
                    Status = new() { State = StatusState.Error }
                },
                new() { Content = "" },
                new() { Content = $"[grey70]{error}[/]" }
            }
        };
    }
}
