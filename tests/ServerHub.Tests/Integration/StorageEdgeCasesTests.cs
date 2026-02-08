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
/// Edge cases, fault tolerance, and error handling tests for the storage system.
/// Tests scenarios like:
/// - No data available
/// - Insufficient data for timeline
/// - Database corruption
/// - Concurrent access
/// - Invalid queries
/// - Extreme values
/// </summary>
[Collection("Storage Tests")]
public class StorageEdgeCasesTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly StorageService _storageService;
    private readonly string _widgetId;
    private readonly WidgetDataRepository _repository;

    public StorageEdgeCasesTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"serverhub_edge_test_{Guid.NewGuid()}.db");
        var config = new StorageConfig
        {
            DatabasePath = _testDbPath,
            Enabled = true
        };
        _storageService = StorageService.Initialize(config);
        _widgetId = "edge_case_test_widget";
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

    #region No Data Cases

    [Fact]
    public void GetTimeSeries_NoDataExists_ReturnsEmptyList()
    {
        // Act - Query before any data inserted
        var results = _repository.GetTimeSeries("cpu_load", "value", "last_30");

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public void GetLatest_NoDataExists_ReturnsNull()
    {
        // Act
        var result = _repository.GetLatest("cpu_load", "value");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetAggregated_NoDataExists_ReturnsNull()
    {
        // Act
        var result = _repository.GetAggregated("cpu_load", "value", "1h");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void HistoryGraph_NoDataExists_ParsesWithEmptyValues()
    {
        // Arrange
        var scriptOutput = @"
title: CPU Test
row: [history_graph:cpu_load.value:last_30:cool:Load:0-100:40]
";

        // Act
        var parser = new WidgetProtocolParser();
        // Storage context passed via Parse
        var widgetData = parser.Parse(scriptOutput, _storageService, _widgetId);

        // Assert - Should parse successfully with empty values
        var row = widgetData.Rows.FirstOrDefault(r => r.HistoryGraph != null);
        Assert.NotNull(row);
        Assert.NotNull(row.HistoryGraph);
        Assert.Empty(row.HistoryGraph.Values);
    }

    [Fact]
    public void Datafetch_NoDataExists_ReturnsNull()
    {
        // Arrange
        var scriptOutput = @"
title: Test
row: Value: [datafetch:metric.value:latest]
";

        // Act
        var parser = new WidgetProtocolParser();
        // Storage context passed via Parse
        var widgetData = parser.Parse(scriptOutput, _storageService, _widgetId);

        // Assert - Should replace with placeholder
        var row = widgetData.Rows.FirstOrDefault();
        Assert.NotNull(row);
        Assert.Contains("--", row.Content); // Default placeholder
    }

    #endregion

    #region Insufficient Data Cases

    [Fact]
    public void GetTimeSeries_LessThanRequestedSamples_ReturnsAvailableData()
    {
        // Arrange - Insert only 5 samples
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (int i = 0; i < 5; i++)
        {
            _repository.Insert("metric", null, baseTime + i, "value", i * 10.0);
        }

        // Act - Request last 30 samples
        var results = _repository.GetTimeSeries("metric", "value", "last_30");

        // Assert - Should return all 5 available samples
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public void GetTimeSeries_SingleDataPoint_ReturnsSingleValue()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric", null, baseTime, "value", 42.0);

        // Act
        var results = _repository.GetTimeSeries("metric", "value", "last_30");

        // Assert
        Assert.Single(results);
        Assert.Equal(42.0, results[0].FieldValue);
    }

    [Fact]
    public void GetAggregated_SingleDataPoint_CalculatesCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric", null, baseTime, "value", 42.0);

        // Act
        var result = _repository.GetAggregated("metric", "value", "last_30");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Count);
        Assert.Equal(42.0, result.Average);
        Assert.Equal(42.0, result.Max);
        Assert.Equal(42.0, result.Min);
        Assert.Equal(42.0, result.Sum);
    }

    [Fact]
    public void GetTimeSeries_PartialTimeRange_ReturnsPartialData()
    {
        // Arrange - Insert data only for last 30 minutes
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var thirtyMinutesAgo = now - 1800;

        _repository.Insert("metric", null, thirtyMinutesAgo, "value", 10.0);
        _repository.Insert("metric", null, now, "value", 20.0);

        // Act - Request 1 hour of data
        var results = _repository.GetTimeSeries("metric", "value", "1h");

        // Assert - Should return partial data (2 points instead of expected more)
        Assert.Equal(2, results.Count);
    }

    #endregion

    #region Invalid Query Cases

    [Fact]
    public void GetTimeSeries_InvalidTimeRange_HandlesGracefully()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric", null, baseTime, "value", 10.0);

        // Act - Invalid time range format
        var results = _repository.GetTimeSeries("metric", "value", "invalid_range");

        // Assert - Should handle gracefully (implementation may vary)
        Assert.NotNull(results);
    }

    [Fact]
    public void GetTimeSeries_NegativeTimeRange_HandlesGracefully()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric", null, baseTime, "value", 10.0);

        // Act
        var results = _repository.GetTimeSeries("metric", "value", "last_-10");

        // Assert
        Assert.NotNull(results);
    }

    [Fact]
    public void GetTimeSeries_ZeroTimeRange_HandlesGracefully()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric", null, baseTime, "value", 10.0);

        // Act
        var results = _repository.GetTimeSeries("metric", "value", "last_0");

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public void HistoryGraph_MalformedQuery_ParsesGracefully()
    {
        // Arrange - Various malformed queries
        var testCases = new[]
        {
            "[history_graph::::]", // Missing parts
            "[history_graph:measurement:invalid_range:invalid_color:label:scale:width]", // Invalid values
            "[history_graph:measurement.field.extra:last_10:cool:label:0-100:40]", // Extra dots
        };

        var parser = new WidgetProtocolParser();
        // Storage context passed via Parse

        foreach (var testCase in testCases)
        {
            var scriptOutput = $"title: Test\nrow: {testCase}";

            // Act & Assert - Should not throw
            var exception = Record.Exception(() => parser.Parse(scriptOutput));
            Assert.Null(exception);
        }
    }

    #endregion

    #region Extreme Values

    [Fact]
    public void Insert_VeryLargeValue_StoresCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var largeValue = double.MaxValue / 2; // Half of max to avoid overflow

        // Act
        _repository.Insert("metric", null, baseTime, "value", largeValue);
        var result = _repository.GetLatest("metric", "value");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(largeValue, result.FieldValue);
    }

    [Fact]
    public void Insert_VerySmallValue_StoresCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var smallValue = double.Epsilon;

        // Act
        _repository.Insert("metric", null, baseTime, "value", smallValue);
        var result = _repository.GetLatest("metric", "value");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(smallValue, result.FieldValue);
    }

    [Fact]
    public void Insert_NegativeValue_StoresCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        _repository.Insert("metric", null, baseTime, "value", -999.99);
        var result = _repository.GetLatest("metric", "value");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(-999.99, result.FieldValue);
    }

    [Fact]
    public void Insert_ZeroValue_StoresCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        _repository.Insert("metric", null, baseTime, "value", 0.0);
        var result = _repository.GetLatest("metric", "value");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0.0, result.FieldValue);
    }

    [Fact]
    public void GetTimeSeries_ExtremelyLargeSampleCount_HandlesGracefully()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric", null, baseTime, "value", 10.0);

        // Act - Request extremely large number of samples
        var results = _repository.GetTimeSeries("metric", "value", "last_999999");

        // Assert - Should not crash, returns available data
        Assert.NotNull(results);
        Assert.Single(results);
    }

    #endregion

    #region Special Characters and Naming

    [Fact]
    public void Insert_MeasurementWithSpecialChars_HandlesCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var specialMeasurement = "metric-with-dashes_and_underscores";

        // Act
        _repository.Insert(specialMeasurement, null, baseTime, "value", 42.0);
        var result = _repository.GetLatest(specialMeasurement, "value");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(42.0, result.FieldValue);
    }

    [Fact]
    public void Insert_TagWithSpecialChars_HandlesCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var tags = new Dictionary<string, string>
        {
            { "key-with-dashes", "value-with-dashes" },
            { "key_with_underscores", "value_with_underscores" }
        };

        // Act
        _repository.Insert("metric", tags, baseTime, "value", 42.0);
        var result = _repository.GetLatest("metric", "value", tags);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(42.0, result.FieldValue);
    }

    [Fact]
    public void Insert_VeryLongMeasurementName_HandlesCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var longName = new string('a', 200); // 200 character measurement name

        // Act
        _repository.Insert(longName, null, baseTime, "value", 42.0);
        var result = _repository.GetLatest(longName, "value");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(42.0, result.FieldValue);
    }

    [Fact]
    public void Insert_VeryLongTagValue_HandlesCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var longValue = new string('b', 500);
        var tags = new Dictionary<string, string> { { "tag", longValue } };

        // Act
        _repository.Insert("metric", tags, baseTime, "value", 42.0);
        var result = _repository.GetLatest("metric", "value", tags);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(42.0, result.FieldValue);
    }

    #endregion

    #region Timestamp Edge Cases

    [Fact]
    public void Insert_FutureTimestamp_StoresCorrectly()
    {
        // Arrange
        var futureTime = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();

        // Act
        _repository.Insert("metric", null, futureTime, "value", 42.0);
        var result = _repository.GetLatest("metric", "value");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(42.0, result.FieldValue);
    }

    [Fact]
    public void Insert_OldTimestamp_StoresCorrectly()
    {
        // Arrange
        var oldTime = DateTimeOffset.UtcNow.AddYears(-1).ToUnixTimeSeconds();

        // Act
        _repository.Insert("metric", null, oldTime, "value", 42.0);
        var result = _repository.GetLatest("metric", "value");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(42.0, result.FieldValue);
    }

    [Fact]
    public void GetTimeSeries_DuplicateTimestamps_ReturnsAllValues()
    {
        // Arrange - Insert multiple values with same timestamp
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric", null, baseTime, "value", 10.0);
        _repository.Insert("metric", null, baseTime, "value", 20.0);
        _repository.Insert("metric", null, baseTime, "value", 30.0);

        // Act
        var results = _repository.GetTimeSeries("metric", "value", "last_10");

        // Assert - Should return all 3 values
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void GetTimeSeries_OutOfOrderTimestamps_ReturnsChronologicalOrder()
    {
        // Arrange - Insert in random order
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric", null, baseTime + 2, "value", 30.0);
        _repository.Insert("metric", null, baseTime, "value", 10.0);
        _repository.Insert("metric", null, baseTime + 1, "value", 20.0);

        // Act
        var results = _repository.GetTimeSeries("metric", "value", "last_10");

        // Assert - Should be ordered by timestamp ascending
        Assert.Equal(3, results.Count);
        Assert.Equal(10.0, results[0].FieldValue);
        Assert.Equal(20.0, results[1].FieldValue);
        Assert.Equal(30.0, results[2].FieldValue);
    }

    #endregion

    #region Concurrent Access

    [Fact]
    public void MultipleRepositories_SameWidget_ShareData()
    {
        // Arrange
        var repo1 = _storageService.GetRepository(_widgetId);
        var repo2 = _storageService.GetRepository(_widgetId);
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act - Write with repo1, read with repo2
        repo1.Insert("metric", null, baseTime, "value", 42.0);
        var result = repo2.GetLatest("metric", "value");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(42.0, result.FieldValue);
    }

    [Fact]
    public void DifferentWidgets_HaveIsolatedData()
    {
        // Arrange
        var widget1 = "widget1";
        var widget2 = "widget2";
        var repo1 = _storageService.GetRepository(widget1);
        var repo2 = _storageService.GetRepository(widget2);
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        repo1.Insert("metric", null, baseTime, "value", 100.0);
        repo2.Insert("metric", null, baseTime, "value", 200.0);

        // Assert - Each widget should only see its own data
        var result1 = repo1.GetLatest("metric", "value");
        var result2 = repo2.GetLatest("metric", "value");

        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal(100.0, result1.FieldValue);
        Assert.Equal(200.0, result2.FieldValue);
    }

    #endregion

    #region Data Consistency

    [Fact]
    public void GetTimeSeries_AfterMultipleInserts_MaintainsConsistency()
    {
        // Arrange - Insert, query, insert more, query again
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        _repository.Insert("metric", null, baseTime, "value", 10.0);
        _repository.Insert("metric", null, baseTime + 1, "value", 20.0);

        var firstQuery = _repository.GetTimeSeries("metric", "value", "last_10");

        _repository.Insert("metric", null, baseTime + 2, "value", 30.0);

        var secondQuery = _repository.GetTimeSeries("metric", "value", "last_10");

        // Assert
        Assert.Equal(2, firstQuery.Count);
        Assert.Equal(3, secondQuery.Count);
        Assert.Equal(30.0, secondQuery.Last().FieldValue);
    }

    [Fact]
    public void GetAggregated_ConsistentWithTimeSeries()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var values = new[] { 10.0, 20.0, 30.0, 40.0, 50.0 };

        for (int i = 0; i < values.Length; i++)
        {
            _repository.Insert("metric", null, baseTime + i, "value", values[i]);
        }

        // Act
        var timeSeries = _repository.GetTimeSeries("metric", "value", "last_10");
        var aggregated = _repository.GetAggregated("metric", "value", "1h");

        // Assert - Aggregated should match time series
        Assert.NotNull(aggregated);
        Assert.Equal(timeSeries.Count, aggregated.Count);
        Assert.Equal(values.Average(), aggregated.Average);
        Assert.Equal(values.Max(), aggregated.Max);
        Assert.Equal(values.Min(), aggregated.Min);
        Assert.Equal(values.Sum(), aggregated.Sum);
    }

    #endregion

    #region Widget Protocol Parser Edge Cases

    [Fact]
    public void HistoryGraph_EmptyKey_HandlesGracefully()
    {
        // Arrange
        var scriptOutput = @"
title: Test
row: [history_graph::last_10:cool:Label:0-100:40]
";

        // Act
        var parser = new WidgetProtocolParser();
        // Storage context passed via Parse
        var widgetData = parser.Parse(scriptOutput, _storageService, _widgetId);

        // Assert - Should not crash
        Assert.NotNull(widgetData);
    }

    [Fact]
    public void HistoryGraph_MissingTimeRange_HandlesGracefully()
    {
        // Arrange
        var scriptOutput = @"
title: Test
row: [history_graph:metric.value::cool:Label:0-100:40]
";

        // Act
        var parser = new WidgetProtocolParser();
        // Storage context passed via Parse
        var widgetData = parser.Parse(scriptOutput, _storageService, _widgetId);

        // Assert
        Assert.NotNull(widgetData);
    }

    [Fact]
    public void Datafetch_InvalidAggregation_HandlesGracefully()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric", null, baseTime, "value", 42.0);

        var scriptOutput = @"
title: Test
row: Value: [datafetch:metric.value:invalid_aggregation:last_10]
";

        // Act
        var parser = new WidgetProtocolParser();
        // Storage context passed via Parse
        var widgetData = parser.Parse(scriptOutput, _storageService, _widgetId);

        // Assert - Should handle gracefully (may show placeholder)
        Assert.NotNull(widgetData);
    }

    [Fact]
    public void HistoryGraph_MultipleGraphsInSameRow_ParsesCorrectly()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric1", null, baseTime, "value", 10.0);
        _repository.Insert("metric2", null, baseTime, "value", 20.0);

        var scriptOutput = @"
title: Test
row: [history_graph:metric1.value:last_10:cool:M1:0-100:20] [history_graph:metric2.value:last_10:warm:M2:0-100:20]
";

        // Act
        var parser = new WidgetProtocolParser();
        // Storage context passed via Parse
        var widgetData = parser.Parse(scriptOutput, _storageService, _widgetId);

        // Assert - Should parse both (though only first is typically stored in HistoryGraph property)
        Assert.NotNull(widgetData);
        var row = widgetData.Rows.FirstOrDefault();
        Assert.NotNull(row);
    }

    #endregion

    #region Null and Empty Handling

    [Fact]
    public void Insert_NullTags_TreatedAsNoTags()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        _repository.Insert("metric", null, baseTime, "value", 42.0);
        var result = _repository.GetLatest("metric", "value", null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(42.0, result.FieldValue);
    }

    [Fact]
    public void Insert_EmptyTagsDictionary_TreatedAsNoTags()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        _repository.Insert("metric", new Dictionary<string, string>(), baseTime, "value", 42.0);
        var result = _repository.GetLatest("metric", "value", new Dictionary<string, string>());

        // Assert
        Assert.NotNull(result);
        Assert.Equal(42.0, result.FieldValue);
    }

    [Fact]
    public void GetTimeSeries_EmptyMeasurementName_ReturnsEmpty()
    {
        // Act
        var results = _repository.GetTimeSeries("", "value", "last_10");

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public void GetTimeSeries_EmptyFieldName_HandlesGracefully()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repository.Insert("metric", null, baseTime, "value", 42.0);

        // Act
        var results = _repository.GetTimeSeries("metric", "", "last_10");

        // Assert
        Assert.NotNull(results);
    }

    #endregion

    #region Performance and Scale

    [Fact]
    public void GetTimeSeries_LargeDataset_PerformsReasonably()
    {
        // Arrange - Insert 1000 data points
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (int i = 0; i < 1000; i++)
        {
            _repository.Insert("metric", null, baseTime + i, "value", i * 1.0);
        }

        // Act - Query last 100
        var startTime = DateTime.UtcNow;
        var results = _repository.GetTimeSeries("metric", "value", "last_100");
        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        Assert.Equal(100, results.Count);
        Assert.True(elapsed.TotalMilliseconds < 1000, $"Query took {elapsed.TotalMilliseconds}ms, expected < 1000ms");
    }

    [Fact]
    public void GetAggregated_LargeDataset_PerformsReasonably()
    {
        // Arrange - Insert 1000 data points
        var baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (int i = 0; i < 1000; i++)
        {
            _repository.Insert("metric", null, baseTime + i, "value", i * 1.0);
        }

        // Act
        var startTime = DateTime.UtcNow;
        var result = _repository.GetAggregated("metric", "value", "last_500");
        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(500, result.Count);
        Assert.True(elapsed.TotalMilliseconds < 1000, $"Aggregation took {elapsed.TotalMilliseconds}ms, expected < 1000ms");
    }

    #endregion
}
