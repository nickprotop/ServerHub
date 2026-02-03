using Spectre.Console.Cli;
using ServerHub.Commands.Settings;

namespace ServerHub.Commands.Cli;

/// <summary>
/// New widget command - interactive widget creation wizard
/// </summary>
public class NewWidgetCommandCli : AsyncCommand<NewWidgetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, NewWidgetSettings settings)
    {
        var newWidgetCommand = new NewWidgetCommand();

        // Build args array from settings
        var argsList = new List<string>();

        if (settings.ListTemplates)
        {
            argsList.Add("--list");
        }
        else
        {
            if (!string.IsNullOrEmpty(settings.TemplateName))
            {
                argsList.Add(settings.TemplateName);
            }

            if (!string.IsNullOrEmpty(settings.Name))
            {
                argsList.Add("--name");
                argsList.Add(settings.Name);
            }

            if (!string.IsNullOrEmpty(settings.OutputFile))
            {
                argsList.Add("--output");
                argsList.Add(settings.OutputFile);
            }

            // Add custom variables
            foreach (var (key, value) in settings.CustomVariables)
            {
                argsList.Add($"--{key.ToLower().Replace("_", "-")}");
                argsList.Add(value);
            }
        }

        return await newWidgetCommand.ExecuteAsync(argsList.ToArray());
    }
}
