// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace ServerHub.Commands.Settings.Storage;

/// <summary>
/// Settings for the storage export command.
/// </summary>
public class StorageExportSettings : CommandSettings
{
    [CommandOption("--config <PATH>")]
    [Description("Path to configuration file")]
    public string? ConfigPath { get; set; }

    [CommandOption("--widget <WIDGET_ID>")]
    [Description("Widget ID to export (required)")]
    public string? WidgetId { get; set; }

    [CommandOption("--format <FORMAT>")]
    [Description("Export format: csv or json (default: csv)")]
    [DefaultValue("csv")]
    public string Format { get; set; } = "csv";

    [CommandOption("--output <PATH>")]
    [Description("Output file path (default: stdout)")]
    public string? OutputPath { get; set; }

    [CommandOption("--measurement <NAME>")]
    [Description("Filter by measurement name")]
    public string? Measurement { get; set; }

    [CommandOption("--since <TIMERANGE>")]
    [Description("Export data since time range (e.g., 24h, 7d)")]
    public string? Since { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(WidgetId))
        {
            return ValidationResult.Error("Widget ID is required (--widget)");
        }

        if (!Format.Equals("csv", StringComparison.OrdinalIgnoreCase) &&
            !Format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Error("Format must be 'csv' or 'json'");
        }

        return ValidationResult.Success();
    }
}
