// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using ServerHub.Models;

namespace ServerHub.Services;

/// <summary>
/// Executes widget actions as shell commands
/// </summary>
public class ActionExecutor
{
    private const int DefaultTimeoutSeconds = 60;
    private const int GracePeriodSeconds = 5;

    /// <summary>
    /// Executes an action and captures output
    /// </summary>
    /// <param name="action">Action to execute</param>
    /// <param name="cancellationToken">Cancellation token for termination</param>
    /// <param name="onProgressUpdate">Callback for progress updates (elapsed seconds)</param>
    /// <param name="onGracefulTerminate">Callback when SIGTERM is sent (graceful shutdown)</param>
    /// <param name="onForceKill">Callback when SIGKILL is sent (force kill)</param>
    /// <returns>Execution result with stdout, stderr, exit code, and duration</returns>
    public async Task<ActionResult> ExecuteAsync(
        WidgetAction action,
        CancellationToken cancellationToken = default,
        Action<int>? onProgressUpdate = null,
        Action? onGracefulTerminate = null,
        Action? onForceKill = null)
    {
        var stopwatch = Stopwatch.StartNew();
        Process? process = null;

        try
        {
            process = new Process();

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

            // Start process
            process.Start();

            // Background timer for progress updates
            var progressTimer = new System.Timers.Timer(1000); // 1 second interval
            var elapsedSeconds = 0;
            progressTimer.Elapsed += (s, e) =>
            {
                elapsedSeconds++;
                onProgressUpdate?.Invoke(elapsedSeconds);
            };
            progressTimer.Start();

            // Start reading output concurrently
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            // Wait for process completion with timeout and cancellation support
            var processExited = false;
            while (!processExited && elapsedSeconds < DefaultTimeoutSeconds)
            {
                // Check if process has exited
                if (process.HasExited)
                {
                    processExited = true;
                    break;
                }

                // Check for cancellation request
                if (cancellationToken.IsCancellationRequested)
                {
                    progressTimer.Stop();
                    await TerminateProcessGracefully(process, onGracefulTerminate, onForceKill);
                    stopwatch.Stop();

                    return new ActionResult
                    {
                        ExitCode = -1,
                        Stdout = "",
                        Stderr = "Execution terminated by user",
                        Duration = stopwatch.Elapsed
                    };
                }

                // Wait a bit before checking again
                await Task.Delay(100);
            }

            progressTimer.Stop();

            // Timeout handling
            if (!processExited)
            {
                await TerminateProcessGracefully(process, onGracefulTerminate, onForceKill);
                stopwatch.Stop();

                return new ActionResult
                {
                    ExitCode = -1,
                    Stdout = "",
                    Stderr = $"Command timed out after {DefaultTimeoutSeconds} seconds",
                    Duration = stopwatch.Elapsed
                };
            }

            // Process completed normally
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
            if (process != null && !process.HasExited)
            {
                await TerminateProcessGracefully(process, onGracefulTerminate, onForceKill);
            }

            stopwatch.Stop();
            return new ActionResult
            {
                ExitCode = -1,
                Stdout = "",
                Stderr = "Execution terminated by user",
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            if (process != null && !process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore kill errors
                }
            }

            stopwatch.Stop();
            return new ActionResult
            {
                ExitCode = -1,
                Stdout = "",
                Stderr = $"Execution failed: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>
    /// Terminates a process gracefully (SIGTERM) with automatic escalation to force kill (SIGKILL)
    /// </summary>
    private async Task TerminateProcessGracefully(
        Process process,
        Action? onGracefulTerminate,
        Action? onForceKill)
    {
        if (process.HasExited)
            return;

        try
        {
            // Step 1: Send SIGTERM (graceful shutdown)
            onGracefulTerminate?.Invoke();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Linux/macOS: Kill without tree kills just the parent (SIGTERM)
                process.Kill(entireProcessTree: false);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: Try to close gracefully first
                process.CloseMainWindow();
            }

            // Step 2: Wait grace period (5 seconds)
            var gracePeriodTask = Task.Delay(TimeSpan.FromSeconds(GracePeriodSeconds));
            var processExitTask = Task.Run(async () =>
            {
                while (!process.HasExited)
                {
                    await Task.Delay(100);
                }
            });

            var completedTask = await Task.WhenAny(gracePeriodTask, processExitTask);

            // Step 3: If still running after grace period, force kill (SIGKILL)
            if (!process.HasExited)
            {
                onForceKill?.Invoke();
                process.Kill(entireProcessTree: true); // Force kill entire tree
            }
        }
        catch
        {
            // If graceful termination fails, force kill immediately
            try
            {
                onForceKill?.Invoke();
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore errors during force kill
            }
        }
    }
}
