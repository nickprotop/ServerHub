using Spectre.Console.Cli;
using System.ComponentModel;

namespace ServerHub.Commands.Settings;

/// <summary>
/// Settings for the default command (run dashboard)
/// </summary>
public class DefaultCommandSettings : CommandSettings
{
    [CommandArgument(0, "[config]")]
    [Description("Path to configuration file (default: config.yaml)")]
    public string? ConfigPath { get; set; }

    [CommandOption("--widgets-path")]
    [Description("Override widget directory path")]
    public string? WidgetsPath { get; set; }

    [CommandOption("--dev-mode")]
    [Description("Enable development mode with hot-reload")]
    public bool DevMode { get; set; }

    [CommandOption("--discover")]
    [Description("Discover and list all available widgets, then exit")]
    public bool Discover { get; set; }

    [CommandOption("--verify-checksums")]
    [Description("Verify bundled widget checksums, then exit")]
    public bool VerifyChecksums { get; set; }

    [CommandOption("--init-config")]
    [Description("Initialize a new configuration file at specified path")]
    public string? InitConfig { get; set; }
}
