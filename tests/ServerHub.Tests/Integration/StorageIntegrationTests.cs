// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Xunit;
using ServerHub.Services;
using ServerHub.Storage;
using ServerHub.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ServerHub.Tests.Integration;

/// <summary>
/// Integration tests for the storage pipeline:
/// Widget Script Output → WidgetProtocolParser → WidgetRefreshService → StorageService → SQLite
/// </summary>
public class StorageIntegrationTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly StorageService _storageService;

    public StorageIntegrationTests()
    {
        // Create a temporary database for testing
        _testDbPath = Path.Combine(Path.GetTempPath(), $"serverhub_test_{Guid.NewGuid()}.db");
        var config = new StorageConfig
        {
            DatabasePath = _testDbPath,
            Enabled = true
        };
        _storageService = StorageService.Initialize(config);
    }

    public void Dispose()
    {
        // Cleanup
        StorageService.Shutdown();
        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Fact]
    public void DatastoreDirective_ParsedAndPersisted()
    {
        // Arrange
        var scriptOutput = @"
title: CPU Monitor
row: CPU Usage: 75%
datastore: cpu_usage value=75.5
";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        // Act - Simulate persistence (this would normally happen in WidgetRefreshService)
        var widgetId = "cpu_test";
        var repository = _storageService.GetRepository(widgetId);

        foreach (var directive in widgetData.DatastoreDirectives)
        {
            var timestamp = directive.Timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var field in directive.Fields)
            {
                double? fieldValue = field.Value is double d ? d : null;
                repository.Insert(
                    directive.Measurement,
                    directive.Tags,
                    timestamp,
                    field.Key,
                    fieldValue
                );
            }
        }

        // Assert - Verify data was persisted
        var latest = repository.GetLatest("cpu_usage", "value");
        Assert.NotNull(latest);
        Assert.Equal(75.5, latest.FieldValue);
    }

    [Fact]
    public void DatastoreDirective_WithTags_ParsedAndPersisted()
    {
        // Arrange
        var scriptOutput = @"
title: CPU Monitor
datastore: cpu_usage,core=0,host=server1 value=75.5
";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        // Act
        var widgetId = "cpu_test_tags";
        var repository = _storageService.GetRepository(widgetId);

        foreach (var directive in widgetData.DatastoreDirectives)
        {
            var timestamp = directive.Timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var field in directive.Fields)
            {
                double? fieldValue = field.Value is double d ? d : null;
                repository.Insert(
                    directive.Measurement,
                    directive.Tags,
                    timestamp,
                    field.Key,
                    fieldValue
                );
            }
        }

        // Assert
        var latest = repository.GetLatest("cpu_usage", "value", new Dictionary<string, string>
        {
            { "core", "0" },
            { "host", "server1" }
        });
        Assert.NotNull(latest);
        Assert.Equal(75.5, latest.FieldValue);
    }

    [Fact]
    public void MultipleWidgets_HaveIsolatedData()
    {
        // Arrange & Act
        var widgetId1 = "widget1";
        var widgetId2 = "widget2";

        var repo1 = _storageService.GetRepository(widgetId1);
        var repo2 = _storageService.GetRepository(widgetId2);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        repo1.Insert("metric", null, timestamp, "value", 100.0);
        repo2.Insert("metric", null, timestamp, "value", 200.0);

        // Assert
        var data1 = repo1.GetLatest("metric", "value");
        var data2 = repo2.GetLatest("metric", "value");

        Assert.NotNull(data1);
        Assert.NotNull(data2);
        Assert.Equal(100.0, data1.FieldValue);
        Assert.Equal(200.0, data2.FieldValue);
    }

    [Fact]
    public void DataPersistsAcrossRepositoryInstances()
    {
        // Arrange
        var widgetId = "persistence_test";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act - Insert with first repository instance
        {
            var repo = _storageService.GetRepository(widgetId);
            repo.Insert("test_metric", null, timestamp, "value", 42.0);
        }

        // Assert - Retrieve with second repository instance
        {
            var repo = _storageService.GetRepository(widgetId);
            var latest = repo.GetLatest("test_metric", "value");
            Assert.NotNull(latest);
            Assert.Equal(42.0, latest.FieldValue);
        }
    }

    [Fact]
    public async Task WidgetRefreshService_PersistsDatastoreDirectives()
    {
        // Arrange - Enable test mode to allow /tmp scripts
        ApplicationState.IsTestMode = true;

        // Create a real temporary widget script
        var testScriptPath = Path.Combine(Path.GetTempPath(), $"test_widget_{Guid.NewGuid()}.sh");
        File.WriteAllText(testScriptPath, @"#!/bin/bash
echo 'title: Test Widget'
echo 'datastore: test_measurement value=123.45'
");
#pragma warning disable CA1416 // ServerHub is Linux-only
        File.SetUnixFileMode(testScriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#pragma warning restore CA1416

        try
        {
            var config = new ServerHubConfig
            {
                Widgets = new Dictionary<string, WidgetConfig>(),
                Storage = new StorageConfig { Enabled = true }
            };

            var executor = new ScriptExecutor(new ScriptValidator(devMode: true));
            var parser = new WidgetProtocolParser();
            var refreshService = new WidgetRefreshService(executor, parser, config, _storageService);

            config.Widgets["test_widget"] = new WidgetConfig
            {
                Path = testScriptPath,
                Refresh = 5
            };

            // Act
            var widgetData = await refreshService.RefreshAsync("test_widget");

            // Assert
            Assert.False(widgetData.HasError, widgetData.Error ?? "Unknown error");

            var repository = _storageService.GetRepository("test_widget");
            var latest = repository.GetLatest("test_measurement", "value");
            Assert.NotNull(latest);
            Assert.Equal(123.45, latest.FieldValue);
        }
        finally
        {
            ApplicationState.IsTestMode = false;
            if (File.Exists(testScriptPath))
                File.Delete(testScriptPath);
        }
    }

    [Fact]
    public async Task WidgetRefreshService_StorageDisabled_DirectivesNotPersisted()
    {
        // Arrange - Enable test mode to allow /tmp scripts
        ApplicationState.IsTestMode = true;

        // Create a real temporary widget script
        var testScriptPath = Path.Combine(Path.GetTempPath(), $"test_widget_disabled_{Guid.NewGuid()}.sh");
        File.WriteAllText(testScriptPath, @"#!/bin/bash
echo 'title: Test Widget'
echo 'datastore: test_measurement value=999.0'
");
#pragma warning disable CA1416 // ServerHub is Linux-only
        File.SetUnixFileMode(testScriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#pragma warning restore CA1416

        try
        {
            var config = new ServerHubConfig
            {
                Widgets = new Dictionary<string, WidgetConfig>(),
                Storage = new StorageConfig { Enabled = false }  // Storage disabled
            };

            var executor = new ScriptExecutor(new ScriptValidator(devMode: true));
            var parser = new WidgetProtocolParser();
            var refreshService = new WidgetRefreshService(executor, parser, config, _storageService);

            config.Widgets["test_widget_disabled"] = new WidgetConfig
            {
                Path = testScriptPath,
                Refresh = 5
            };

            // Act
            var widgetData = await refreshService.RefreshAsync("test_widget_disabled");

            // Assert - Widget works but data not persisted
            Assert.False(widgetData.HasError, widgetData.Error ?? "Unknown error");
            Assert.Single(widgetData.DatastoreDirectives);  // Parsed but not persisted

            var repository = _storageService.GetRepository("test_widget_disabled");
            var latest = repository.GetLatest("test_measurement", "value");
            Assert.Null(latest);  // No data in database
        }
        finally
        {
            ApplicationState.IsTestMode = false;
            if (File.Exists(testScriptPath))
                File.Delete(testScriptPath);
        }
    }

    /// <summary>
    /// Mock script executor for testing (returns hardcoded output instead of running scripts)
    /// </summary>
    private class MockScriptExecutor : ScriptExecutor
    {
        private readonly string _output;

        public MockScriptExecutor(string output) : base(new ScriptValidator(devMode: true))
        {
            _output = output;
        }

        public new Task<ExecutionResult> ExecuteAsync(string scriptPath, string? arguments = null, string? expectedChecksum = null, int timeoutSeconds = 10)
        {
            return Task.FromResult(ExecutionResult.Success(_output));
        }
    }
}
