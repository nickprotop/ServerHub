using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace ServerHub.Storage;

/// <summary>
/// Repository for widget data CRUD operations. Scoped to a specific widget_id.
/// </summary>
public class WidgetDataRepository
{
    private readonly SqliteConnection _connection;
    private readonly string _widgetId;

    /// <summary>
    /// Creates a new repository instance scoped to a specific widget.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="widgetId">The widget identifier.</param>
    public WidgetDataRepository(SqliteConnection connection, string widgetId)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _widgetId = widgetId ?? throw new ArgumentNullException(nameof(widgetId));
    }

    /// <summary>
    /// Inserts a data point into the database.
    /// </summary>
    /// <param name="measurement">The measurement name (e.g., "cpu", "memory").</param>
    /// <param name="tags">Optional tags for grouping (e.g., {"core":"0","host":"srv01"}).</param>
    /// <param name="timestamp">Unix timestamp in seconds.</param>
    /// <param name="fieldName">The field name (e.g., "usage", "temperature").</param>
    /// <param name="fieldValue">Numeric field value (optional).</param>
    /// <param name="fieldText">String field value (optional).</param>
    /// <param name="fieldJson">JSON field value (optional).</param>
    public void Insert(
        string measurement,
        Dictionary<string, string>? tags,
        long timestamp,
        string fieldName,
        double? fieldValue = null,
        string? fieldText = null,
        string? fieldJson = null)
    {
        if (string.IsNullOrWhiteSpace(measurement))
            throw new ArgumentException("Measurement cannot be empty", nameof(measurement));
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("Field name cannot be empty", nameof(fieldName));
        if (fieldValue == null && fieldText == null && fieldJson == null)
            throw new ArgumentException("At least one field value must be provided");

        var tagsJson = TagNormalizer.ToJson(tags);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO widget_data
                (widget_id, measurement, tags, timestamp, field_name, field_value, field_text, field_json)
            VALUES
                (@widget_id, @measurement, @tags, @timestamp, @field_name, @field_value, @field_text, @field_json);
        ";

        cmd.Parameters.AddWithValue("@widget_id", _widgetId);
        cmd.Parameters.AddWithValue("@measurement", measurement);
        cmd.Parameters.AddWithValue("@tags", (object?)tagsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@timestamp", timestamp);
        cmd.Parameters.AddWithValue("@field_name", fieldName);
        cmd.Parameters.AddWithValue("@field_value", (object?)fieldValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@field_text", (object?)fieldText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@field_json", (object?)fieldJson ?? DBNull.Value);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Gets the latest value for a specific measurement and field.
    /// </summary>
    /// <param name="measurement">The measurement name.</param>
    /// <param name="fieldName">The field name.</param>
    /// <param name="tags">Optional tags to filter by.</param>
    /// <returns>The latest data point, or null if not found.</returns>
    public DataPoint? GetLatest(string measurement, string fieldName, Dictionary<string, string>? tags = null)
    {
        var tagsJson = TagNormalizer.ToJson(tags);

        using var cmd = _connection.CreateCommand();
        if (tagsJson != null)
        {
            cmd.CommandText = @"
                SELECT timestamp, field_value, field_text, field_json
                FROM widget_data
                WHERE widget_id = @widget_id
                  AND measurement = @measurement
                  AND field_name = @field_name
                  AND tags = @tags
                ORDER BY timestamp DESC
                LIMIT 1;
            ";
            cmd.Parameters.AddWithValue("@tags", tagsJson);
        }
        else
        {
            cmd.CommandText = @"
                SELECT timestamp, field_value, field_text, field_json
                FROM widget_data
                WHERE widget_id = @widget_id
                  AND measurement = @measurement
                  AND field_name = @field_name
                  AND tags IS NULL
                ORDER BY timestamp DESC
                LIMIT 1;
            ";
        }

        cmd.Parameters.AddWithValue("@widget_id", _widgetId);
        cmd.Parameters.AddWithValue("@measurement", measurement);
        cmd.Parameters.AddWithValue("@field_name", fieldName);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new DataPoint
            {
                Timestamp = reader.GetInt64(0),
                FieldValue = reader.IsDBNull(1) ? null : reader.GetDouble(1),
                FieldText = reader.IsDBNull(2) ? null : reader.GetString(2),
                FieldJson = reader.IsDBNull(3) ? null : reader.GetString(3)
            };
        }

        return null;
    }

    /// <summary>
    /// Gets a time series of data points for a specific measurement and field.
    /// </summary>
    /// <param name="measurement">The measurement name.</param>
    /// <param name="fieldName">The field name.</param>
    /// <param name="timeRange">Time range string (e.g., "1h", "30s", "last_10").</param>
    /// <param name="tags">Optional tags to filter by.</param>
    /// <returns>List of data points ordered by timestamp ascending.</returns>
    public List<DataPoint> GetTimeSeries(
        string measurement,
        string fieldName,
        string timeRange,
        Dictionary<string, string>? tags = null)
    {
        var parseResult = TimeRangeParser.Parse(timeRange);
        var tagsJson = TagNormalizer.ToJson(tags);

        using var cmd = _connection.CreateCommand();

        if (parseResult.IsTimeBased)
        {
            // Time-based query
            if (tagsJson != null)
            {
                cmd.CommandText = @"
                    SELECT timestamp, field_value, field_text, field_json
                    FROM widget_data
                    WHERE widget_id = @widget_id
                      AND measurement = @measurement
                      AND field_name = @field_name
                      AND tags = @tags
                      AND timestamp >= @start_timestamp
                    ORDER BY timestamp ASC;
                ";
                cmd.Parameters.AddWithValue("@tags", tagsJson);
            }
            else
            {
                cmd.CommandText = @"
                    SELECT timestamp, field_value, field_text, field_json
                    FROM widget_data
                    WHERE widget_id = @widget_id
                      AND measurement = @measurement
                      AND field_name = @field_name
                      AND tags IS NULL
                      AND timestamp >= @start_timestamp
                    ORDER BY timestamp ASC;
                ";
            }
            cmd.Parameters.AddWithValue("@start_timestamp", parseResult.StartTimestamp!.Value);
        }
        else
        {
            // Sample-based query (last N samples)
            if (tagsJson != null)
            {
                cmd.CommandText = @"
                    SELECT timestamp, field_value, field_text, field_json
                    FROM widget_data
                    WHERE widget_id = @widget_id
                      AND measurement = @measurement
                      AND field_name = @field_name
                      AND tags = @tags
                    ORDER BY timestamp DESC
                    LIMIT @limit;
                ";
                cmd.Parameters.AddWithValue("@tags", tagsJson);
            }
            else
            {
                cmd.CommandText = @"
                    SELECT timestamp, field_value, field_text, field_json
                    FROM widget_data
                    WHERE widget_id = @widget_id
                      AND measurement = @measurement
                      AND field_name = @field_name
                      AND tags IS NULL
                    ORDER BY timestamp DESC
                    LIMIT @limit;
                ";
            }
            cmd.Parameters.AddWithValue("@limit", parseResult.SampleCount!.Value);
        }

        cmd.Parameters.AddWithValue("@widget_id", _widgetId);
        cmd.Parameters.AddWithValue("@measurement", measurement);
        cmd.Parameters.AddWithValue("@field_name", fieldName);

        var results = new List<DataPoint>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new DataPoint
            {
                Timestamp = reader.GetInt64(0),
                FieldValue = reader.IsDBNull(1) ? null : reader.GetDouble(1),
                FieldText = reader.IsDBNull(2) ? null : reader.GetString(2),
                FieldJson = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }

        // For sample-based queries, reverse to get chronological order
        if (!parseResult.IsTimeBased)
            results.Reverse();

        return results;
    }

    /// <summary>
    /// Gets aggregated statistics for a measurement over a time range.
    /// </summary>
    /// <param name="measurement">The measurement name.</param>
    /// <param name="fieldName">The field name (must be numeric).</param>
    /// <param name="timeRange">Time range string (e.g., "1h", "30s").</param>
    /// <param name="tags">Optional tags to filter by.</param>
    /// <returns>Aggregated statistics, or null if no data found.</returns>
    public AggregatedData? GetAggregated(
        string measurement,
        string fieldName,
        string timeRange,
        Dictionary<string, string>? tags = null)
    {
        var parseResult = TimeRangeParser.Parse(timeRange);
        if (!parseResult.IsTimeBased)
            throw new ArgumentException("Aggregation requires time-based range (e.g., '1h'), not sample count", nameof(timeRange));

        var tagsJson = TagNormalizer.ToJson(tags);

        using var cmd = _connection.CreateCommand();

        if (tagsJson != null)
        {
            cmd.CommandText = @"
                SELECT
                    COUNT(*) as count,
                    AVG(field_value) as avg,
                    MIN(field_value) as min,
                    MAX(field_value) as max,
                    SUM(field_value) as sum
                FROM widget_data
                WHERE widget_id = @widget_id
                  AND measurement = @measurement
                  AND field_name = @field_name
                  AND tags = @tags
                  AND timestamp >= @start_timestamp
                  AND field_value IS NOT NULL;
            ";
            cmd.Parameters.AddWithValue("@tags", tagsJson);
        }
        else
        {
            cmd.CommandText = @"
                SELECT
                    COUNT(*) as count,
                    AVG(field_value) as avg,
                    MIN(field_value) as min,
                    MAX(field_value) as max,
                    SUM(field_value) as sum
                FROM widget_data
                WHERE widget_id = @widget_id
                  AND measurement = @measurement
                  AND field_name = @field_name
                  AND tags IS NULL
                  AND timestamp >= @start_timestamp
                  AND field_value IS NOT NULL;
            ";
        }

        cmd.Parameters.AddWithValue("@widget_id", _widgetId);
        cmd.Parameters.AddWithValue("@measurement", measurement);
        cmd.Parameters.AddWithValue("@field_name", fieldName);
        cmd.Parameters.AddWithValue("@start_timestamp", parseResult.StartTimestamp!.Value);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var count = reader.GetInt64(0);
            if (count == 0)
                return null;

            return new AggregatedData
            {
                Count = count,
                Average = reader.IsDBNull(1) ? null : reader.GetDouble(1),
                Min = reader.IsDBNull(2) ? null : reader.GetDouble(2),
                Max = reader.IsDBNull(3) ? null : reader.GetDouble(3),
                Sum = reader.IsDBNull(4) ? null : reader.GetDouble(4)
            };
        }

        return null;
    }

    /// <summary>
    /// Deletes data older than the specified retention period.
    /// </summary>
    /// <param name="retentionPeriod">Time range to keep (e.g., "7d", "30d").</param>
    /// <returns>Number of rows deleted.</returns>
    public int DeleteOlderThan(string retentionPeriod)
    {
        var parseResult = TimeRangeParser.Parse(retentionPeriod);
        if (!parseResult.IsTimeBased)
            throw new ArgumentException("Retention period must be time-based (e.g., '7d'), not sample count", nameof(retentionPeriod));

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM widget_data
            WHERE widget_id = @widget_id
              AND timestamp < @cutoff_timestamp;
        ";
        cmd.Parameters.AddWithValue("@widget_id", _widgetId);
        cmd.Parameters.AddWithValue("@cutoff_timestamp", parseResult.StartTimestamp!.Value);

        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Represents a single data point.
    /// </summary>
    public class DataPoint
    {
        public long Timestamp { get; init; }
        public double? FieldValue { get; init; }
        public string? FieldText { get; init; }
        public string? FieldJson { get; init; }
    }

    /// <summary>
    /// Represents aggregated statistics.
    /// </summary>
    public class AggregatedData
    {
        public long Count { get; init; }
        public double? Average { get; init; }
        public double? Min { get; init; }
        public double? Max { get; init; }
        public double? Sum { get; init; }
    }
}
