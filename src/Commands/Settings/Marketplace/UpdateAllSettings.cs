using Spectre.Console.Cli;
using System.ComponentModel;

namespace ServerHub.Commands.Settings.Marketplace;

/// <summary>
/// Settings for marketplace update-all command
/// </summary>
public class UpdateAllSettings : CommandSettings
{
    [CommandOption("--yes|-y")]
    [Description("Skip confirmation prompts")]
    public bool SkipConfirmation { get; set; }
}
