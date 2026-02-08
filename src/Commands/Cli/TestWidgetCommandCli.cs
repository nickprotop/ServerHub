namespace ServerHub.Commands.Cli;

/// <summary>
/// Test widget command - validates protocol output and optionally shows UI preview
/// </summary>
public class TestWidgetCommandCli
{
    public static async Task<int> ExecuteAsync(
        string scriptPath,
        bool extended,
        bool uiMode,
        bool skipConfirmation)
    {
        // Delegate to existing TestWidgetCommand logic
        var testCommand = new TestWidgetCommand();
        return await testCommand.ExecuteAsync(
            scriptPath,
            extended,
            uiMode,
            skipConfirmation
        );
    }
}
