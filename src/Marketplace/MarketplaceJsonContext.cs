using System.Text.Json.Serialization;
using ServerHub.Marketplace.Models;

namespace ServerHub.Marketplace;

/// <summary>
/// JSON source generation context for marketplace models (required for trimmed builds)
/// </summary>
[JsonSerializable(typeof(RegistryIndex))]
[JsonSerializable(typeof(RegistryWidget))]
[JsonSerializable(typeof(WidgetManifest))]
[JsonSerializable(typeof(WidgetMetadata))]
[JsonSerializable(typeof(WidgetVersion))]
[JsonSerializable(typeof(WidgetArtifact))]
[JsonSerializable(typeof(WidgetDependencies))]
[JsonSerializable(typeof(WidgetConfigInfo))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class MarketplaceJsonContext : JsonSerializerContext
{
}
