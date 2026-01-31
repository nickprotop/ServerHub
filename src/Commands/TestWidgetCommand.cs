// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Text;
using ServerHub.Models;
using ServerHub.Services;
using Spectre.Console;
using YamlDotNet.Serialization;

namespace ServerHub.Commands;

/// <summary>
/// Command for testing widgets - validates protocol output and optionally shows UI preview
/// </summary>
public class TestWidgetCommand
{
    private readonly WidgetProtocolParser _parser;

    public TestWidgetCommand()
    {
        _parser = new WidgetProtocolParser();
    }

    public async Task<int> ExecuteAsync(string widgetPath, bool extended, bool uiMode, bool skipConfirmation)
    {
        if (uiMode)
        {
            return await RunUiPreviewAsync(widgetPath, extended, skipConfirmation);
        }

        return await RunCliTestAsync(widgetPath, extended, skipConfirmation);
    }

    private async Task<int> RunCliTestAsync(string widgetPath, bool extended, bool skipConfirmation)
    {
        AnsiConsole.MarkupLine($"[bold cyan]Testing widget:[/] {widgetPath}");
        AnsiConsole.WriteLine(new string('━', 80));
        AnsiConsole.WriteLine();

        // 1. Validate file exists
        if (!File.Exists(widgetPath))
        {
            AnsiConsole.MarkupLine("[red]✗ Widget file not found[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]✓ Widget file exists[/]");

        // 2. Show security warning and ask for permission (unless --yes flag used)
        if (!skipConfirmation)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow bold]⚠  Security Notice[/]");
            AnsiConsole.MarkupLine("[yellow]This will execute the widget script with minimal security restrictions.[/]");
            AnsiConsole.MarkupLine("[yellow]Only test widgets from trusted sources.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[dim]Script path:[/] {Path.GetFullPath(widgetPath)}");
            AnsiConsole.WriteLine();

            if (!AnsiConsole.Confirm("Execute this widget?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[yellow]Test cancelled.[/]");
                return 0;
            }
        }

        // 3. Execute widget directly without path validation (test mode)
        var args = extended ? "--extended" : "";
        var startTime = DateTime.UtcNow;

        AnsiConsole.WriteLine();
        // For testing, we execute scripts directly without security restrictions
        var result = await ExecuteWidgetDirectlyAsync(widgetPath, args);

        var executionTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

        // 3. Display execution status
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Execution Status:[/]");
        AnsiConsole.WriteLine(new string('━', 80));

        if (result.IsSuccess)
        {
            AnsiConsole.MarkupLine($"[green]✓ Widget executed successfully[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]✗ Widget execution failed: {result.ErrorMessage}[/]");
        }

        AnsiConsole.MarkupLine($"[green]✓ Execution time: {executionTime:F0}ms[/]");

        // 4. Parse output
        WidgetData? widgetData = null;
        var parseErrors = new List<string>();

        try
        {
            widgetData = _parser.Parse(result.Output ?? string.Empty);
            var validationErrors = _parser.GetValidationErrors();
            parseErrors.AddRange(validationErrors);
        }
        catch (Exception ex)
        {
            parseErrors.Add($"Parse exception: {ex.Message}");
        }

        // 5. Display parsed output
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Parsed Output:[/]");
        AnsiConsole.WriteLine(new string('━', 80));

        if (widgetData != null)
        {
            AnsiConsole.MarkupLine($"  [dim]Title:[/] {widgetData.Title ?? "(none)"}");
            AnsiConsole.MarkupLine($"  [dim]Refresh:[/] {widgetData.RefreshInterval} seconds");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  [dim]Rows ({widgetData.Rows.Count}):[/]");

            foreach (var row in widgetData.Rows)
            {
                var (statusColor, statusLabel) = row.Status?.State switch
                {
                    StatusState.Ok => ("green", "OK"),
                    StatusState.Info => ("blue", "INFO"),
                    StatusState.Warn => ("yellow", "WARN"),
                    StatusState.Error => ("red", "ERROR"),
                    _ => (null, null)
                };

                if (statusColor != null && statusLabel != null)
                {
                    AnsiConsole.Markup($"    [{statusColor}]{statusLabel}[/] ");
                    AnsiConsole.WriteLine(row.Content);
                }
                else
                {
                    AnsiConsole.WriteLine($"    {row.Content}");
                }

                if (row.Progress != null)
                    AnsiConsole.MarkupLine($"      [dim]PROGRESS: {row.Progress.Value}%[/]");
                if (row.Sparkline != null)
                    AnsiConsole.MarkupLine($"      [dim]SPARKLINE: {row.Sparkline.Values.Count} values[/]");
                if (row.Graph != null)
                    AnsiConsole.MarkupLine($"      [dim]GRAPH: {row.Graph.Label ?? "unlabeled"}[/]");
                if (row.Table != null)
                    AnsiConsole.MarkupLine($"      [dim]TABLE: {row.Table.Headers.Count} columns, {row.Table.Rows.Count} rows[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("  [red]Failed to parse widget output[/]");
        }

        // 6. Display actions
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]Actions ({widgetData?.Actions.Count ?? 0}):[/]");
        AnsiConsole.WriteLine(new string('━', 80));

        var actionErrors = new List<string>();

        if (widgetData == null || widgetData.Actions.Count == 0)
        {
            AnsiConsole.MarkupLine("  [dim](no actions)[/]");
        }
        else
        {
            foreach (var action in widgetData.Actions)
            {
                // Validate action
                if (string.IsNullOrWhiteSpace(action.Label))
                {
                    actionErrors.Add("Action has no label");
                    AnsiConsole.MarkupLine("  [red]✗ (no label)[/]");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(action.Command))
                {
                    actionErrors.Add($"Action '{action.Label}' has no command");
                    AnsiConsole.MarkupLine($"  [red]✗ {action.Label} (no command)[/]");
                    continue;
                }

                // Display action info
                var flags = new List<string>();
                if (action.Flags.Contains("danger")) flags.Add("[red]danger[/]");
                if (action.Flags.Contains("refresh")) flags.Add("[cyan]refresh[/]");
                if (action.Flags.Contains("sudo")) flags.Add("[yellow]sudo[/]");
                if (action.Timeout.HasValue) flags.Add($"[dim]timeout={action.Timeout}[/]");

                var flagsStr = flags.Count > 0 ? $" ({string.Join(", ", flags)})" : "";
                AnsiConsole.Markup($"  [green]✓[/] ");
                AnsiConsole.WriteLine($"{action.Label}{flagsStr}");
                AnsiConsole.Markup($"    [dim]Command:[/] ");
                AnsiConsole.WriteLine(action.Command);
            }
        }

        // 7. Protocol validation
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Protocol Validation:[/]");
        AnsiConsole.WriteLine(new string('━', 80));

        if (parseErrors.Count == 0 && actionErrors.Count == 0)
        {
            AnsiConsole.MarkupLine("  [green]✓ No protocol errors[/]");
            AnsiConsole.MarkupLine("  [green]✓ All markup valid[/]");
        }
        else
        {
            foreach (var error in parseErrors)
            {
                AnsiConsole.MarkupLine($"  [red]✗ {error}[/]");
            }
            foreach (var error in actionErrors)
            {
                AnsiConsole.MarkupLine($"  [red]✗ {error}[/]");
            }
        }

        // 8. Warnings
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Warnings:[/]");
        AnsiConsole.WriteLine(new string('━', 80));

        var warnings = new List<string>();

        if (widgetData != null)
        {
            bool hasErrorStatus = widgetData.Rows.Any(r => r.Status?.State == StatusState.Error);
            if (!hasErrorStatus && widgetData.Rows.Count > 0)
            {
                warnings.Add("No error status indicator found (consider adding health checks)");
            }

            if (widgetData.Actions.Count == 0)
            {
                warnings.Add("No actions defined (consider adding interactive actions)");
            }

            if (widgetData.Rows.Count < 2)
            {
                warnings.Add("Very few rows (consider adding more information)");
            }
        }

        if (warnings.Count == 0)
        {
            AnsiConsole.MarkupLine("  [dim](none)[/]");
        }
        else
        {
            foreach (var warning in warnings)
            {
                AnsiConsole.MarkupLine($"  [yellow]⚠[/] {warning}");
            }
        }

        // 9. Suggestions
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Suggestions:[/]");
        AnsiConsole.WriteLine(new string('━', 80));

        var suggestions = new List<string>();

        if (widgetData != null)
        {
            bool hasSparkline = widgetData.Rows.Any(r => r.Sparkline != null);
            bool hasGraph = widgetData.Rows.Any(r => r.Graph != null);
            bool hasProgress = widgetData.Rows.Any(r => r.Progress != null);
            bool hasTable = widgetData.Rows.Any(r => r.Table != null);

            if (!hasSparkline && !hasGraph)
            {
                suggestions.Add("Consider adding sparklines or graphs for trends");
            }

            if (!hasProgress && widgetData.Rows.Count > 3)
            {
                suggestions.Add("Consider using progress bars for percentage values");
            }

            if (!hasTable && widgetData.Rows.Count > 5)
            {
                suggestions.Add("Consider using tables for structured data");
            }
        }

        if (suggestions.Count == 0)
        {
            AnsiConsole.MarkupLine("  [dim](none)[/]");
        }
        else
        {
            foreach (var suggestion in suggestions)
            {
                AnsiConsole.MarkupLine($"  [cyan]→[/] {suggestion}");
            }
        }

        // 10. Show error message if present
        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Error Details:[/]");
            AnsiConsole.WriteLine(new string('━', 80));
            AnsiConsole.MarkupLine($"[red]{result.ErrorMessage}[/]");
        }

        // 11. Return exit code
        AnsiConsole.WriteLine();
        bool hasErrors = !result.IsSuccess ||
                       parseErrors.Count > 0 ||
                       actionErrors.Count > 0;

        if (hasErrors)
        {
            AnsiConsole.MarkupLine("[red bold]✗ Widget test FAILED[/]");
            return 1;
        }
        else
        {
            AnsiConsole.MarkupLine("[green bold]✓ Widget test PASSED[/]");
            return 0;
        }
    }

    /// <summary>
    /// Runs the UI preview mode - displays widget in a live TUI
    /// </summary>
    private async Task<int> RunUiPreviewAsync(string widgetPath, bool extended, bool skipConfirmation)
    {
        // 1. Validate file exists
        if (!File.Exists(widgetPath))
        {
            AnsiConsole.MarkupLine("[red]✗ Widget file not found[/]");
            return 1;
        }

        var fullPath = Path.GetFullPath(widgetPath);

        // 2. Show security warning and ask for permission (unless --yes flag used)
        if (!skipConfirmation)
        {
            AnsiConsole.MarkupLine("[yellow bold]⚠  Security Notice[/]");
            AnsiConsole.MarkupLine("[yellow]This will execute the widget script repeatedly in preview mode.[/]");
            AnsiConsole.MarkupLine("[yellow]Only preview widgets from trusted sources.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[dim]Script path:[/] {fullPath}");
            AnsiConsole.WriteLine();

            if (!AnsiConsole.Confirm("Launch preview?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[yellow]Preview cancelled.[/]");
                return 0;
            }
        }

        // 3. Create temporary config for test widget
        var testConfig = new ServerHubConfig
        {
            DefaultRefresh = 5,
            Widgets = new Dictionary<string, WidgetConfig>
            {
                ["test-widget"] = new WidgetConfig
                {
                    Path = fullPath,
                    Refresh = 5,
                    Location = null // Direct path, no search needed
                }
            },
            Layout = new LayoutConfig
            {
                Order = new List<string> { "test-widget" }
            }
        };

        // 4. Write temporary config file
        var tempConfigPath = Path.Combine(Path.GetTempPath(), $"serverhub-test-{Guid.NewGuid()}.yaml");
        try
        {
            var serializer = new YamlDotNet.Serialization.SerializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance)
                .Build();
            var yaml = serializer.Serialize(testConfig);
            await File.WriteAllTextAsync(tempConfigPath, yaml);

            // 5. Launch main dashboard with the test config
            ApplicationState.IsTestMode = true;
            try
            {
                var exitCode = await Program.Main(new[] { tempConfigPath, "--dev-mode" });
                return exitCode;
            }
            finally
            {
                ApplicationState.IsTestMode = false;
            }
        }
        finally
        {
            // Cleanup temp config
            if (File.Exists(tempConfigPath))
            {
                try { File.Delete(tempConfigPath); } catch { }
            }
        }
    }


    /// <summary>
    /// Executes a widget script directly for testing purposes (bypasses path validation)
    /// </summary>
    private async Task<ExecutionResult> ExecuteWidgetDirectlyAsync(string scriptPath, string? arguments = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"{scriptPath} {arguments ?? string.Empty}".Trim(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory
            };

            // Set minimal environment
            startInfo.Environment.Clear();
            startInfo.Environment["PATH"] = "/usr/local/bin:/usr/bin:/bin";
            startInfo.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            startInfo.Environment["USER"] = Environment.UserName;
            startInfo.Environment["LANG"] = "en_US.UTF-8";

            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait with 10 second timeout
            var completed = await process.WaitForExitAsync(TimeSpan.FromSeconds(10));

            if (!completed)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore errors during kill
                }

                return ExecutionResult.Failure("Script execution timed out after 10 seconds");
            }

            var output = outputBuilder.ToString();
            return ExecutionResult.Success(output);
        }
        catch (Exception ex)
        {
            return ExecutionResult.Failure($"Failed to execute script: {ex.Message}");
        }
    }
}

/// <summary>
/// Extension method for WaitForExitAsync with timeout
/// </summary>
public static class ProcessTestExtensions
{
    public static async Task<bool> WaitForExitAsync(this Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
