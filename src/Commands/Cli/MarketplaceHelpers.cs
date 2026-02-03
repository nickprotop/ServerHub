using ServerHub.Marketplace.Models;

namespace ServerHub.Commands.Cli;

/// <summary>
/// Helper methods for marketplace commands
/// </summary>
public static class MarketplaceHelpers
{
    public static string GetVerificationBadge(VerificationLevel level)
    {
        return level switch
        {
            VerificationLevel.Verified => "[green]✓ Verified[/]",
            VerificationLevel.Community => "[yellow]⚡ Community[/]",
            VerificationLevel.Unverified => "[red]⚠ Unverified[/]",
            _ => "[dim]Unknown[/]"
        };
    }

    public static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.#} {sizes[order]}";
    }
}
