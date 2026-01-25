// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using ServerHub.Services;

namespace ServerHub.Utils;

/// <summary>
/// Provides deep cloning utilities for objects.
/// Uses YAML serialization to ensure all properties are copied correctly.
/// </summary>
public static class DeepCloner
{
    private static readonly ISerializer _serializer;
    private static readonly IDeserializer _deserializer;

    static DeepCloner()
    {
        // Use the same YAML configuration as ConfigManager to ensure consistency
        _serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeConverter(new WidgetLocationTypeConverter())
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
            .Build();

        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeConverter(new WidgetLocationTypeConverter())
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Creates a deep clone of an object by serializing and deserializing it.
    /// This ensures all properties, including nested objects and collections, are properly copied.
    /// </summary>
    /// <typeparam name="T">The type of object to clone</typeparam>
    /// <param name="source">The source object to clone</param>
    /// <returns>A deep clone of the source object</returns>
    /// <exception cref="ArgumentNullException">Thrown when source is null</exception>
    public static T Clone<T>(T source) where T : class
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        // Serialize to YAML
        var yaml = _serializer.Serialize(source);

        // Deserialize back to object (creates a deep copy)
        var clone = _deserializer.Deserialize<T>(yaml);

        if (clone == null)
            throw new InvalidOperationException($"Failed to clone object of type {typeof(T).Name}");

        return clone;
    }

    /// <summary>
    /// Attempts to create a deep clone of an object.
    /// Returns null if cloning fails instead of throwing an exception.
    /// </summary>
    /// <typeparam name="T">The type of object to clone</typeparam>
    /// <param name="source">The source object to clone</param>
    /// <returns>A deep clone of the source object, or null if cloning fails</returns>
    public static T? TryClone<T>(T? source) where T : class
    {
        if (source == null)
            return null;

        try
        {
            var yaml = _serializer.Serialize(source);
            return _deserializer.Deserialize<T>(yaml);
        }
        catch
        {
            return null;
        }
    }
}
