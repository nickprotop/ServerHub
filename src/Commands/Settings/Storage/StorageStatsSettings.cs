// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Spectre.Console.Cli;
using System.ComponentModel;

namespace ServerHub.Commands.Settings.Storage;

/// <summary>
/// Settings for the storage stats command.
/// </summary>
public class StorageStatsSettings : CommandSettings
{
    [CommandOption("--config <PATH>")]
    [Description("Path to configuration file")]
    public string? ConfigPath { get; set; }
}
