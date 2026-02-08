namespace ServerHub.Commands.Cli;

/// <summary>
/// New widget command - interactive widget creation wizard
/// </summary>
public class NewWidgetCommandCli
{
    public static async Task<int> ExecuteAsync(
        string? templateName,
        string? name,
        string? outputFile,
        bool listTemplates)
    {
        var newWidgetCommand = new NewWidgetCommand();

        // Build args array from parameters
        var argsList = new List<string>();

        if (listTemplates)
        {
            argsList.Add("--list");
        }
        else
        {
            if (!string.IsNullOrEmpty(templateName))
            {
                argsList.Add(templateName);
            }

            if (!string.IsNullOrEmpty(name))
            {
                argsList.Add("--name");
                argsList.Add(name);
            }

            if (!string.IsNullOrEmpty(outputFile))
            {
                argsList.Add("--output");
                argsList.Add(outputFile);
            }
        }

        return await newWidgetCommand.ExecuteAsync(argsList.ToArray());
    }
}
