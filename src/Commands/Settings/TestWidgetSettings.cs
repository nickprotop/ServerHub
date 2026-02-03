using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace ServerHub.Commands.Settings;

/// <summary>
/// Settings for test-widget command
/// </summary>
public class TestWidgetSettings : CommandSettings
{
    [CommandArgument(0, "<script>")]
    [Description("Path to widget script to test")]
    public string ScriptPath { get; set; } = string.Empty;

    [CommandOption("--extended")]
    [Description("Run extended tests with multiple cycles")]
    public bool Extended { get; set; }

    [CommandOption("--ui")]
    [Description("Show UI preview of widget output")]
    public bool UiMode { get; set; }

    [CommandOption("--yes|-y")]
    [Description("Skip confirmation prompts")]
    public bool SkipConfirmation { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(ScriptPath))
        {
            return ValidationResult.Error("Script path is required");
        }

        if (!File.Exists(ScriptPath))
        {
            return ValidationResult.Error($"Script file not found: {ScriptPath}");
        }

        return ValidationResult.Success();
    }
}
