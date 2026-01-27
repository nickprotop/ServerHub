namespace ServerHub.Marketplace;

/// <summary>
/// Configuration settings for the marketplace
/// </summary>
public class MarketplaceConfig
{
    /// <summary>
    /// Base URL for the registry repository
    /// </summary>
    public static string RegistryBaseUrl { get; set; } =
        "https://raw.githubusercontent.com/nickprotop/serverhub-registry/main";

    /// <summary>
    /// URL to the registry index file
    /// </summary>
    public static string RegistryIndexUrl => $"{RegistryBaseUrl}/docs/registry.json";

    /// <summary>
    /// Cache directory for marketplace data
    /// </summary>
    public static string CacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "serverhub", "marketplace");

    /// <summary>
    /// Cache time-to-live in hours
    /// </summary>
    public static int CacheTtlHours { get; set; } = 24;

    /// <summary>
    /// Whether to show warnings for unverified widgets
    /// </summary>
    public static bool ShowUnverifiedWarnings { get; set; } = true;

    /// <summary>
    /// Allowed URL patterns for widget downloads (security)
    /// </summary>
    public static readonly string[] AllowedUrlPatterns = new[]
    {
        "https://github.com/*/releases/download/*",
        "https://raw.githubusercontent.com/*"
    };
}
