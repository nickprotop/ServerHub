// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Text;

namespace ServerHub.Services;

/// <summary>
/// Executes widget scripts with security restrictions
/// Enforces timeout, minimal environment, and captures output
/// </summary>
public class ScriptExecutor
{
    private const int DefaultTimeoutSeconds = 10;
    private readonly ScriptValidator _validator;

    public ScriptExecutor(ScriptValidator validator)
    {
        _validator = validator;
    }

    /// <summary>
    /// Executes a widget script and returns its output
    /// </summary>
    /// <param name="scriptPath">Path to the script</param>
    /// <param name="arguments">Optional arguments to pass to the script</param>
    /// <param name="expectedChecksum">Optional SHA256 checksum for validation</param>
    /// <param name="timeoutSeconds">Timeout in seconds (default 10)</param>
    /// <returns>Execution result with output or error</returns>
    public async Task<ExecutionResult> ExecuteAsync(
        string scriptPath,
        string? arguments = null,
        string? expectedChecksum = null,
        int timeoutSeconds = DefaultTimeoutSeconds)
    {
        // 1. Validate script before execution
        var validationResult = _validator.Validate(scriptPath, expectedChecksum);
        if (!validationResult.IsValid)
        {
            return ExecutionResult.Failure(validationResult.ErrorMessage ?? "Validation failed");
        }

        var validatedPath = validationResult.ValidatedPath!;

        // 2. Prepare process
        var startInfo = new ProcessStartInfo
        {
            FileName = validatedPath,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(validatedPath) ?? Environment.CurrentDirectory
        };

        // 3. Set minimal environment (security: no user environment leakage)
        startInfo.Environment.Clear();
        startInfo.Environment["PATH"] = "/usr/local/bin:/usr/bin:/bin";
        startInfo.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        startInfo.Environment["USER"] = Environment.UserName;
        startInfo.Environment["LANG"] = "en_US.UTF-8";

        // 4. Execute with timeout
        try
        {
            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait with timeout
            var completed = await process.WaitForExitAsync(
                TimeSpan.FromSeconds(timeoutSeconds)
            );

            if (!completed)
            {
                // Timeout - kill the process
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore errors during kill
                }

                return ExecutionResult.Failure($"Script execution timed out after {timeoutSeconds} seconds");
            }

            // Check exit code
            if (process.ExitCode != 0)
            {
                var errorOutput = errorBuilder.ToString().Trim();
                return ExecutionResult.Failure(
                    $"Script exited with code {process.ExitCode}" +
                    (string.IsNullOrEmpty(errorOutput) ? "" : $": {errorOutput}")
                );
            }

            var output = outputBuilder.ToString();
            return ExecutionResult.Success(output);
        }
        catch (Exception ex)
        {
            return ExecutionResult.Failure($"Failed to execute script: {ex.Message}");
        }
    }
}

/// <summary>
/// Extension method for WaitForExitAsync with timeout
/// </summary>
public static class ProcessExtensions
{
    public static async Task<bool> WaitForExitAsync(this Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

/// <summary>
/// Result of script execution
/// </summary>
public class ExecutionResult
{
    public bool IsSuccess { get; init; }
    public string? Output { get; init; }
    public string? ErrorMessage { get; init; }

    public static ExecutionResult Success(string output) =>
        new() { IsSuccess = true, Output = output };

    public static ExecutionResult Failure(string errorMessage) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage };
}
