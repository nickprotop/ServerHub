using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using ServerHub.Marketplace.Models;

namespace ServerHub.Marketplace.Services;

/// <summary>
/// Client for fetching and parsing marketplace registry data
/// </summary>
public class RegistryClient
{
    private readonly HttpClient _httpClient;
    private readonly MarketplaceCache _cache;
    private readonly IDeserializer _yamlDeserializer;

    public RegistryClient()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ServerHub-Marketplace/1.0");

        _cache = new MarketplaceCache();

        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Fetches the registry index (with caching)
    /// </summary>
    public async Task<RegistryIndex?> FetchRegistryIndexAsync(bool forceRefresh = false)
    {
        const string cacheKey = "registry_index";

        // Try cache first
        if (!forceRefresh)
        {
            var cached = _cache.Get<RegistryIndex>(cacheKey);
            if (cached != null)
            {
                return cached;
            }
        }

        try
        {
            var response = await _httpClient.GetStringAsync(MarketplaceConfig.RegistryIndexUrl);
            var index = JsonSerializer.Deserialize(response, MarketplaceJsonContext.Default.RegistryIndex);

            if (index != null)
            {
                _cache.Set(cacheKey, index);
            }

            return index;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Failed to fetch registry index: {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Failed to parse registry index: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Fetches a specific widget manifest
    /// </summary>
    public async Task<WidgetManifest?> FetchWidgetManifestAsync(string manifestUrl)
    {
        // Construct full URL
        var fullUrl = manifestUrl.StartsWith("http")
            ? manifestUrl
            : $"{MarketplaceConfig.RegistryBaseUrl}/{manifestUrl}";

        var cacheKey = $"manifest_{manifestUrl.Replace("/", "_")}";

        // Try cache first
        var cached = _cache.Get<WidgetManifest>(cacheKey);
        if (cached != null)
        {
            return cached;
        }

        try
        {
            var response = await _httpClient.GetStringAsync(fullUrl);
            var manifest = _yamlDeserializer.Deserialize<WidgetManifest>(response);

            if (manifest != null)
            {
                _cache.Set(cacheKey, manifest);
            }

            return manifest;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Failed to fetch widget manifest: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to parse widget manifest: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Searches for widgets by keyword
    /// </summary>
    public async Task<List<RegistryWidget>> SearchWidgetsAsync(string query)
    {
        var index = await FetchRegistryIndexAsync();
        if (index == null)
        {
            return new List<RegistryWidget>();
        }

        var lowerQuery = query.ToLower();

        return index.Widgets
            .Where(w =>
                w.Name.ToLower().Contains(lowerQuery) ||
                w.Description.ToLower().Contains(lowerQuery) ||
                w.Id.ToLower().Contains(lowerQuery) ||
                w.Category.ToLower().Contains(lowerQuery))
            .ToList();
    }

    /// <summary>
    /// Gets widgets by category
    /// </summary>
    public async Task<List<RegistryWidget>> GetWidgetsByCategoryAsync(string category)
    {
        var index = await FetchRegistryIndexAsync();
        if (index == null)
        {
            return new List<RegistryWidget>();
        }

        return index.Widgets
            .Where(w => w.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Gets a specific widget by ID
    /// </summary>
    public async Task<RegistryWidget?> GetWidgetByIdAsync(string widgetId)
    {
        var index = await FetchRegistryIndexAsync();
        if (index == null)
        {
            return null;
        }

        return index.Widgets
            .FirstOrDefault(w => w.Id.Equals(widgetId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Clears the local cache
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }
}
