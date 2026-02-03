using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace ServerHub.Commands.Settings;

/// <summary>
/// Settings for marketplace info command
/// </summary>
public class MarketplaceInfoSettings : CommandSettings
{
    [CommandArgument(0, "<widget-id>")]
    [Description("Widget ID to show details for")]
    public string WidgetId { get; set; } = string.Empty;

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(WidgetId))
        {
            return ValidationResult.Error("Widget ID is required");
        }

        return ValidationResult.Success();
    }
}
