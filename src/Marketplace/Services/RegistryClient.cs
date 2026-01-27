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
    private readonly IDeserializer _yamlDeserializer;

    public RegistryClient()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ServerHub-Marketplace/1.0");

        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Fetches the registry index (always fresh)
    /// </summary>
    public async Task<RegistryIndex?> FetchRegistryIndexAsync()
    {
        try
        {
            var response = await _httpClient.GetStringAsync(MarketplaceConfig.RegistryIndexUrl);
            var index = JsonSerializer.Deserialize(response, MarketplaceJsonContext.Default.RegistryIndex);
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
    /// Fetches a specific widget manifest (always fresh)
    /// </summary>
    public async Task<WidgetManifest?> FetchWidgetManifestAsync(string manifestUrl)
    {
        // Construct full URL
        var fullUrl = manifestUrl.StartsWith("http")
            ? manifestUrl
            : $"{MarketplaceConfig.RegistryBaseUrl}/{manifestUrl}";

        try
        {
            var response = await _httpClient.GetStringAsync(fullUrl);
            var manifest = _yamlDeserializer.Deserialize<WidgetManifest>(response);
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

}
