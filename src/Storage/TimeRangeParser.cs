using System.Text.RegularExpressions;

namespace ServerHub.Storage;

/// <summary>
/// Parses time range strings like "1h", "30s", "7d", "last_10" into TimeSpan or sample count.
/// Supports:
/// - Seconds: "10s", "30s"
/// - Minutes: "5m", "15m"
/// - Hours: "1h", "24h"
/// - Days: "7d", "30d"
/// - Samples: "last_10", "last_100"
/// </summary>
public static class TimeRangeParser
{
    private static readonly Regex TimeRangeRegex = new(@"^(\d+)([smhd])$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SampleCountRegex = new(@"^last_(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Result of parsing a time range string.
    /// </summary>
    public class ParseResult
    {
        /// <summary>
        /// True if the range is a time duration, false if it's a sample count.
        /// </summary>
        public bool IsTimeBased { get; init; }

        /// <summary>
        /// The parsed time duration (only valid if IsTimeBased is true).
        /// </summary>
        public TimeSpan? Duration { get; init; }

        /// <summary>
        /// The sample count (only valid if IsTimeBased is false).
        /// </summary>
        public int? SampleCount { get; init; }

        /// <summary>
        /// The Unix timestamp (seconds) for the start of the time range (only valid if IsTimeBased is true).
        /// </summary>
        public long? StartTimestamp { get; init; }
    }

    /// <summary>
    /// Parses a time range string.
    /// </summary>
    /// <param name="range">The time range string (e.g., "1h", "30s", "last_10").</param>
    /// <param name="nowTimestamp">Current Unix timestamp in seconds (default is DateTimeOffset.UtcNow).</param>
    /// <returns>ParseResult with duration or sample count.</returns>
    /// <exception cref="ArgumentException">If the format is invalid.</exception>
    public static ParseResult Parse(string range, long? nowTimestamp = null)
    {
        if (string.IsNullOrWhiteSpace(range))
            throw new ArgumentException("Time range cannot be empty", nameof(range));

        // Check for sample count format: "last_N"
        var sampleMatch = SampleCountRegex.Match(range);
        if (sampleMatch.Success)
        {
            var count = int.Parse(sampleMatch.Groups[1].Value);
            if (count <= 0)
                throw new ArgumentException($"Sample count must be positive: {range}", nameof(range));

            return new ParseResult
            {
                IsTimeBased = false,
                SampleCount = count
            };
        }

        // Check for time duration format: "10s", "5m", "1h", "7d"
        var timeMatch = TimeRangeRegex.Match(range);
        if (timeMatch.Success)
        {
            var value = int.Parse(timeMatch.Groups[1].Value);
            var unit = timeMatch.Groups[2].Value.ToLowerInvariant();

            if (value <= 0)
                throw new ArgumentException($"Time value must be positive: {range}", nameof(range));

            TimeSpan duration = unit switch
            {
                "s" => TimeSpan.FromSeconds(value),
                "m" => TimeSpan.FromMinutes(value),
                "h" => TimeSpan.FromHours(value),
                "d" => TimeSpan.FromDays(value),
                _ => throw new ArgumentException($"Invalid time unit: {unit}", nameof(range))
            };

            var now = nowTimestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var startTimestamp = now - (long)duration.TotalSeconds;

            return new ParseResult
            {
                IsTimeBased = true,
                Duration = duration,
                StartTimestamp = startTimestamp
            };
        }

        throw new ArgumentException($"Invalid time range format: {range}. Expected formats: '10s', '5m', '1h', '7d', or 'last_10'", nameof(range));
    }

    /// <summary>
    /// Tries to parse a time range string.
    /// </summary>
    /// <param name="range">The time range string.</param>
    /// <param name="result">The parse result if successful.</param>
    /// <param name="nowTimestamp">Current Unix timestamp in seconds (default is DateTimeOffset.UtcNow).</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryParse(string range, out ParseResult? result, long? nowTimestamp = null)
    {
        try
        {
            result = Parse(range, nowTimestamp);
            return true;
        }
        catch
        {
            result = null;
            return false;
        }
    }
}
