using YamlDotNet.Serialization;

namespace ServerHub.Marketplace.Models;

/// <summary>
/// Represents a widget manifest from the marketplace registry
/// </summary>
public class WidgetManifest
{
    [YamlMember(Alias = "schema_version")]
    public string SchemaVersion { get; set; } = "1.0";

    [YamlMember(Alias = "metadata")]
    public WidgetMetadata Metadata { get; set; } = new();

    [YamlMember(Alias = "versions")]
    public List<WidgetVersion> Versions { get; set; } = new();

    [YamlMember(Alias = "dependencies")]
    public WidgetDependencies? Dependencies { get; set; }

    [YamlMember(Alias = "config")]
    public WidgetConfigInfo? Config { get; set; }

    /// <summary>
    /// Gets the latest version from the versions list
    /// </summary>
    public WidgetVersion? LatestVersion => Versions
        .OrderByDescending(v => ParseVersion(v.Version))
        .FirstOrDefault();

    private static Version ParseVersion(string version)
    {
        // Remove 'v' prefix if present
        var cleanVersion = version.TrimStart('v');
        return Version.TryParse(cleanVersion, out var v) ? v : new Version(0, 0, 0);
    }
}

public class WidgetMetadata
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "author")]
    public string Author { get; set; } = string.Empty;

    [YamlMember(Alias = "homepage")]
    public string Homepage { get; set; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = string.Empty;

    [YamlMember(Alias = "category")]
    public string Category { get; set; } = string.Empty;

    [YamlMember(Alias = "tags")]
    public List<string> Tags { get; set; } = new();

    [YamlMember(Alias = "license")]
    public string License { get; set; } = string.Empty;

    [YamlMember(Alias = "verification_level")]
    public string VerificationLevelString { get; set; } = "unverified";

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

public class WidgetVersion
{
    [YamlMember(Alias = "version")]
    public string Version { get; set; } = string.Empty;

    [YamlMember(Alias = "released")]
    public DateTime Released { get; set; }

    [YamlMember(Alias = "min_serverhub_version")]
    public string MinServerHubVersion { get; set; } = "0.1.0";

    [YamlMember(Alias = "changelog")]
    public string Changelog { get; set; } = string.Empty;

    [YamlMember(Alias = "artifacts")]
    public List<WidgetArtifact> Artifacts { get; set; } = new();
}

public class WidgetArtifact
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "url")]
    public string Url { get; set; } = string.Empty;

    [YamlMember(Alias = "sha256")]
    public string Sha256 { get; set; } = string.Empty;
}

public class WidgetDependencies
{
    [YamlMember(Alias = "system_commands")]
    public List<string> SystemCommands { get; set; } = new();

    [YamlMember(Alias = "optional")]
    public List<string> Optional { get; set; } = new();
}

public class WidgetConfigInfo
{
    [YamlMember(Alias = "example")]
    public string Example { get; set; } = string.Empty;

    [YamlMember(Alias = "default_refresh")]
    public int DefaultRefresh { get; set; } = 10;

    [YamlMember(Alias = "default_expanded_refresh")]
    public int? DefaultExpandedRefresh { get; set; }
}
