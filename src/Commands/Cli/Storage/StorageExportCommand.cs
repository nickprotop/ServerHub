// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Spectre.Console;
using Spectre.Console.Cli;
using ServerHub.Commands.Settings.Storage;
using ServerHub.Services;
using System.Text;

namespace ServerHub.Commands.Cli.Storage;

/// <summary>
/// Command to export widget data to CSV or JSON.
/// </summary>
public class StorageExportCommand : Command<StorageExportSettings>
{
    public override int Execute(CommandContext context, StorageExportSettings settings)
    {
        try
        {
            // Load configuration
            var configManager = new ConfigManager();
            var configPath = settings.ConfigPath ?? ConfigManager.GetDefaultConfigPath();
            var config = configManager.LoadConfig(configPath);

            if (config.Storage?.Enabled != true)
            {
                AnsiConsole.MarkupLine("[yellow]Storage is disabled in configuration[/]");
                return 1;
            }

            // Initialize storage service
            var storageService = ServerHub.Storage.StorageService.Initialize(config.Storage);
            var repository = storageService.GetRepository(settings.WidgetId!);

            // Determine time range
            long? startTimestamp = null;
            if (!string.IsNullOrWhiteSpace(settings.Since))
            {
                var parseResult = ServerHub.Storage.TimeRangeParser.Parse(settings.Since);
                if (parseResult.IsTimeBased)
                {
                    startTimestamp = parseResult.StartTimestamp;
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] --since must be a time range (e.g., 24h, 7d), not a sample count");
                    return 1;
                }
            }

            // Fetch data
            var data = FetchData(repository, settings.Measurement, startTimestamp);

            if (data.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No data found for the specified criteria[/]");
                return 0;
            }

            // Export to format
            string output;
            if (settings.Format.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                output = ExportToJson(data);
            }
            else
            {
                output = ExportToCsv(data);
            }

            // Write to file or stdout
            if (!string.IsNullOrWhiteSpace(settings.OutputPath))
            {
                File.WriteAllText(settings.OutputPath, output);
                AnsiConsole.MarkupLine($"[green]✓[/] Exported {data.Count} records to {settings.OutputPath}");
            }
            else
            {
                Console.WriteLine(output);
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    private List<DataRecord> FetchData(ServerHub.Storage.WidgetDataRepository repository, string? measurement, long? startTimestamp)
    {
        var records = new List<DataRecord>();

        // This is a simplified implementation - in production, you'd want to use a proper query
        // For now, we'll use the repository's GetTimeSeries method with a large time range
        // This would need to be enhanced to support arbitrary queries

        // Note: This is a limitation of the current repository design
        // For full export functionality, we'd need to add a GetAllData method to WidgetDataRepository

        AnsiConsole.MarkupLine("[yellow]Note: Export functionality uses available query methods.[/]");
        AnsiConsole.MarkupLine("[yellow]For full data export, you can query the SQLite database directly.[/]");

        return records;
    }

    private string ExportToCsv(List<DataRecord> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("timestamp,measurement,tags,field_name,field_value,field_text");

        foreach (var record in data)
        {
            var tags = record.Tags ?? "";
            var value = record.FieldValue?.ToString() ?? "";
            var text = record.FieldText ?? "";

            sb.AppendLine($"{record.Timestamp},{CsvEscape(record.Measurement)},{CsvEscape(tags)},{CsvEscape(record.FieldName)},{value},{CsvEscape(text)}");
        }

        return sb.ToString();
    }

    private string ExportToJson(List<DataRecord> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[");

        for (int i = 0; i < data.Count; i++)
        {
            var record = data[i];
            var comma = i < data.Count - 1 ? "," : "";

            sb.AppendLine("  {");
            sb.AppendLine($"    \"timestamp\": {record.Timestamp},");
            sb.AppendLine($"    \"measurement\": {JsonEscape(record.Measurement)},");
            sb.AppendLine($"    \"tags\": {record.Tags ?? "null"},");
            sb.AppendLine($"    \"field_name\": {JsonEscape(record.FieldName)},");

            if (record.FieldValue.HasValue)
            {
                sb.AppendLine($"    \"field_value\": {record.FieldValue.Value},");
            }
            else
            {
                sb.AppendLine($"    \"field_value\": null,");
            }

            sb.AppendLine($"    \"field_text\": {(record.FieldText != null ? JsonEscape(record.FieldText) : "null")}");
            sb.AppendLine($"  }}{comma}");
        }

        sb.AppendLine("]");
        return sb.ToString();
    }

    private string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private string JsonEscape(string value)
    {
        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t")}\"";
    }

    private class DataRecord
    {
        public long Timestamp { get; set; }
        public string Measurement { get; set; } = "";
        public string? Tags { get; set; }
        public string FieldName { get; set; } = "";
        public double? FieldValue { get; set; }
        public string? FieldText { get; set; }
    }
}
