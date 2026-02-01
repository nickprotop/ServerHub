// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ServerHub.Commands.Models;
using ServerHub.Commands.Services;
using Spectre.Console;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ServerHub.Commands;

/// <summary>
/// JSON source generation context for template types (enables trimming)
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(TemplateIndex))]
internal partial class TemplateJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Command for creating new widgets from GitHub-hosted templates
/// </summary>
public class NewWidgetCommand
{
    private const string TemplateBaseUrl = "https://raw.githubusercontent.com/nickprotop/ServerHub/main/templates/";
    private readonly HttpClient _httpClient;
    private readonly TemplateSubstitution _substitution;
    private readonly IDeserializer _yamlDeserializer;

    public NewWidgetCommand()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _substitution = new TemplateSubstitution();
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
    }

    public async Task<int> ExecuteAsync(string[] args)
    {
        var options = ParseArguments(args);

        if (options.ShowList)
            return await ListTemplatesAsync();

        if (options.TemplateName != null)
            return await CreateFromTemplateAsync(options);

        // No arguments - interactive mode
        return await InteractiveModeAsync();
    }

    private CommandOptions ParseArguments(string[] args)
    {
        var options = new CommandOptions();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "list" || arg == "--list")
            {
                options.ShowList = true;
                return options;
            }

            if (arg == "--help" || arg == "-h")
            {
                ShowHelp();
                options.ShowHelp = true;
                return options;
            }

            if (arg == "--name" && i + 1 < args.Length)
            {
                options.Variables["WIDGET_NAME"] = args[++i];
            }
            else if (arg == "--output" && i + 1 < args.Length)
            {
                options.OutputFile = args[++i];
            }
            else if (arg.StartsWith("--"))
            {
                // Custom variable (e.g., --title "My Widget")
                var varName = arg.Substring(2).ToUpperInvariant().Replace("-", "_");
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    options.Variables[varName] = args[++i];
                }
            }
            else if (options.TemplateName == null && !arg.StartsWith("-"))
            {
                // First non-flag argument is template name
                options.TemplateName = arg;
            }
        }

        return options;
    }

    private void ShowHelp()
    {
        AnsiConsole.WriteLine("ServerHub Widget Template Creator");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Usage:");
        AnsiConsole.WriteLine("  serverhub new-widget                         Interactive widget creation wizard");
        AnsiConsole.WriteLine("  serverhub new-widget list                    List available templates");
        AnsiConsole.WriteLine("  serverhub new-widget <template> [options]    Create from specific template");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Options:");
        AnsiConsole.WriteLine("  --name <name>                                Widget name (identifier)");
        AnsiConsole.WriteLine("  --output <path>                              Output file path");
        AnsiConsole.WriteLine("  --<variable> <value>                         Set template variable");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Examples:");
        AnsiConsole.WriteLine("  serverhub new-widget bash-basic --name cpu-monitor --refresh 5");
        AnsiConsole.WriteLine("  serverhub new-widget python-advanced --name disk-check --output ~/widgets/disk.py");
    }

    private async Task<int> ListTemplatesAsync()
    {
        var index = await FetchTemplateIndexAsync();
        if (index == null)
            return 1;

        AnsiConsole.MarkupLine("[bold cyan]Available Widget Templates[/]");
        AnsiConsole.WriteLine();

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("[bold]Template ID[/]");
        table.AddColumn("[bold]Name[/]");
        table.AddColumn("[bold]Language[/]");
        table.AddColumn("[bold]Difficulty[/]");
        table.AddColumn("[bold]Description[/]");

        foreach (var template in index.Templates)
        {
            var difficultyColor = template.Difficulty switch
            {
                "beginner" => "green",
                "intermediate" => "yellow",
                "advanced" => "red",
                _ => "white"
            };

            table.AddRow(
                $"[cyan]{template.Id}[/]",
                template.Name,
                template.Language,
                $"[{difficultyColor}]{template.Difficulty}[/]",
                $"[dim]{template.Description}[/]"
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]Use 'serverhub new-widget <template-id>' to create a widget[/]");

        return 0;
    }

    private async Task<int> CreateFromTemplateAsync(CommandOptions options)
    {
        if (options.TemplateName == null)
        {
            AnsiConsole.MarkupLine("[red]Error: Template name is required[/]");
            return 1;
        }

        // Fetch template metadata
        var metadata = await FetchTemplateMetadataAsync(options.TemplateName);
        if (metadata == null)
            return 1;

        AnsiConsole.MarkupLine($"[bold cyan]Creating widget from template:[/] {metadata.DisplayName}");
        AnsiConsole.WriteLine();

        // Collect variable values (from options or prompt)
        var variables = new Dictionary<string, string>();

        foreach (var (varName, varDef) in metadata.Variables)
        {
            // Check if already provided via command line
            if (options.Variables.ContainsKey(varName))
            {
                variables[varName] = options.Variables[varName];
                continue;
            }

            // Prompt for value
            string? value = PromptForVariableValue(varName, varDef, variables);
            if (value == null && varDef.Required)
            {
                AnsiConsole.MarkupLine("[red]Error: Required variable not provided[/]");
                return 1;
            }

            if (!string.IsNullOrEmpty(value))
                variables[varName] = value;
        }

        // Determine output file
        var outputFile = DetermineOutputFile(options.OutputFile, variables, metadata.Language);

        // Fetch and substitute template content
        var templateContent = await FetchTemplateContentAsync(options.TemplateName, metadata.TemplateFile);
        if (templateContent == null)
            return 1;

        var substituted = _substitution.Substitute(templateContent, variables, outputFile);

        // Write file
        await File.WriteAllTextAsync(outputFile, substituted);

        // Set executable permission (Unix)
        if (!OperatingSystem.IsWindows())
        {
            await SetExecutablePermissionAsync(outputFile);
        }

        // Display success and post-creation instructions
        DisplaySuccess(outputFile, metadata, variables);

        return 0;
    }

    private async Task<int> InteractiveModeAsync()
    {
        AnsiConsole.MarkupLine("[bold cyan]ServerHub Widget Creator[/]");
        AnsiConsole.WriteLine();

        // 1. Fetch templates
        var index = await FetchTemplateIndexAsync();
        if (index == null)
            return 1;

        // 2. Template selection
        var template = AnsiConsole.Prompt(
            new SelectionPrompt<TemplateInfo>()
                .Title("Select a [green]widget template[/]:")
                .PageSize(10)
                .AddChoices(index.Templates)
                .UseConverter(t => $"{t.Name} - {t.Description} ({t.Language})")
        );

        // 3. Fetch template metadata
        var metadata = await FetchTemplateMetadataAsync(template.Id);
        if (metadata == null)
            return 1;

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]Template:[/] {metadata.DisplayName}");
        AnsiConsole.MarkupLine($"[dim]{metadata.Description}[/]");
        AnsiConsole.WriteLine();

        // 4. Collect variable values
        var variables = new Dictionary<string, string>();

        foreach (var (varName, varDef) in metadata.Variables)
        {
            string? value = null;

            while (value == null)
            {
                value = PromptForVariableValue(varName, varDef, variables);

                // Use default if empty
                if (string.IsNullOrWhiteSpace(value) && !string.IsNullOrEmpty(varDef.Default))
                {
                    value = _substitution.Substitute(varDef.Default, variables);
                }

                // Validate required
                if (varDef.Required && string.IsNullOrWhiteSpace(value))
                {
                    AnsiConsole.MarkupLine("[red]This field is required[/]");
                    value = null;
                    continue;
                }

                // Validate pattern
                if (!string.IsNullOrWhiteSpace(value) && !string.IsNullOrEmpty(varDef.Pattern))
                {
                    if (!Regex.IsMatch(value, varDef.Pattern))
                    {
                        AnsiConsole.MarkupLine($"[red]Invalid format. Pattern: {varDef.Pattern}[/]");
                        value = null;
                        continue;
                    }
                }
            }

            variables[varName] = value;
        }

        // 5. Determine output file
        var outputFile = DetermineOutputFile(null, variables, metadata.Language);

        outputFile = AnsiConsole.Prompt(
            new TextPrompt<string>("[yellow]Output file path:[/]")
                .DefaultValue(outputFile)
                .AllowEmpty()
        );

        if (string.IsNullOrWhiteSpace(outputFile))
        {
            var extension = GetFileExtension(metadata.Language);
            outputFile = $"{variables.GetValueOrDefault("WIDGET_NAME", "widget")}{extension}";
        }

        // 6. Fetch template content and substitute
        var templateContent = await FetchTemplateContentAsync(template.Id, metadata.TemplateFile);
        if (templateContent == null)
            return 1;

        var substituted = _substitution.Substitute(templateContent, variables, outputFile);

        // 7. Preview (optional)
        if (AnsiConsole.Confirm("Preview generated content?", defaultValue: false))
        {
            AnsiConsole.WriteLine();
            var panel = new Panel(substituted)
                .Header("Preview")
                .BorderColor(Color.Cyan1);
            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();
        }

        // 8. Confirm creation
        if (!AnsiConsole.Confirm($"Create widget at [cyan]{outputFile}[/]?", defaultValue: true))
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled[/]");
            return 0;
        }

        // 9. Write file
        await File.WriteAllTextAsync(outputFile, substituted);

        // 10. Set executable permission (Unix)
        if (!OperatingSystem.IsWindows())
        {
            await SetExecutablePermissionAsync(outputFile);
        }

        // 11. Display success and post-creation instructions
        DisplaySuccess(outputFile, metadata, variables);

        return 0;
    }

    private async Task<TemplateIndex?> FetchTemplateIndexAsync()
    {
        try
        {
            var url = $"{TemplateBaseUrl}index.json";
            var response = await _httpClient.GetStringAsync(url);
            return JsonSerializer.Deserialize<TemplateIndex>(response, TemplateJsonContext.Default.TemplateIndex);
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: Failed to fetch template index from GitHub[/]");
            AnsiConsole.MarkupLine($"[dim]{ex.Message}[/]");
            return null;
        }
        catch (JsonException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: Failed to parse template index[/]");
            AnsiConsole.MarkupLine($"[dim]{ex.Message}[/]");
            return null;
        }
    }

    private async Task<TemplateMetadata?> FetchTemplateMetadataAsync(string templateId)
    {
        try
        {
            var url = $"{TemplateBaseUrl}{templateId}/template.yaml";
            var response = await _httpClient.GetStringAsync(url);
            return _yamlDeserializer.Deserialize<TemplateMetadata>(response);
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: Template '{templateId}' not found[/]");
            AnsiConsole.MarkupLine($"[dim]{ex.Message}[/]");
            return null;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: Failed to parse template metadata[/]");
            AnsiConsole.MarkupLine($"[dim]{ex.Message}[/]");
            return null;
        }
    }

    private async Task<string?> FetchTemplateContentAsync(string templateId, string fileName)
    {
        try
        {
            var url = $"{TemplateBaseUrl}{templateId}/{fileName}";
            return await _httpClient.GetStringAsync(url);
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: Failed to fetch template file '{fileName}'[/]");
            AnsiConsole.MarkupLine($"[dim]{ex.Message}[/]");
            return null;
        }
    }

    private string? PromptForVariableValue(string varName, TemplateVariable varDef, Dictionary<string, string> existingVariables)
    {
        // Build prompt text with example if available - escape user text to prevent markup issues
        var promptText = $"[yellow]{Markup.Escape(varDef.Description)}[/]";
        if (!string.IsNullOrEmpty(varDef.Example))
        {
            promptText += $" [dim](e.g., {Markup.Escape(varDef.Example)})[/dim]";
        }

        var prompt = new TextPrompt<string>(promptText);

        if (!string.IsNullOrEmpty(varDef.Default))
        {
            // Substitute existing variables in default
            var defaultValue = _substitution.Substitute(varDef.Default, existingVariables);
            prompt.DefaultValue(defaultValue);
            prompt.AllowEmpty();
        }
        else if (!varDef.Required)
        {
            prompt.AllowEmpty();
        }

        return AnsiConsole.Prompt(prompt);
    }

    private string DetermineOutputFile(string? providedPath, Dictionary<string, string> variables, string language)
    {
        if (!string.IsNullOrEmpty(providedPath))
            return providedPath;

        var extension = GetFileExtension(language);
        var widgetName = variables.GetValueOrDefault("WIDGET_NAME", "widget");
        return $"{widgetName}{extension}";
    }

    private string GetFileExtension(string language)
    {
        return language switch
        {
            "bash" => ".sh",
            "python" => ".py",
            "csharp" => ".csx",
            "powershell" => ".ps1",
            _ => ".sh"
        };
    }

    private async Task SetExecutablePermissionAsync(string filePath)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();
        }
        catch
        {
            // Silently ignore chmod errors
        }
    }

    private void DisplaySuccess(string outputFile, TemplateMetadata metadata, Dictionary<string, string> variables)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]✓ Widget created successfully:[/] {outputFile}");

        if (metadata.PostCreationInstructions.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Next steps:[/]");
            foreach (var instruction in metadata.PostCreationInstructions)
            {
                var substitutedInstruction = _substitution.Substitute(instruction, variables, outputFile);
                AnsiConsole.MarkupLine($"  • {substitutedInstruction}");
            }
        }
    }

    private class CommandOptions
    {
        public bool ShowList { get; set; }
        public bool ShowHelp { get; set; }
        public string? TemplateName { get; set; }
        public string? OutputFile { get; set; }
        public Dictionary<string, string> Variables { get; set; } = new();
    }
}
