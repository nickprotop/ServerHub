using System.Diagnostics;
using ServerHub.Marketplace.Models;

namespace ServerHub.Marketplace.Services;

/// <summary>
/// Checks for system command dependencies
/// </summary>
public class DependencyChecker
{
    /// <summary>
    /// Result of a dependency check
    /// </summary>
    public class DependencyCheckResult
    {
        public string Command { get; set; } = string.Empty;
        public bool Found { get; set; }
        public string? Path { get; set; }
        public bool IsOptional { get; set; }
    }

    /// <summary>
    /// Checks all dependencies for a widget
    /// </summary>
    public List<DependencyCheckResult> CheckDependencies(WidgetDependencies? dependencies)
    {
        var results = new List<DependencyCheckResult>();

        if (dependencies == null)
        {
            return results;
        }

        // Check required dependencies
        foreach (var cmd in dependencies.SystemCommands)
        {
            results.Add(CheckCommand(cmd, isOptional: false));
        }

        // Check optional dependencies
        foreach (var cmd in dependencies.Optional)
        {
            results.Add(CheckCommand(cmd, isOptional: true));
        }

        return results;
    }

    /// <summary>
    /// Checks if a single command is available
    /// </summary>
    public DependencyCheckResult CheckCommand(string command, bool isOptional = false)
    {
        var result = new DependencyCheckResult
        {
            Command = command,
            IsOptional = isOptional
        };

        try
        {
            // Use 'which' on Unix, 'where' on Windows
            var checkCommand = OperatingSystem.IsWindows() ? "where" : "which";

            var psi = new ProcessStartInfo
            {
                FileName = checkCommand,
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.Found = false;
                return result;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            result.Found = process.ExitCode == 0 && !string.IsNullOrEmpty(output);
            result.Path = result.Found ? output.Split('\n')[0].Trim() : null;
        }
        catch
        {
            result.Found = false;
        }

        return result;
    }

    /// <summary>
    /// Gets a human-readable summary of missing dependencies
    /// </summary>
    public static string GetMissingDependenciesSummary(List<DependencyCheckResult> results)
    {
        var missing = results.Where(r => !r.Found && !r.IsOptional).ToList();

        if (missing.Count == 0)
        {
            return string.Empty;
        }

        var summary = "Missing required dependencies:\n";
        foreach (var dep in missing)
        {
            summary += $"  - {dep.Command}\n";
        }

        return summary;
    }
}
