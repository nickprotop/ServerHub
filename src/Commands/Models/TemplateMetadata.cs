// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using YamlDotNet.Serialization;

namespace ServerHub.Commands.Models;

public class TemplateMetadata
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "display_name")]
    public string DisplayName { get; set; } = "";

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = "";

    [YamlMember(Alias = "language")]
    public string Language { get; set; } = "";

    [YamlMember(Alias = "difficulty")]
    public string Difficulty { get; set; } = "";

    [YamlMember(Alias = "author")]
    public string Author { get; set; } = "";

    [YamlMember(Alias = "tags")]
    public List<string> Tags { get; set; } = new();

    [YamlMember(Alias = "template_file")]
    public string TemplateFile { get; set; } = "";

    [YamlMember(Alias = "variables")]
    public Dictionary<string, TemplateVariable> Variables { get; set; } = new();

    [YamlMember(Alias = "post_creation")]
    public List<string> PostCreationInstructions { get; set; } = new();
}

public class TemplateVariable
{
    [YamlMember(Alias = "description")]
    public string Description { get; set; } = "";

    [YamlMember(Alias = "required")]
    public bool Required { get; set; }

    [YamlMember(Alias = "default")]
    public string? Default { get; set; }

    [YamlMember(Alias = "pattern")]
    public string? Pattern { get; set; }

    [YamlMember(Alias = "example")]
    public string? Example { get; set; }
}
