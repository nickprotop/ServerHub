using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace ServerHub.Commands.Settings;

/// <summary>
/// Settings for marketplace install command
/// </summary>
public class MarketplaceInstallSettings : CommandSettings
{
    [CommandArgument(0, "<widget-id>")]
    [Description("Widget ID to install")]
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
