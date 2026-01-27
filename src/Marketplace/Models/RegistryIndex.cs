using System.Text.Json.Serialization;

namespace ServerHub.Marketplace.Models;

/// <summary>
/// Represents the master registry index (registry.json)
/// </summary>
public class RegistryIndex
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("widgets")]
    public List<RegistryWidget> Widgets { get; set; } = new();
}

public class RegistryWidget
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("verification_level")]
    public string VerificationLevelString { get; set; } = "unverified";

    [JsonPropertyName("latest_version")]
    public string LatestVersion { get; set; } = string.Empty;

    [JsonPropertyName("manifest_url")]
    public string ManifestUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets the verification level as an enum
    /// </summary>
    public VerificationLevel VerificationLevel
    {
        get
        {
            return VerificationLevelString.ToLower() switch
            {
                "verified" => VerificationLevel.Verified,
                "community" => VerificationLevel.Community,
                _ => VerificationLevel.Unverified
            };
        }
    }
}
