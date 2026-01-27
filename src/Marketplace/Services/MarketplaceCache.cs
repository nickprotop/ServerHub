using System.Text.Json;
using ServerHub.Marketplace.Models;

namespace ServerHub.Marketplace.Services;

/// <summary>
/// Manages local caching of marketplace data
/// </summary>
public class MarketplaceCache
{
    private readonly string _cacheDir;
    private readonly TimeSpan _ttl;

    public MarketplaceCache()
    {
        _cacheDir = MarketplaceConfig.CacheDirectory;
        _ttl = TimeSpan.FromHours(MarketplaceConfig.CacheTtlHours);

        // Ensure cache directory exists
        if (!Directory.Exists(_cacheDir))
        {
            Directory.CreateDirectory(_cacheDir);
        }
    }

    /// <summary>
    /// Gets a cached value if it exists and is not expired
    /// </summary>
    public T? Get<T>(string key) where T : class
    {
        var cachePath = GetCachePath(key);
        if (!File.Exists(cachePath))
        {
            return null;
        }

        var fileInfo = new FileInfo(cachePath);
        if (DateTime.UtcNow - fileInfo.LastWriteTimeUtc > _ttl)
        {
            // Cache expired
            File.Delete(cachePath);
            return null;
        }

        try
        {
            var json = File.ReadAllText(cachePath);

            // Use source-generated JSON context for known types
            if (typeof(T) == typeof(RegistryIndex))
            {
                return JsonSerializer.Deserialize(json, MarketplaceJsonContext.Default.RegistryIndex) as T;
            }
            else if (typeof(T) == typeof(WidgetManifest))
            {
                return JsonSerializer.Deserialize(json, MarketplaceJsonContext.Default.WidgetManifest) as T;
            }

            // Fallback for other types (shouldn't happen)
            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            // Corrupted cache file
            File.Delete(cachePath);
            return null;
        }
    }

    /// <summary>
    /// Sets a value in the cache
    /// </summary>
    public void Set<T>(string key, T value) where T : class
    {
        var cachePath = GetCachePath(key);

        string json;
        // Use source-generated JSON context for known types
        if (value is RegistryIndex registryIndex)
        {
            json = JsonSerializer.Serialize(registryIndex, MarketplaceJsonContext.Default.RegistryIndex);
        }
        else if (value is WidgetManifest widgetManifest)
        {
            json = JsonSerializer.Serialize(widgetManifest, MarketplaceJsonContext.Default.WidgetManifest);
        }
        else
        {
            // Fallback for other types (shouldn't happen)
            json = JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        File.WriteAllText(cachePath, json);
    }

    /// <summary>
    /// Clears all cached data
    /// </summary>
    public void Clear()
    {
        if (Directory.Exists(_cacheDir))
        {
            foreach (var file in Directory.GetFiles(_cacheDir))
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>
    /// Checks if a cache entry exists and is valid
    /// </summary>
    public bool IsValid(string key)
    {
        var cachePath = GetCachePath(key);
        if (!File.Exists(cachePath))
        {
            return false;
        }

        var fileInfo = new FileInfo(cachePath);
        return DateTime.UtcNow - fileInfo.LastWriteTimeUtc <= _ttl;
    }

    private string GetCachePath(string key)
    {
        // Sanitize key to be filesystem-safe
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_cacheDir, $"{safeKey}.json");
    }
}
