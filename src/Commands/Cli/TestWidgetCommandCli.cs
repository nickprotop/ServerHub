using Spectre.Console.Cli;
using ServerHub.Commands.Settings;

namespace ServerHub.Commands.Cli;

/// <summary>
/// Test widget command - validates protocol output and optionally shows UI preview
/// </summary>
public class TestWidgetCommandCli : AsyncCommand<TestWidgetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, TestWidgetSettings settings)
    {
        // Delegate to existing TestWidgetCommand logic
        var testCommand = new TestWidgetCommand();
        return await testCommand.ExecuteAsync(
            settings.ScriptPath,
            settings.Extended,
            settings.UiMode,
            settings.SkipConfirmation
        );
    }
}
