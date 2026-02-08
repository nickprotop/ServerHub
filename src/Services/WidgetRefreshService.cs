// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Models;
using ServerHub.Utils;
using ServerHub.Storage;

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
    private readonly StorageService? _storageService;

    public WidgetRefreshService(ScriptExecutor executor, WidgetProtocolParser parser, ServerHubConfig config, StorageService? storageService = null)
    {
        _executor = executor;
        _parser = parser;
        _config = config;
        _storageService = storageService;
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

        var scriptPath = WidgetPaths.ResolveWidgetPath(widgetConfig.Path, widgetConfig.Location);
        if (scriptPath == null)
        {
            var locationHint = widgetConfig.Location switch
            {
                WidgetLocation.Bundled => " in bundled widgets directory",
                WidgetLocation.Custom => " in custom widgets directories",
                _ => ""
            };
            return CreateErrorData(widgetId, $"Widget script not found{locationHint}: {widgetConfig.Path}");
        }

        try
        {
            var args = extended ? "--extended" : null;
            var result = await _executor.ExecuteAsync(scriptPath, args, widgetConfig.Sha256);

            if (result.IsSuccess)
            {
                // Parse with storage context for datafetch/history elements
                var data = _parser.Parse(result.Output ?? "", _storageService, widgetId);
                data.Timestamp = DateTime.Now;

                // Log any validation errors from parsing (including datastore directive errors)
                var validationErrors = _parser.GetValidationErrors();
                if (validationErrors.Count > 0)
                {
                    foreach (var error in validationErrors)
                    {
                        Logger.Warning($"Widget '{widgetId}' validation warning: {error}", "WidgetRefresh");
                    }
                }

                // Persist datastore directives if storage is enabled
                if (_storageService != null && _config.Storage?.Enabled == true && data.DatastoreDirectives.Count > 0)
                {
                    Logger.Debug($"Widget '{widgetId}' has {data.DatastoreDirectives.Count} datastore directives to persist", "Storage");
                    try
                    {
                        var repository = _storageService.GetRepository(widgetId);
                        foreach (var directive in data.DatastoreDirectives)
                        {
                            Logger.Debug($"Processing directive: measurement='{directive.Measurement}', tags={directive.Tags.Count}, fields={directive.Fields.Count}", "Storage");

                            // Use current timestamp if directive doesn't specify one
                            var timestamp = directive.Timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                            foreach (var field in directive.Fields)
                            {
                                // Convert field value to appropriate type for storage
                                double? fieldValue = null;
                                string? fieldText = null;

                                if (field.Value is double d)
                                    fieldValue = d;
                                else if (field.Value is int i)
                                    fieldValue = i;
                                else if (field.Value is long l)
                                    fieldValue = l;
                                else if (field.Value is float f)
                                    fieldValue = f;
                                else if (field.Value is bool b)
                                    fieldValue = b ? 1.0 : 0.0;
                                else
                                    fieldText = field.Value?.ToString();

                                repository.Insert(
                                    directive.Measurement,
                                    directive.Tags,
                                    timestamp,
                                    field.Key,
                                    fieldValue,
                                    fieldText
                                );
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log error but don't crash widget refresh
                        // Storage errors should not prevent the widget from displaying
                        Logger.Error($"Storage error for widget '{widgetId}': {ex.Message}", ex, "Storage");
                    }
                }

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
    /// Gets the refresh interval for the expanded dialog view of a specific widget.
    /// Falls back to regular refresh interval if expanded_refresh is not configured.
    /// </summary>
    /// <param name="widgetId">The widget ID</param>
    /// <returns>Refresh interval in seconds for expanded dialog view</returns>
    public int GetExpandedRefreshInterval(string widgetId)
    {
        var widgetConfig = _config.Widgets.GetValueOrDefault(widgetId);

        // Return expanded_refresh if explicitly set
        if (widgetConfig?.ExpandedRefresh.HasValue == true)
        {
            return widgetConfig.ExpandedRefresh.Value;
        }

        // Fall back to regular refresh interval
        return GetRefreshInterval(widgetId);
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
