// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using ServerHub.Models;

namespace ServerHub.Services;

/// <summary>
/// Custom YAML type converter for WidgetLocation enum
/// Handles conversion between YAML string values (bundled/custom/auto) and enum values
/// </summary>
public class WidgetLocationTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(WidgetLocation) || type == typeof(WidgetLocation?);
    }

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var scalar = parser.Consume<Scalar>();
        var value = scalar.Value;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.ToLowerInvariant() switch
        {
            "bundled" => WidgetLocation.Bundled,
            "custom" => WidgetLocation.Custom,
            "auto" => WidgetLocation.Auto,
            _ => throw new YamlException(scalar.Start, scalar.End,
                $"Invalid widget location value: '{value}'. Expected 'bundled', 'custom', or 'auto'.")
        };
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        if (value == null)
        {
            emitter.Emit(new Scalar(string.Empty));
            return;
        }

        var location = (WidgetLocation)value;
        var yamlValue = location switch
        {
            WidgetLocation.Bundled => "bundled",
            WidgetLocation.Custom => "custom",
            WidgetLocation.Auto => "auto",
            _ => throw new ArgumentException($"Unknown WidgetLocation value: {location}")
        };

        emitter.Emit(new Scalar(yamlValue));
    }
}
