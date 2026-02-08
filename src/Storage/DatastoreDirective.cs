// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace ServerHub.Storage;

/// <summary>
/// Represents a parsed datastore directive from widget output.
/// Follows InfluxDB line protocol format for time-series data storage.
///
/// Syntax: datastore: measurement[,tag=val,tag=val] field=val[,field=val] [timestamp]
///
/// Examples:
/// - datastore: cpu_usage value=75.5
/// - datastore: cpu_usage,core=0,host=srv01 value=75.5,temp=65
/// - datastore: disk_io,device=sda reads=1500,writes=2300
/// - datastore: metric,tag=x value=100 1707348000
/// </summary>
public class DatastoreDirective
{
    /// <summary>
    /// Measurement name (required) - identifies the metric being recorded
    /// </summary>
    public string Measurement { get; set; } = string.Empty;

    /// <summary>
    /// Tags (optional) - indexed metadata for filtering/grouping
    /// Common examples: host, region, device, core
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// Fields (required) - actual metric values
    /// Supports: integers, floats, booleans, quoted strings
    /// </summary>
    public Dictionary<string, object> Fields { get; set; } = new();

    /// <summary>
    /// Timestamp in Unix seconds (optional)
    /// If null, storage service will auto-generate current timestamp
    /// </summary>
    public long? Timestamp { get; set; }
}
