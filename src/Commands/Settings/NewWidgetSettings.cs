using Spectre.Console.Cli;
using System.ComponentModel;

namespace ServerHub.Commands.Settings;

/// <summary>
/// Settings for new-widget command
/// </summary>
public class NewWidgetSettings : CommandSettings
{
    [CommandArgument(0, "[template]")]
    [Description("Template name (optional, use --list to see available templates)")]
    public string? TemplateName { get; set; }

    [CommandOption("--name")]
    [Description("Widget name")]
    public string? Name { get; set; }

    [CommandOption("--output|-o")]
    [Description("Output file path")]
    public string? OutputFile { get; set; }

    [CommandOption("--list")]
    [Description("List available templates and exit")]
    public bool ListTemplates { get; set; }

    // Store remaining custom variable assignments
    public Dictionary<string, string> CustomVariables { get; set; } = new();
}
