using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace ServerHub.Commands.Settings;

/// <summary>
/// Settings for marketplace search command
/// </summary>
public class MarketplaceSearchSettings : CommandSettings
{
    [CommandArgument(0, "<query>")]
    [Description("Search query for widgets")]
    public string Query { get; set; } = string.Empty;

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Query))
        {
            return ValidationResult.Error("Search query is required");
        }

        return ValidationResult.Success();
    }
}
