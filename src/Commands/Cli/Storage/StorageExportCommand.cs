// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Spectre.Console;
using ServerHub.Services;
using System.Text;

namespace ServerHub.Commands.Cli.Storage;

/// <summary>
/// Command to export widget data to CSV or JSON.
/// </summary>
public class StorageExportCommand
{
    public static int Execute(string? widgetId, string? outputPath, string format, string? configPath)
    {
        try
        {
            // Load configuration
            var configManager = new ConfigManager();
            var resolvedConfigPath = configPath ?? ConfigManager.GetDefaultConfigPath();
            var config = configManager.LoadConfig(resolvedConfigPath);

            if (config.Storage?.Enabled != true)
            {
                AnsiConsole.MarkupLine("[yellow]Storage is disabled in configuration[/]");
                return 1;
            }

            // Initialize storage service
            var storageService = ServerHub.Storage.StorageService.Initialize(config.Storage);
            var repository = storageService.GetRepository(widgetId!);

            // Fetch data (simplified implementation - in production you'd want proper query support)
            var data = new List<DataRecord>();

            if (data.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No data found for the specified criteria[/]");
                return 0;
            }

            // Export to format
            string output;
            if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                output = ExportToJson(data);
            }
            else
            {
                output = ExportToCsv(data);
            }

            // Write to file or stdout
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                File.WriteAllText(outputPath, output);
                AnsiConsole.MarkupLine($"[green]✓[/] Exported {data.Count} records to {outputPath}");
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

    private static string ExportToCsv(List<DataRecord> data)
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

    private static string ExportToJson(List<DataRecord> data)
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

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private static string JsonEscape(string value)
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
