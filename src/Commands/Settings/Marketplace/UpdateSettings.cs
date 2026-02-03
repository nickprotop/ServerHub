using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace ServerHub.Commands.Settings.Marketplace;

/// <summary>
/// Settings for marketplace update command
/// </summary>
public class UpdateSettings : CommandSettings
{
    [CommandArgument(0, "<widget-id>")]
    [Description("Widget ID to update")]
    public string WidgetId { get; set; } = string.Empty;

    [CommandOption("--version")]
    [Description("Specific version to install (defaults to latest)")]
    public string? Version { get; set; }

    [CommandOption("--yes|-y")]
    [Description("Skip confirmation prompts")]
    public bool SkipConfirmation { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(WidgetId))
        {
            return ValidationResult.Error("Widget ID is required");
        }

        return ValidationResult.Success();
    }
}
