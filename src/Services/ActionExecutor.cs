// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using ServerHub.Models;

namespace ServerHub.Services;

/// <summary>
/// Executes widget actions as shell commands
/// </summary>
public class ActionExecutor
{
    private const int DefaultTimeoutSeconds = 60;

    /// <summary>
    /// Executes an action and captures output
    /// </summary>
    /// <param name="action">Action to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Execution result with stdout, stderr, exit code, and duration</returns>
    public async Task<ActionResult> ExecuteAsync(
        WidgetAction action,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var process = new Process();

            // Cross-platform shell execution
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = $"/c {action.Command}";
            }
            else
            {
                process.StartInfo.FileName = "/bin/bash";
                process.StartInfo.Arguments = $"-c \"{action.Command.Replace("\"", "\\\"")}\"";
            }

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            // Start and read output concurrently
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var processTask = process.WaitForExitAsync(cancellationToken);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(DefaultTimeoutSeconds), cancellationToken);

            var completedTask = await Task.WhenAny(processTask, timeoutTask);

            // Check for cancellation first
            if (cancellationToken.IsCancellationRequested)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore kill errors
                }
                stopwatch.Stop();
                return new ActionResult
                {
                    ExitCode = -1,
                    Stdout = "",
                    Stderr = "Execution cancelled by user",
                    Duration = stopwatch.Elapsed
                };
            }

            if (completedTask == timeoutTask)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore kill errors
                }
                stopwatch.Stop();
                return new ActionResult
                {
                    ExitCode = -1,
                    Stdout = "",
                    Stderr = $"Command timed out after {DefaultTimeoutSeconds} seconds",
                    Duration = stopwatch.Elapsed
                };
            }

            var stdout = await outputTask;
            var stderr = await errorTask;
            stopwatch.Stop();

            return new ActionResult
            {
                ExitCode = process.ExitCode,
                Stdout = stdout ?? "",
                Stderr = stderr ?? "",
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new ActionResult
            {
                ExitCode = -1,
                Stdout = "",
                Stderr = "Execution cancelled by user",
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ActionResult
            {
                ExitCode = -1,
                Stdout = "",
                Stderr = $"Execution failed: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }
}
