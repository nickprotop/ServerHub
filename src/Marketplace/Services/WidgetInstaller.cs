using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ServerHub.Marketplace.Models;
using ServerHub.Models;
using ServerHub.Services;
using ServerHub.Utils;

namespace ServerHub.Marketplace.Services;

/// <summary>
/// Handles downloading and installing marketplace widgets
/// </summary>
public class WidgetInstaller
{
    private readonly HttpClient _httpClient;
    private readonly RegistryClient _registryClient;
    private readonly string _installPath;

    public WidgetInstaller(RegistryClient registryClient, string installPath)
    {
        _registryClient = registryClient;
        _installPath = installPath;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ServerHub-Marketplace/1.0");
    }

    /// <summary>
    /// Result of an installation attempt
    /// </summary>
    public class InstallResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? InstalledPath { get; set; }
        public string? WidgetId { get; set; }
        public string? Sha256 { get; set; }
        public WidgetManifest? Manifest { get; set; }
    }

    /// <summary>
    /// Installs a widget from the marketplace
    /// </summary>
    public async Task<InstallResult> InstallWidgetAsync(
        string widgetId,
        string? specificVersion = null,
        bool skipDependencyCheck = false)
    {
        var result = new InstallResult();

        // Get widget info from registry
        var registryWidget = await _registryClient.GetWidgetByIdAsync(widgetId);
        if (registryWidget == null)
        {
            result.ErrorMessage = $"Widget '{widgetId}' not found in marketplace";
            return result;
        }

        // Fetch full manifest
        var manifest = await _registryClient.FetchWidgetManifestAsync(registryWidget.ManifestUrl);
        if (manifest == null)
        {
            result.ErrorMessage = $"Failed to fetch manifest for widget '{widgetId}'";
            return result;
        }

        result.Manifest = manifest;

        // Select version
        var version = specificVersion != null
            ? manifest.Versions.FirstOrDefault(v => v.Version == specificVersion)
            : manifest.LatestVersion;

        if (version == null)
        {
            result.ErrorMessage = specificVersion != null
                ? $"Version '{specificVersion}' not found for widget '{widgetId}'"
                : $"No versions available for widget '{widgetId}'";
            return result;
        }

        // Check dependencies
        if (!skipDependencyCheck && manifest.Dependencies != null)
        {
            var depChecker = new DependencyChecker();
            var depResults = depChecker.CheckDependencies(manifest.Dependencies);
            var missing = depResults.Where(r => !r.Found && !r.IsOptional).ToList();

            if (missing.Count > 0)
            {
                var missingList = string.Join(", ", missing.Select(m => m.Command));
                result.ErrorMessage = $"Missing required dependencies: {missingList}";
                return result;
            }
        }

        // Download and install artifacts
        if (version.Artifacts.Count == 0)
        {
            result.ErrorMessage = $"No artifacts defined for version {version.Version}";
            return result;
        }

        // For Phase 1, we only support single-file widgets
        var artifact = version.Artifacts[0];

        // Validate URL is from allowed domain
        if (!IsUrlAllowed(artifact.Url))
        {
            result.ErrorMessage = $"URL not from allowed domain: {artifact.Url}";
            return result;
        }

        // Download artifact
        byte[] data;
        try
        {
            data = await _httpClient.GetByteArrayAsync(artifact.Url);
        }
        catch (HttpRequestException ex)
        {
            result.ErrorMessage = $"Failed to download widget: {ex.Message}";
            return result;
        }

        // Verify SHA256
        var actualSha256 = ComputeSha256(data);
        if (!actualSha256.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            result.ErrorMessage = $"SHA256 checksum mismatch!\n" +
                                  $"Expected: {artifact.Sha256}\n" +
                                  $"Got: {actualSha256}";
            return result;
        }

        // Install to configured directory (custom path or default user widgets)
        Directory.CreateDirectory(_installPath);

        var installPath = Path.Combine(_installPath, artifact.Name);

        // Write file
        try
        {
            await File.WriteAllBytesAsync(installPath, data);

            // Make executable on Unix systems
            if (!OperatingSystem.IsWindows())
            {
                var chmod = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{installPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                chmod.Start();
                chmod.WaitForExit();
            }

            result.Success = true;
            result.InstalledPath = installPath;
            result.WidgetId = manifest.Metadata.Id;
            result.Sha256 = artifact.Sha256;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Failed to write widget file: {ex.Message}";
            return result;
        }

        return result;
    }

    /// <summary>
    /// Computes SHA256 hash of data
    /// </summary>
    private static string ComputeSha256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash).ToLower();
    }

    /// <summary>
    /// Checks if a URL is from an allowed domain
    /// </summary>
    private static bool IsUrlAllowed(string url)
    {
        foreach (var pattern in MarketplaceConfig.AllowedUrlPatterns)
        {
            // Convert glob pattern to regex
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                + "$";

            if (Regex.IsMatch(url, regexPattern, RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Uninstalls a widget from the marketplace
    /// </summary>
    /// <param name="widgetId">ID of the widget to uninstall</param>
    /// <param name="configPath">Path to the configuration file</param>
    /// <returns>Tuple of (success, message)</returns>
    public async Task<(bool success, string message)> UninstallWidgetAsync(string widgetId, string configPath)
    {
        try
        {
            // 1. Load config
            var configManager = new ConfigManager();
            var config = configManager.LoadConfig(configPath);

            if (!config.Widgets.TryGetValue(widgetId, out var widgetConfig))
            {
                return (false, $"Widget '{widgetId}' not found in configuration");
            }

            // 2. Check if it's a bundled widget (cannot uninstall)
            if (widgetConfig.Location == WidgetLocation.Bundled)
            {
                return (false, "Cannot uninstall bundled widgets");
            }

            // 3. Resolve widget file path
            var (widgetPath, actualLocation) = WidgetPaths.ResolveWidgetPathWithLocation(
                widgetConfig.Path,
                widgetConfig.Location);

            // Additional check: prevent uninstalling bundled widgets even if location is Auto
            if (actualLocation == WidgetLocation.Bundled)
            {
                return (false, "Cannot uninstall bundled widgets");
            }

            // 4. Delete widget file if it exists
            if (!string.IsNullOrEmpty(widgetPath) && File.Exists(widgetPath))
            {
                File.Delete(widgetPath);
            }

            // 5. Remove from config
            config.Widgets.Remove(widgetId);

            // 6. Remove from layout order if present
            if (config.Layout?.Order != null)
            {
                config.Layout.Order.Remove(widgetId);
            }

            // 7. Save updated config
            configManager.SaveConfig(config, configPath);

            return (true, $"Widget '{widgetId}' uninstalled successfully");
        }
        catch (Exception ex)
        {
            return (false, $"Uninstall failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets formatted installation summary
    /// </summary>
    public static string FormatInstallationSummary(InstallResult result, WidgetVersion version)
    {
        if (!result.Success)
        {
            return $"Installation failed: {result.ErrorMessage}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"✓ Successfully installed {result.WidgetId} v{version.Version}");
        sb.AppendLine($"  Location: {result.InstalledPath}");
        sb.AppendLine($"  SHA256: {result.Sha256}");
        sb.AppendLine();
        sb.AppendLine("Next steps:");
        sb.AppendLine("  1. Review the widget code if desired");
        sb.AppendLine("  2. Add to config.yaml or use 'serverhub config' to configure");
        sb.AppendLine("  3. Restart ServerHub or press F5 to load");

        return sb.ToString();
    }
}
