namespace ServerHub.Storage;

/// <summary>
/// Normalizes tag dictionaries by sorting keys alphabetically for consistent JSON serialization.
/// This ensures that {"core":"0","host":"srv01"} and {"host":"srv01","core":"0"} produce identical JSON.
/// </summary>
public static class TagNormalizer
{
    /// <summary>
    /// Sorts a tag dictionary by key alphabetically.
    /// </summary>
    /// <param name="tags">The tag dictionary to normalize. Can be null.</param>
    /// <returns>A new sorted dictionary, or null if input is null or empty.</returns>
    public static SortedDictionary<string, string>? Normalize(Dictionary<string, string>? tags)
    {
        if (tags == null || tags.Count == 0)
            return null;

        return new SortedDictionary<string, string>(tags, StringComparer.Ordinal);
    }

    /// <summary>
    /// Normalizes and serializes tags to JSON string.
    /// Manual JSON serialization to avoid reflection requirements.
    /// </summary>
    /// <param name="tags">The tag dictionary to serialize. Can be null.</param>
    /// <returns>JSON string or null if tags is null/empty.</returns>
    public static string? ToJson(Dictionary<string, string>? tags)
    {
        var normalized = Normalize(tags);
        if (normalized == null)
            return null;

        // Manual JSON serialization (simple dictionary, no reflection needed)
        var pairs = normalized.Select(kvp => $"\"{EscapeJson(kvp.Key)}\":\"{EscapeJson(kvp.Value)}\"");
        return "{" + string.Join(",", pairs) + "}";
    }

    /// <summary>
    /// Escapes a string for JSON (handles quotes, backslashes, etc.)
    /// </summary>
    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
