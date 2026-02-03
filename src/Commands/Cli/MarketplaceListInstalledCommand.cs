using Spectre.Console;
using Spectre.Console.Cli;
using ServerHub.Utils;

namespace ServerHub.Commands.Cli;

/// <summary>
/// Marketplace list-installed command - shows installed marketplace widgets
/// </summary>
public class MarketplaceListInstalledCommand : AsyncCommand
{
    public override Task<int> ExecuteAsync(CommandContext context)
    {
        var installPath = WidgetPaths.GetMarketplaceInstallPath();

        if (!Directory.Exists(installPath))
        {
            AnsiConsole.MarkupLine("[yellow]No marketplace widgets installed.[/]");
            return Task.FromResult(0);
        }

        var files = Directory.GetFiles(installPath, "*", SearchOption.TopDirectoryOnly)
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.Name)
            .ToList();

        if (files.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No marketplace widgets installed.[/]");
            return Task.FromResult(0);
        }

        AnsiConsole.WriteLine($"\nInstalled marketplace widgets ({files.Count}):\n");

        var table = new Table();
        table.AddColumn("File");
        table.AddColumn("Size");
        table.AddColumn("Modified");

        foreach (var file in files)
        {
            table.AddRow(
                file.Name,
                MarketplaceHelpers.FormatFileSize(file.Length),
                file.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]Location: {installPath}[/]");

        return Task.FromResult(0);
    }
}
