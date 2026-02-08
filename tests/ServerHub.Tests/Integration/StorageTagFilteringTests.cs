// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Xunit;
using ServerHub.Services;
using ServerHub.Storage;
using ServerHub.Models;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace ServerHub.Tests.Integration;

/// <summary>
/// Comprehensive tests for storage tag filtering across all timeline methods and history elements.
/// This test suite ensures that tag-based queries work correctly for:
/// - GetTimeSeries (history_graph, history_sparkline, history_line)
/// - GetLatest (datafetch:latest)
/// - GetAggregated (datafetch with aggregations)
/// </summary>
[Collection("Storage Tests")]
public class StorageTagFilteringTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly StorageService _storageService;
    private readonly string _widgetId;
    private readonly WidgetDataRepository _repository;

    public StorageTagFilteringTests()
    {
        // Create a temporary database for testing
        _testDbPath = Path.Combine(Path.GetTempPath(), $"serverhub_tag_test_{Guid.NewGuid()}.db");
        var config = new StorageConfig
        {
            DatabasePath = _testDbPath,
            Enabled = true
        };
        _storageService = StorageService.Initialize(config);
        _widgetId = "tag_filter_test_widget";
        _repository = _storageService.GetRepository(_widgetId);
    }

    public void Dispose()
    {
        StorageService.Shutdown();
        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    #region GetTimeSeries Tests (history_graph, history_sparkline, history_line)

    [Fact]
    public void GetTimeSeries_NoTagsInQuery_MatchesOnlyDataWithoutTags()
    {
        // Arrange - Insert data with and without tags
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("network_traffic", null, baseTime, "rx_kb", 100.0);
        _repository.Insert("network_traffic", new Dictionary<string, string> { { "interface", "eth0" } }, baseTime + 1, "rx_kb", 200.0);
        _repository.Insert("network_traffic", null, baseTime + 2, "rx_kb", 150.0);

        // Act - Query without tags (defensive: only matches data with NULL tags)
        var results = _repository.GetTimeSeries("network_traffic", "rx_kb", "last_10");

        // Assert - Should return only data without tags (defensive behavior)
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.FieldValue == 100.0);
        Assert.Contains(results, r => r.FieldValue == 150.0);
    }

    [Fact]
    public void GetTimeSeries_WithSingleTag_FiltersCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("network_traffic", new Dictionary<string, string> { { "interface", "eth0" } }, baseTime, "rx_kb", 100.0);
        _repository.Insert("network_traffic", new Dictionary<string, string> { { "interface", "eth1" } }, baseTime + 1, "rx_kb", 200.0);
        _repository.Insert("network_traffic", new Dictionary<string, string> { { "interface", "eth0" } }, baseTime + 2, "rx_kb", 150.0);

        // Act - Query with interface=eth0 tag
        var results = _repository.GetTimeSeries("network_traffic", "rx_kb", "last_10",
            new Dictionary<string, string> { { "interface", "eth0" } });

        // Assert - Should only return eth0 data
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.FieldValue == 100.0 || r.FieldValue == 150.0));
        Assert.DoesNotContain(results, r => r.FieldValue == 200.0);
    }

    [Fact]
    public void GetTimeSeries_WithMultipleTags_FiltersCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("docker_stats", new Dictionary<string, string> { { "container", "web" }, { "host", "server1" } }, baseTime, "cpu_pct", 50.0);
        _repository.Insert("docker_stats", new Dictionary<string, string> { { "container", "web" }, { "host", "server2" } }, baseTime + 1, "cpu_pct", 60.0);
        _repository.Insert("docker_stats", new Dictionary<string, string> { { "container", "api" }, { "host", "server1" } }, baseTime + 2, "cpu_pct", 70.0);
        _repository.Insert("docker_stats", new Dictionary<string, string> { { "container", "web" }, { "host", "server1" } }, baseTime + 3, "cpu_pct", 55.0);

        // Act - Query with both container=web AND host=server1
        var results = _repository.GetTimeSeries("docker_stats", "cpu_pct", "last_10",
            new Dictionary<string, string> { { "container", "web" }, { "host", "server1" } });

        // Assert - Should only return data matching BOTH tags
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.FieldValue == 50.0 || r.FieldValue == 55.0));
    }

    [Fact]
    public void GetTimeSeries_SampleBasedRange_ReturnsCorrectCount()
    {
        // Arrange - Insert 50 data points
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (int i = 0; i < 50; i++)
        {
            _repository.Insert("cpu_load", null, baseTime + i, "value", i * 10.0);
        }

        // Act - Query last_30
        var results = _repository.GetTimeSeries("cpu_load", "value", "last_30");

        // Assert
        Assert.Equal(30, results.Count);
        Assert.Equal(490.0, results.Last().FieldValue); // Most recent (49 * 10)
    }

    [Fact]
    public void GetTimeSeries_TimeBasedRange_ReturnsCorrectTimeWindow()
    {
        // Arrange - Insert data at specific timestamps
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var oneHourAgo = now - 3600;
        var twoHoursAgo = now - 7200;

        _repository.Insert("metric", null, twoHoursAgo, "value", 10.0);
        _repository.Insert("metric", null, oneHourAgo, "value", 20.0);
        _repository.Insert("metric", null, now, "value", 30.0);

        // Act - Query last 1 hour
        var results = _repository.GetTimeSeries("metric", "value", "1h");

        // Assert - Should only get data from last hour
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.FieldValue == 20.0);
        Assert.Contains(results, r => r.FieldValue == 30.0);
        Assert.DoesNotContain(results, r => r.FieldValue == 10.0);
    }

    [Fact]
    public void GetTimeSeries_EmptyResults_ReturnsEmptyList()
    {
        // Act - Query non-existent data
        var results = _repository.GetTimeSeries("nonexistent", "value", "last_10");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void GetTimeSeries_TagMismatch_ReturnsEmptyList()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("network_traffic", new Dictionary<string, string> { { "interface", "eth0" } }, baseTime, "rx_kb", 100.0);

        // Act - Query with non-matching tag
        var results = _repository.GetTimeSeries("network_traffic", "rx_kb", "last_10",
            new Dictionary<string, string> { { "interface", "eth999" } });

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region GetLatest Tests (datafetch:latest)

    [Fact]
    public void GetLatest_NoTags_ReturnsMostRecentValue()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("cpu_load", null, baseTime, "value", 50.0);
        _repository.Insert("cpu_load", null, baseTime + 1, "value", 60.0);
        _repository.Insert("cpu_load", null, baseTime + 2, "value", 70.0);

        // Act
        var result = _repository.GetLatest("cpu_load", "value");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(70.0, result.FieldValue);
    }

    [Fact]
    public void GetLatest_WithTags_ReturnsLatestMatchingValue()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("disk_usage", new Dictionary<string, string> { { "mount", "/" } }, baseTime, "value", 80.0);
        _repository.Insert("disk_usage", new Dictionary<string, string> { { "mount", "/home" } }, baseTime + 1, "value", 50.0);
        _repository.Insert("disk_usage", new Dictionary<string, string> { { "mount", "/" } }, baseTime + 2, "value", 85.0);

        // Act - Query latest for mount=/
        var result = _repository.GetLatest("disk_usage", "value",
            new Dictionary<string, string> { { "mount", "/" } });

        // Assert
        Assert.NotNull(result);
        Assert.Equal(85.0, result.FieldValue);
    }

    [Fact]
    public void GetLatest_TagMismatch_ReturnsNull()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("disk_usage", new Dictionary<string, string> { { "mount", "/" } }, baseTime, "value", 80.0);

        // Act - Query with non-matching tag
        var result = _repository.GetLatest("disk_usage", "value",
            new Dictionary<string, string> { { "mount", "/nonexistent" } });

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetLatest_NonExistentMeasurement_ReturnsNull()
    {
        // Act
        var result = _repository.GetLatest("nonexistent", "value");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetAggregated Tests (datafetch:avg, max, min, sum, count)

    [Fact]
    public void GetAggregated_Average_CalculatesCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric", null, baseTime, "value", 10.0);
        _repository.Insert("metric", null, baseTime + 1, "value", 20.0);
        _repository.Insert("metric", null, baseTime + 2, "value", 30.0);

        // Act
        var result = _repository.GetAggregated("metric", "value", "1h");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(20.0, result.Average);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void GetAggregated_MaxAndMin_CalculatesCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric", null, baseTime, "value", 50.0);
        _repository.Insert("metric", null, baseTime + 1, "value", 10.0);
        _repository.Insert("metric", null, baseTime + 2, "value", 80.0);
        _repository.Insert("metric", null, baseTime + 3, "value", 30.0);

        // Act
        var result = _repository.GetAggregated("metric", "value", "1h");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(80.0, result.Max);
        Assert.Equal(10.0, result.Min);
    }

    [Fact]
    public void GetAggregated_Sum_CalculatesCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric", null, baseTime, "value", 10.0);
        _repository.Insert("metric", null, baseTime + 1, "value", 20.0);
        _repository.Insert("metric", null, baseTime + 2, "value", 30.0);

        // Act
        var result = _repository.GetAggregated("metric", "value", "1h");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(60.0, result.Sum);
    }

    [Fact]
    public void GetAggregated_Count_ReturnsCorrectCount()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (int i = 0; i < 15; i++)
        {
            _repository.Insert("metric", null, baseTime + i, "value", i * 10.0);
        }

        // Act - Use sample-based range to aggregate last 10 samples
        var result = _repository.GetAggregated("metric", "value", "last_10");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.Count); // Should only aggregate last 10
    }

    [Fact]
    public void GetAggregated_WithTags_AggregatesFilteredData()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("network_traffic", new Dictionary<string, string> { { "interface", "eth0" } }, baseTime, "rx_kb", 100.0);
        _repository.Insert("network_traffic", new Dictionary<string, string> { { "interface", "eth0" } }, baseTime + 1, "rx_kb", 200.0);
        _repository.Insert("network_traffic", new Dictionary<string, string> { { "interface", "eth1" } }, baseTime + 2, "rx_kb", 500.0);
        _repository.Insert("network_traffic", new Dictionary<string, string> { { "interface", "eth0" } }, baseTime + 3, "rx_kb", 300.0);

        // Act - Aggregate only eth0 data
        var result = _repository.GetAggregated("network_traffic", "rx_kb", "1h",
            new Dictionary<string, string> { { "interface", "eth0" } });

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count); // Only 3 eth0 entries
        Assert.Equal(200.0, result.Average); // (100 + 200 + 300) / 3
        Assert.Equal(600.0, result.Sum);
        Assert.Equal(300.0, result.Max);
        Assert.Equal(100.0, result.Min);
    }

    [Fact]
    public void GetAggregated_EmptyResults_ReturnsNull()
    {
        // Act
        var result = _repository.GetAggregated("nonexistent", "value", "1h");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetAggregated_TimeBasedRange_AggregatesCorrectTimeWindow()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var oneHourAgo = now - 3600;
        var twoHoursAgo = now - 7200;

        _repository.Insert("metric", null, twoHoursAgo, "value", 10.0);
        _repository.Insert("metric", null, oneHourAgo, "value", 20.0);
        _repository.Insert("metric", null, now, "value", 30.0);

        // Act - Aggregate last 1 hour
        var result = _repository.GetAggregated("metric", "value", "1h");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count); // Only last 2 data points
        Assert.Equal(25.0, result.Average); // (20 + 30) / 2
        Assert.Equal(50.0, result.Sum);
    }

    #endregion

    #region WidgetProtocolParser Integration Tests

    [Fact]
    public void HistoryGraph_WithTags_ParsesAndQueriesCorrectly()
    {
        // Arrange - Insert test data
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("network_traffic", new Dictionary<string, string> { { "interface", "eth0" } }, baseTime, "rx_kb", 100.0);
        _repository.Insert("network_traffic", new Dictionary<string, string> { { "interface", "eth0" } }, baseTime + 1, "rx_kb", 200.0);
        _repository.Insert("network_traffic", new Dictionary<string, string> { { "interface", "eth1" } }, baseTime + 2, "rx_kb", 500.0);

        var scriptOutput = @"
title: Network Test
row: [history_graph:network_traffic.rx_kb,interface=eth0:last_10:cool:RX:0-auto:40]
";

        // Act
        var parser = new WidgetProtocolParser();
        // Storage context passed via Parse
        var widgetData = parser.Parse(scriptOutput, _storageService, _widgetId);

        // Assert
        var row = widgetData.Rows.FirstOrDefault(r => r.HistoryGraph != null);
        Assert.NotNull(row);
        Assert.NotNull(row.HistoryGraph);
        Assert.Equal(2, row.HistoryGraph.Values.Count); // Only eth0 data
        Assert.Contains(100.0, row.HistoryGraph.Values);
        Assert.Contains(200.0, row.HistoryGraph.Values);
        Assert.DoesNotContain(500.0, row.HistoryGraph.Values); // eth1 data excluded
    }

    [Fact]
    public void HistorySparkline_WithTags_ParsesAndQueriesCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("docker_stats", new Dictionary<string, string> { { "container", "web" } }, baseTime, "cpu_pct", 30.0);
        _repository.Insert("docker_stats", new Dictionary<string, string> { { "container", "api" } }, baseTime + 1, "cpu_pct", 70.0);
        _repository.Insert("docker_stats", new Dictionary<string, string> { { "container", "web" } }, baseTime + 2, "cpu_pct", 40.0);

        var scriptOutput = @"
title: Docker Test
row: [history_sparkline:docker_stats.cpu_pct,container=web:last_10:spectrum:20]
";

        // Act
        var parser = new WidgetProtocolParser();
        // Storage context passed via Parse
        var widgetData = parser.Parse(scriptOutput, _storageService, _widgetId);

        // Assert
        var row = widgetData.Rows.FirstOrDefault(r => r.HistorySparkline != null);
        Assert.NotNull(row);
        Assert.NotNull(row.HistorySparkline);
        Assert.Equal(2, row.HistorySparkline.Values.Count); // Only web container
        Assert.Contains(30.0, row.HistorySparkline.Values);
        Assert.Contains(40.0, row.HistorySparkline.Values);
    }

    [Fact]
    public void HistoryLine_WithTags_ParsesAndQueriesCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("disk_usage", new Dictionary<string, string> { { "mount", "/" } }, baseTime, "value", 80.0);
        _repository.Insert("disk_usage", new Dictionary<string, string> { { "mount", "/home" } }, baseTime + 1, "value", 50.0);
        _repository.Insert("disk_usage", new Dictionary<string, string> { { "mount", "/" } }, baseTime + 2, "value", 85.0);

        var scriptOutput = @"
title: Disk Test
row: [history_line:disk_usage.value,mount=/:last_10:warm:Disk:0-100:60:8:braille]
";

        // Act
        var parser = new WidgetProtocolParser();
        // Storage context passed via Parse
        var widgetData = parser.Parse(scriptOutput, _storageService, _widgetId);

        // Assert
        var row = widgetData.Rows.FirstOrDefault(r => r.HistoryLineGraph != null);
        Assert.NotNull(row);
        Assert.NotNull(row.HistoryLineGraph);
        Assert.Equal(2, row.HistoryLineGraph.Values.Count); // Only / mount
        Assert.Contains(80.0, row.HistoryLineGraph.Values);
        Assert.Contains(85.0, row.HistoryLineGraph.Values);
    }

    [Fact]
    public void Datafetch_WithTags_ParsesAndQueriesCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("network_traffic", new Dictionary<string, string> { { "interface", "eth0" } }, baseTime, "rx_kb", 100.0);
        _repository.Insert("network_traffic", new Dictionary<string, string> { { "interface", "eth0" } }, baseTime + 1, "rx_kb", 200.0);
        _repository.Insert("network_traffic", new Dictionary<string, string> { { "interface", "eth1" } }, baseTime + 2, "rx_kb", 500.0);

        var scriptOutput = @"
title: Network Test
row: Average RX (eth0): [datafetch:network_traffic.rx_kb,interface=eth0:avg:last_10] KB/s
";

        // Act
        var parser = new WidgetProtocolParser();
        // Storage context passed via Parse
        var widgetData = parser.Parse(scriptOutput, _storageService, _widgetId);

        // Assert
        var row = widgetData.Rows.FirstOrDefault(r => r.Datafetch != null);
        Assert.NotNull(row);
        Assert.NotNull(row.Datafetch);
        Assert.Equal("150", row.Datafetch.Value); // (100 + 200) / 2 = 150
        Assert.Contains("150", row.Content); // Value should be interpolated
    }

    [Fact]
    public void HistoryGraph_WithMultipleTags_ParsesAndQueriesCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("app_metrics", new Dictionary<string, string> { { "service", "api" }, { "env", "prod" } }, baseTime, "latency", 100.0);
        _repository.Insert("app_metrics", new Dictionary<string, string> { { "service", "api" }, { "env", "staging" } }, baseTime + 1, "latency", 200.0);
        _repository.Insert("app_metrics", new Dictionary<string, string> { { "service", "web" }, { "env", "prod" } }, baseTime + 2, "latency", 300.0);
        _repository.Insert("app_metrics", new Dictionary<string, string> { { "service", "api" }, { "env", "prod" } }, baseTime + 3, "latency", 150.0);

        var scriptOutput = @"
title: App Test
row: [history_graph:app_metrics.latency,service=api,env=prod:last_10:spectrum:Latency:0-500:40]
";

        // Act
        var parser = new WidgetProtocolParser();
        // Storage context passed via Parse
        var widgetData = parser.Parse(scriptOutput, _storageService, _widgetId);

        // Assert
        var row = widgetData.Rows.FirstOrDefault(r => r.HistoryGraph != null);
        Assert.NotNull(row);
        Assert.NotNull(row.HistoryGraph);
        Assert.Equal(2, row.HistoryGraph.Values.Count); // Only api+prod
        Assert.Contains(100.0, row.HistoryGraph.Values);
        Assert.Contains(150.0, row.HistoryGraph.Values);
    }

    [Fact]
    public void HistoryGraph_NoTags_ReturnsAllData()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("cpu_load", null, baseTime, "value", 50.0);
        _repository.Insert("cpu_load", null, baseTime + 1, "value", 60.0);
        _repository.Insert("cpu_load", null, baseTime + 2, "value", 70.0);

        var scriptOutput = @"
title: CPU Test
row: [history_graph:cpu_load.value:last_10:cool:Load:0-100:40]
";

        // Act
        var parser = new WidgetProtocolParser();
        // Storage context passed via Parse
        var widgetData = parser.Parse(scriptOutput, _storageService, _widgetId);

        // Assert
        var row = widgetData.Rows.FirstOrDefault(r => r.HistoryGraph != null);
        Assert.NotNull(row);
        Assert.NotNull(row.HistoryGraph);
        Assert.Equal(3, row.HistoryGraph.Values.Count);
        Assert.Contains(50.0, row.HistoryGraph.Values);
        Assert.Contains(60.0, row.HistoryGraph.Values);
        Assert.Contains(70.0, row.HistoryGraph.Values);
    }

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    public void HistoryGraph_InvalidTagSyntax_HandlesGracefully()
    {
        // Arrange - Malformed tag (missing equals sign)
        var scriptOutput = @"
title: Test
row: [history_graph:metric.value,invalid:last_10:cool:Label:0-100:40]
";

        // Act & Assert - Should not throw
        var parser = new WidgetProtocolParser();
        // Storage context passed via Parse
        var widgetData = parser.Parse(scriptOutput, _storageService, _widgetId);

        // Should still parse but may have empty results
        Assert.NotNull(widgetData);
    }

    [Fact]
    public void HistoryGraph_EmptyTagValue_HandlesGracefully()
    {
        // Arrange
        var scriptOutput = @"
title: Test
row: [history_graph:metric.value,tag=:last_10:cool:Label:0-100:40]
";

        // Act & Assert - Should not throw
        var parser = new WidgetProtocolParser();
        // Storage context passed via Parse
        var widgetData = parser.Parse(scriptOutput, _storageService, _widgetId);

        Assert.NotNull(widgetData);
    }

    [Fact]
    public void GetTimeSeries_NullTags_MatchesOnlyNullTags()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric", null, baseTime, "value", 10.0);
        _repository.Insert("metric", new Dictionary<string, string> { { "tag", "value" } }, baseTime + 1, "value", 20.0);

        // Act - Query with null tags (defensive: only matches NULL tags in database)
        var results = _repository.GetTimeSeries("metric", "value", "last_10", null);

        // Assert - Should return only data with NULL tags (defensive behavior prevents accidental large queries)
        Assert.Single(results);
        Assert.Equal(10.0, results[0].FieldValue);
    }

    [Fact]
    public void GetTimeSeries_EmptyTagsDictionary_MatchesOnlyNullTags()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric", null, baseTime, "value", 10.0);
        _repository.Insert("metric", new Dictionary<string, string> { { "tag", "value" } }, baseTime + 1, "value", 20.0);

        // Act - Query with empty dictionary (treated as null by TagNormalizer)
        var results = _repository.GetTimeSeries("metric", "value", "last_10", new Dictionary<string, string>());

        // Assert - Should return only data with NULL tags (defensive behavior)
        Assert.Single(results);
        Assert.Equal(10.0, results[0].FieldValue);
    }

    #endregion
}
