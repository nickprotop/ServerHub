// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace ServerHub.Models;

/// <summary>
/// Status of a discovered widget relative to configuration.
/// </summary>
public enum WidgetConfigStatus
{
    /// <summary>Widget is in config and script exists</summary>
    Configured,
    /// <summary>Script exists but not in config</summary>
    Available,
    /// <summary>In config but script not found</summary>
    Missing
}

/// <summary>
/// Represents a discovered widget with its metadata and status.
/// </summary>
public class DiscoveredWidget
{
    public required string DisplayId { get; init; }
    public required string RelativePath { get; init; }
    public required string? FullPath { get; init; }
    public required WidgetConfigStatus Status { get; init; }
    public required WidgetConfig Config { get; init; }
    public WidgetLocation? ActualLocation { get; init; }
}

/// <summary>
/// Result of widget validation.
/// </summary>
public class WidgetValidationResult
{
    public required string WidgetId { get; init; }
    public required bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public WidgetConfigStatus Status { get; init; }
    public string? FullPath { get; init; }
}
