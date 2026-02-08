// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using ServerHub.Storage;

namespace ServerHub.Models;

/// <summary>
/// Represents the parsed data from a widget script's output
/// </summary>
public class WidgetData
{
    /// <summary>
    /// Widget title (from "title:" protocol element)
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Refresh interval in seconds (from "refresh:" protocol element)
    /// </summary>
    public int RefreshInterval { get; set; } = 5;

    /// <summary>
    /// Collection of data rows to display
    /// </summary>
    public List<WidgetRow> Rows { get; set; } = new();

    /// <summary>
    /// Collection of available actions (interactive widgets)
    /// </summary>
    public List<WidgetAction> Actions { get; set; } = new();

    /// <summary>
    /// Timestamp when this data was collected
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// Error message if widget execution failed
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Whether this widget data represents an error state
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>
    /// Whether this widget has any actions
    /// </summary>
    public bool HasActions => Actions.Count > 0;

    /// <summary>
    /// Collection of datastore directives for time-series data storage
    /// </summary>
    public List<DatastoreDirective> DatastoreDirectives { get; set; } = new();
}
