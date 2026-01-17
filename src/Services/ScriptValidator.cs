// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using System.Text;
using ServerHub.Config;
using ServerHub.Utils;

namespace ServerHub.Services;

/// <summary>
/// Validates widget scripts for security before execution
/// Performs checksum validation, path restrictions, symlink detection, and file permission checks
/// </summary>
public class ScriptValidator
{
    private readonly Dictionary<string, string> _bundledChecksums;

    public ScriptValidator()
    {
        // Load hardcoded checksums from generated file
        _bundledChecksums = BundledWidgets.Checksums;
    }

    /// <summary>
    /// Validates a widget script for security
    /// </summary>
    /// <param name="scriptPath">Full path to the script</param>
    /// <param name="expectedChecksum">Optional expected SHA256 checksum</param>
    /// <returns>Validation result</returns>
    public ValidationResult Validate(string scriptPath, string? expectedChecksum = null)
    {
        // 1. Check if file exists
        if (!File.Exists(scriptPath))
        {
            return ValidationResult.Failure($"Script not found: {scriptPath}");
        }

        // 2. Resolve real path (detect symlinks)
        string realPath;
        try
        {
            var fileInfo = new FileInfo(scriptPath);
            realPath = fileInfo.FullName;

            // Check if it's a symlink
            if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return ValidationResult.Failure($"Symlinks are not allowed: {scriptPath}");
            }
        }
        catch (Exception ex)
        {
            return ValidationResult.Failure($"Failed to resolve path: {ex.Message}");
        }

        // 3. Check path restrictions (must be within widget search paths)
        if (!IsPathAllowed(realPath))
        {
            return ValidationResult.Failure($"Script path is not within allowed directories: {realPath}");
        }

        // 4. Check executable permissions (Unix-like systems)
        if (!IsExecutable(realPath))
        {
            return ValidationResult.Failure($"Script is not executable: {realPath}. Run: chmod +x {scriptPath}");
        }

        // 5. Checksum validation
        if (!string.IsNullOrEmpty(expectedChecksum))
        {
            var actualChecksum = CalculateChecksum(realPath);
            if (!string.Equals(actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Failure($"Checksum mismatch for {scriptPath}.\nExpected: {expectedChecksum}\nActual: {actualChecksum}");
            }
        }
        else
        {
            // Check if this is a bundled widget (should have checksum)
            var bundledPath = WidgetPaths.GetBundledWidgetsDirectory();
            if (realPath.StartsWith(bundledPath, StringComparison.Ordinal))
            {
                var relativePath = Path.GetRelativePath(bundledPath, realPath);
                if (_bundledChecksums.TryGetValue(relativePath, out var bundledChecksum))
                {
                    var actualChecksum = CalculateChecksum(realPath);
                    if (!string.Equals(actualChecksum, bundledChecksum, StringComparison.OrdinalIgnoreCase))
                    {
                        return ValidationResult.Failure($"Bundled widget checksum mismatch: {relativePath}");
                    }
                }
            }
        }

        return ValidationResult.Success(realPath);
    }

    /// <summary>
    /// Checks if a path is within allowed widget directories
    /// </summary>
    private bool IsPathAllowed(string path)
    {
        var normalizedPath = Path.GetFullPath(path);

        foreach (var searchPath in WidgetPaths.GetSearchPaths())
        {
            var normalizedSearchPath = Path.GetFullPath(searchPath);
            if (normalizedPath.StartsWith(normalizedSearchPath, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a file is executable (Unix-like systems)
    /// </summary>
    private bool IsExecutable(string path)
    {
        // On Windows, all files are "executable" via their extension
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        // On Unix-like systems, check execute permission
        try
        {
            var fileInfo = new FileInfo(path);
            var unixFileMode = File.GetUnixFileMode(path);

            // Check if owner, group, or others have execute permission
            return unixFileMode.HasFlag(UnixFileMode.UserExecute) ||
                   unixFileMode.HasFlag(UnixFileMode.GroupExecute) ||
                   unixFileMode.HasFlag(UnixFileMode.OtherExecute);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Calculates SHA256 checksum of a file
    /// </summary>
    public static string CalculateChecksum(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// Result of script validation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ValidatedPath { get; init; }

    public static ValidationResult Success(string validatedPath) =>
        new() { IsValid = true, ValidatedPath = validatedPath };

    public static ValidationResult Failure(string errorMessage) =>
        new() { IsValid = false, ErrorMessage = errorMessage };
}
