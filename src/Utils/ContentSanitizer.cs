// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace ServerHub.Utils;

/// <summary>
/// Sanitizes widget content by stripping ANSI codes and escaping invalid Spectre.Console markup brackets.
/// This ensures widget output from scripts doesn't break the rendering pipeline.
/// </summary>
public static class ContentSanitizer
{
    // Regex to match ANSI escape sequences (colors, cursor movement, etc.)
    private static readonly Regex AnsiEscapeRegex = new(
        @"\x1b\[[0-9;]*[a-zA-Z]|\x1b\].*?(?:\x07|\x1b\\)",
        RegexOptions.Compiled);

    // Valid Spectre.Console markup patterns
    // Matches: [/], [color], [style], [color on color], [rgb(r,g,b)], [#hex], combinations
    private static readonly Regex ValidMarkupPattern = new(
        @"^\[" +
        @"(?:" +
            @"\/|" +                                                    // Closing tag [/]
            @"(?:bold|italic|underline|strikethrough|dim|link|blink|invert|conceal|rapidblink|slowblink)" +  // Styles
            @"(?:\s+(?:on\s+)?(?:[a-z]+\d*|rgb\s*\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*\)|#[0-9a-fA-F]{3,6}))?" +  // Optional color after style
            @"|" +
            @"(?:on\s+)?(?:[a-z]+\d*|rgb\s*\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*\)|#[0-9a-fA-F]{3,6})" +         // Colors (named, rgb, hex)
            @"(?:\s+on\s+(?:[a-z]+\d*|rgb\s*\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*\)|#[0-9a-fA-F]{3,6}))?" +       // Optional background
        @")" +
        @"\]$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Sanitizes content by stripping ANSI codes and escaping invalid brackets.
    /// Valid Spectre.Console markup tags are preserved.
    /// </summary>
    /// <param name="content">Raw content from widget script</param>
    /// <returns>Sanitized content safe for Spectre.Console rendering</returns>
    public static string Sanitize(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        try
        {
            // Step 1: Strip ANSI escape codes
            var stripped = StripAnsiCodes(content);

            // Step 2: Escape brackets that aren't valid Spectre markup
            return EscapeInvalidBrackets(stripped);
        }
        catch (Exception)
        {
            // Fallback: If sanitization fails, use aggressive escaping (escape ALL brackets)
            // This ensures content is always safe, even if less sophisticated
            return EscapeAllBrackets(content);
        }
    }

    /// <summary>
    /// Strips ANSI escape sequences from text.
    /// These come from command output and are not valid Spectre markup.
    /// </summary>
    public static string StripAnsiCodes(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        try
        {
            return AnsiEscapeRegex.Replace(text, "");
        }
        catch
        {
            // If regex fails, return original text (safer than empty)
            return text;
        }
    }

    /// <summary>
    /// Escapes brackets that don't form valid Spectre.Console markup.
    /// Valid tags like [red], [bold], [/], [cyan1], [rgb(255,0,0)] are preserved.
    /// Invalid brackets like [kworker/0:1] are escaped to [[kworker/0:1]].
    /// </summary>
    public static string EscapeInvalidBrackets(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        try
        {
            var result = new StringBuilder(text.Length + 16);
            int i = 0;

            while (i < text.Length)
            {
                if (text[i] == '[')
                {
                    // Check if already escaped [[
                    if (i + 1 < text.Length && text[i + 1] == '[')
                    {
                        result.Append("[[");
                        i += 2;
                        continue;
                    }

                    // Find the closing bracket
                    int closeBracket = FindClosingBracket(text, i);
                    if (closeBracket > i)
                    {
                        var potentialTag = text.Substring(i, closeBracket - i + 1);

                        if (IsValidMarkupTag(potentialTag))
                        {
                            // Valid markup - keep as is
                            result.Append(potentialTag);
                            i = closeBracket + 1;
                        }
                        else
                        {
                            // Not valid markup - escape the opening bracket
                            result.Append("[[");
                            i++;
                        }
                    }
                    else
                    {
                        // No closing bracket found - escape it
                        result.Append("[[");
                        i++;
                    }
                }
                else if (text[i] == ']')
                {
                    // Check if already escaped ]]
                    if (i + 1 < text.Length && text[i + 1] == ']')
                    {
                        result.Append("]]");
                        i += 2;
                    }
                    else
                    {
                        // Lone closing bracket - escape it
                        result.Append("]]");
                        i++;
                    }
                }
                else
                {
                    result.Append(text[i]);
                    i++;
                }
            }

            return result.ToString();
        }
        catch
        {
            // Fallback: Use aggressive escaping if sophisticated logic fails
            return text.Replace("[", "[[").Replace("]", "]]");
        }
    }

    /// <summary>
    /// Finds the closing bracket for an opening bracket, handling nesting.
    /// </summary>
    private static int FindClosingBracket(string text, int openPos)
    {
        // Simple search - find first ] after [
        // Don't handle nesting since Spectre tags don't nest within a single tag
        for (int i = openPos + 1; i < text.Length; i++)
        {
            if (text[i] == ']')
            {
                // Check it's not escaped
                if (i + 1 < text.Length && text[i + 1] == ']')
                {
                    i++; // Skip escaped ]]
                    continue;
                }
                return i;
            }
            // If we hit another [ before finding ], this bracket sequence is broken
            if (text[i] == '[' && (i + 1 >= text.Length || text[i + 1] != '['))
            {
                return -1;
            }
        }
        return -1;
    }

    /// <summary>
    /// Checks if a bracket expression is a valid Spectre.Console markup tag.
    /// Uses Spectre.Console's own parser to validate.
    /// </summary>
    private static bool IsValidMarkupTag(string tag)
    {
        // Closing tag [/] is always valid
        if (tag == "[/]")
            return true;

        // First check with regex for basic structure
        if (!ValidMarkupPattern.IsMatch(tag))
            return false;

        // Then validate with Spectre.Console to ensure color/style actually exists
        try
        {
            _ = new Markup(tag + "[/]"); // Try to parse it
            return true;
        }
        catch
        {
            return false; // Invalid Spectre markup
        }
    }

    /// <summary>
    /// Fallback sanitization: Aggressively escapes ALL brackets without validation.
    /// Used when sophisticated sanitization fails. Strips ANSI codes and escapes all [ and ].
    /// This sacrifices markup preservation for guaranteed safety.
    /// </summary>
    private static string EscapeAllBrackets(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        try
        {
            // Strip ANSI codes first
            var stripped = AnsiEscapeRegex.Replace(text, "");

            // Escape ALL brackets (no validation)
            return stripped
                .Replace("[", "[[")
                .Replace("]", "]]");
        }
        catch
        {
            // Ultimate fallback: return empty string rather than crash
            return string.Empty;
        }
    }
}
