using Spectre.Console.Cli;
using System.ComponentModel;

namespace ServerHub.Commands.Settings.Marketplace;

/// <summary>
/// Settings for marketplace check-updates command
/// </summary>
public class CheckUpdatesSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Output results as JSON")]
    public bool JsonOutput { get; set; }
}
