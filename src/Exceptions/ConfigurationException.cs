// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace ServerHub.Exceptions;

/// <summary>
/// Base exception for configuration errors with helpful context
/// </summary>
public class ConfigurationException : Exception
{
    public string Problem { get; }
    public string HowToFix { get; }
    public string? ConfigPath { get; set; }
    public List<string> AdditionalInfo { get; } = new();

    public ConfigurationException(string problem, string howToFix)
        : base(problem)
    {
        Problem = problem;
        HowToFix = howToFix;
    }
}

/// <summary>
/// Invalid widget reference in layout.order
/// </summary>
public class InvalidLayoutWidgetException : ConfigurationException
{
    public string InvalidWidgetId { get; }
    public List<string> AvailableWidgets { get; }

    public InvalidLayoutWidgetException(string widgetId, List<string> available)
        : base(
            $"Layout.order contains widget '{widgetId}' which doesn't exist in your config",
            $"Remove '{widgetId}' from layout.order or add it to widgets section")
    {
        InvalidWidgetId = widgetId;
        AvailableWidgets = available;

        if (available.Count > 0)
        {
            AdditionalInfo.Add($"Available widgets ({available.Count}):");
            foreach (var w in available.Take(10))
            {
                AdditionalInfo.Add($"  - {w}");
            }
            if (available.Count > 10)
            {
                AdditionalInfo.Add($"  ... and {available.Count - 10} more");
            }
        }
    }
}

/// <summary>
/// Widget missing required path
/// </summary>
public class MissingWidgetPathException : ConfigurationException
{
    public string WidgetId { get; }

    public MissingWidgetPathException(string widgetId)
        : base(
            $"Widget '{widgetId}' has no path specified",
            $"Add a 'path' field to widget '{widgetId}' in your config")
    {
        WidgetId = widgetId;
        AdditionalInfo.Add("Example:");
        AdditionalInfo.Add($"  {widgetId}:");
        AdditionalInfo.Add($"    path: my-script.sh");
        AdditionalInfo.Add($"    refresh: 5");
    }
}

/// <summary>
/// Invalid refresh interval
/// </summary>
public class InvalidRefreshIntervalException : ConfigurationException
{
    public string WidgetId { get; }
    public int InvalidInterval { get; }

    public InvalidRefreshIntervalException(string widgetId, int interval)
        : base(
            $"Widget '{widgetId}' has invalid refresh interval: {interval}",
            $"Set refresh to 1 or higher for widget '{widgetId}'")
    {
        WidgetId = widgetId;
        InvalidInterval = interval;
        AdditionalInfo.Add($"Current value: {interval} seconds");
        AdditionalInfo.Add("Minimum allowed: 1 second");
    }

    public InvalidRefreshIntervalException(string widgetId, int interval, string fieldName)
        : base(
            $"Widget '{widgetId}' has invalid {fieldName} interval: {interval}",
            $"Set {fieldName} to 1 or higher for widget '{widgetId}'")
    {
        WidgetId = widgetId;
        InvalidInterval = interval;
        AdditionalInfo.Add($"Field: {fieldName}");
        AdditionalInfo.Add($"Current value: {interval} seconds");
        AdditionalInfo.Add("Minimum allowed: 1 second");
    }
}
