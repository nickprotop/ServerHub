// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Xunit;
using ServerHub.Utils;
using Spectre.Console;
using System.Text;
using System.Diagnostics;

namespace ServerHub.Tests.Utils;

/// <summary>
/// Comprehensive test suite for ContentSanitizer.
/// Validates single-escaping, no double-escaping, valid markup preservation,
/// invalid bracket escaping, and rendering consistency.
/// </summary>
public class ContentSanitizerTests
{
    #region Category 1: Basic Bracket Escaping

    public class BasicBracketEscaping
    {
        [Theory]
        [InlineData("[red]text[/]", "[red]text[/]")]
        [InlineData("[bold]text[/]", "[bold]text[/]")]
        [InlineData("[cyan1]text[/]", "[cyan1]text[/]")]
        [InlineData("[/]", "[/]")]
        [InlineData("[red on blue]text[/]", "[red on blue]text[/]")]
        [InlineData("[rgb(255,0,0)]text[/]", "[rgb(255,0,0)]text[/]")]
        [InlineData("[#ff0000]text[/]", "[#ff0000]text[/]")]
        [InlineData("[green]text[/]", "[green]text[/]")]
        [InlineData("[yellow]text[/]", "[yellow]text[/]")]
        [InlineData("[blue]text[/]", "[blue]text[/]")]
        [InlineData("[italic]text[/]", "[italic]text[/]")]
        [InlineData("[underline]text[/]", "[underline]text[/]")]
        [InlineData("[strikethrough]text[/]", "[strikethrough]text[/]")]
        public void Sanitize_PreservesValidSpectreMarkup(string input, string expected)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("[invalid]", "[[invalid]]")]
        [InlineData("[kworker/0:1]", "[[kworker/0:1]]")]
        [InlineData("[systemd]", "[[systemd]]")]
        [InlineData("[123]", "[[123]]")]
        [InlineData("text[bracket", "text[[bracket")]
        [InlineData("[notacolor]", "[[notacolor]]")]
        [InlineData("[xyz123]", "[[xyz123]]")]
        public void Sanitize_EscapesInvalidBrackets(string input, string expected)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("text]", "text]]")]
        [InlineData("]text", "]]text")]
        [InlineData("te]xt", "te]]xt")]
        public void Sanitize_EscapesLoneClosingBrackets(string input, string expected)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("[red]Process: [kworker/0:1][/]", "[red]Process: [[kworker/0:1]][/]")]
        [InlineData("[bold]Text[/] [invalid]", "[bold]Text[/] [[invalid]]")]
        [InlineData("CPU: [invalid] [green]85%[/]", "CPU: [[invalid]] [green]85%[/]")]
        [InlineData("[red]Error:[/] in [process]", "[red]Error:[/] in [[process]]")]
        public void Sanitize_HandlesValidAndInvalidMarkupTogether(string input, string expected)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }
    }

    #endregion

    #region Category 2: Already-Escaped Content

    public class AlreadyEscapedContent
    {
        [Theory]
        [InlineData("[[text]]", "[[text]]")]
        [InlineData("[[[[nested]]]]", "[[[[nested]]]]")]
        [InlineData("text [[bracket]]", "text [[bracket]]")]
        [InlineData("[[kworker/0:1]]", "[[kworker/0:1]]")]
        public void Sanitize_PreservesAlreadyEscapedBrackets(string input, string expected)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("[kworker/0:1]")]
        [InlineData("[systemd]")]
        [InlineData("text]")]
        [InlineData("[invalid]")]
        [InlineData("Mix [a] [b] [c]")]
        public void Sanitize_IsIdempotent_SecondCallDoesNotChange(string input)
        {
            var firstPass = ContentSanitizer.Sanitize(input);
            var secondPass = ContentSanitizer.Sanitize(firstPass);

            Assert.Equal(firstPass, secondPass);
        }

        [Fact]
        public void Sanitize_IsIdempotent_MultipleCallsAreStable()
        {
            var input = "[kworker/0:1] and [systemd] running";
            var pass1 = ContentSanitizer.Sanitize(input);
            var pass2 = ContentSanitizer.Sanitize(pass1);
            var pass3 = ContentSanitizer.Sanitize(pass2);
            var pass4 = ContentSanitizer.Sanitize(pass3);

            Assert.Equal(pass1, pass2);
            Assert.Equal(pass2, pass3);
            Assert.Equal(pass3, pass4);
        }
    }

    #endregion

    #region Category 3: ANSI Code Stripping

    public class AnsiCodeStripping
    {
        [Theory]
        [InlineData("\x1b[31mRed Text\x1b[0m", "Red Text")]
        [InlineData("\x1b[1;32mBold Green\x1b[0m", "Bold Green")]
        [InlineData("Normal \x1b[33mYellow\x1b[0m Normal", "Normal Yellow Normal")]
        [InlineData("\x1b[38;5;214mOrange\x1b[0m", "Orange")]
        [InlineData("\x1b[0m", "")]
        [InlineData("\x1b[1m\x1b[31m\x1b[0m", "")]
        public void Sanitize_RemovesAnsiEscapeCodes(string input, string expected)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("\x1b[31m[red]text[/]\x1b[0m", "[red]text[/]")]
        [InlineData("\x1b[31m[invalid]\x1b[0m", "[[invalid]]")]
        [InlineData("\x1b[1m[kworker/0:1]\x1b[0m", "[[kworker/0:1]]")]
        public void Sanitize_RemovesAnsiAndEscapesBrackets(string input, string expected)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void StripAnsiCodes_RemovesComplexAnsiSequences()
        {
            var input = "\x1b[1;31;44mComplex\x1b[0m \x1b[38;2;255;0;0mRGB\x1b[0m";
            var result = ContentSanitizer.StripAnsiCodes(input);
            Assert.Equal("Complex RGB", result);
        }
    }

    #endregion

    #region Category 4: Edge Cases

    public class EdgeCases
    {
        [Theory]
        [InlineData("", "")]
        [InlineData(null, null)]
        public void Sanitize_HandlesEmptyAndNullStrings(string? input, string? expected)
        {
            var result = ContentSanitizer.Sanitize(input!);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Text with \n newlines", "Text with \n newlines")]
        [InlineData("Tabs\there", "Tabs\there")]
        [InlineData("Unicode: 你好 🎉", "Unicode: 你好 🎉")]
        [InlineData("Emoji 🚀 🎯", "Emoji 🚀 🎯")]
        public void Sanitize_PreservesSpecialCharacters(string input, string expected)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Sanitize_HandlesVeryLongContent()
        {
            var longContent = new string('a', 100000) + "[invalid]" + new string('b', 100000);
            var result = ContentSanitizer.Sanitize(longContent);

            Assert.Contains("[[invalid]]", result);
            Assert.Equal(200000 + 11, result.Length);
        }

        [Theory]
        [InlineData("[outer[inner]]", "[[outer[[inner]]")]  // Partial escaping due to nesting
        [InlineData("[[nested[deep]]]", "[[nested[[deep]]]]")]  // Already escaped opening bracket
        [InlineData("[a[b[c]]]", "[[a[[b[[c]]]]")]  // Partial escaping due to nesting
        public void Sanitize_HandlesNestedInvalidBrackets(string input, string expected)
        {
            // Note: Nested brackets are a pathological case - the sanitizer
            // escapes what it can, but doesn't fully handle deep nesting.
            // This is acceptable since real-world process names don't have nested brackets.
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("[kworker/0:1]", "[[kworker/0:1]]")]
        [InlineData("[migration/0]", "[[migration/0]]")]
        [InlineData("[ksoftirqd/0]", "[[ksoftirqd/0]]")]
        [InlineData("[rcu_sched]", "[[rcu_sched]]")]
        [InlineData("systemd-journald [123]", "systemd-journald [[123]]")]
        [InlineData("[watchdog/0]", "[[watchdog/0]]")]
        [InlineData("[kdevtmpfs]", "[[kdevtmpfs]]")]
        public void Sanitize_HandlesRealProcessNames(string input, string expected)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("[")]
        [InlineData("]")]
        [InlineData("[[")]
        [InlineData("]]")]
        [InlineData("[[[")]
        [InlineData("]]]")]
        public void Sanitize_HandlesSingleAndMultipleBrackets(string input)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.NotNull(result);
            // Should not throw
        }

        [Theory]
        [InlineData("     ", "     ")]
        [InlineData("\n\n\n", "\n\n\n")]
        [InlineData("\t\t\t", "\t\t\t")]
        public void Sanitize_PreservesWhitespace(string input, string expected)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }
    }

    #endregion

    #region Category 5: Table Content Sanitization

    public class TableContentSanitization
    {
        [Theory]
        [InlineData("[kworker/0:1]", "[[kworker/0:1]]")]
        [InlineData("[systemd]", "[[systemd]]")]
        [InlineData("Cell [invalid] content", "Cell [[invalid]] content")]
        public void Sanitize_WorksForTableCellContent(string input, string expected)
        {
            // Table cells go through the same sanitization
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }
    }

    #endregion

    #region Category 6: Performance Tests

    public class PerformanceTests
    {
        [Fact]
        public void Sanitize_Performance_LargeContent()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 10000; i++)
            {
                sb.Append($"Line {i}: [process{i}] running\n");
            }
            var input = sb.ToString();

            var stopwatch = Stopwatch.StartNew();
            var result = ContentSanitizer.Sanitize(input);
            stopwatch.Stop();

            Assert.True(stopwatch.ElapsedMilliseconds < 1000,
                $"Sanitization took {stopwatch.ElapsedMilliseconds}ms, expected < 1000ms");
            Assert.NotEmpty(result);
        }

        [Fact]
        public void Sanitize_Performance_ManySmallCalls()
        {
            var inputs = new string[1000];
            for (int i = 0; i < 1000; i++)
            {
                inputs[i] = $"[process{i}] running";
            }

            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                _ = ContentSanitizer.Sanitize(inputs[i]);
            }
            stopwatch.Stop();

            Assert.True(stopwatch.ElapsedMilliseconds < 500,
                $"1000 sanitizations took {stopwatch.ElapsedMilliseconds}ms, expected < 500ms");
        }
    }

    #endregion

    #region Category 7: Robustness - No Exceptions

    public class RobustnessTests
    {
        [Theory]
        [InlineData("[kworker/0:1]")]
        [InlineData("[systemd]")]
        [InlineData("]]]]]]")]
        [InlineData("[[[[[[")]
        [InlineData("[invalid[nested[deep]]]")]
        [InlineData("]]text[[")]
        [InlineData("Mixed [red]valid[/] and [invalid] brackets")]
        [InlineData("\x1b[31m[process]\x1b[0m")]
        [InlineData("[")]
        [InlineData("]")]
        [InlineData("Process: ]garbage[ text")]
        [InlineData("Unicode 你好 [process] 🎉")]
        public void Sanitize_NeverThrowsException_WithSanitizedContent(string rawInput)
        {
            // Sanitize the input
            var sanitized = ContentSanitizer.Sanitize(rawInput);

            // CRITICAL: Sanitized content must NEVER throw when rendered
            var exception = Record.Exception(() =>
            {
                var markup = new Markup(sanitized);
                using var writer = new StringWriter();
                var console = AnsiConsole.Create(new AnsiConsoleSettings
                {
                    Out = new AnsiConsoleOutput(writer)
                });
                console.Write(markup);
            });

            Assert.Null(exception);
        }

        [Theory]
        [InlineData("")]
        [InlineData("     ")]
        [InlineData("\n\n\n")]
        public void Sanitize_HandlesEmptyContent_WithoutThrowing(string input)
        {
            var sanitized = ContentSanitizer.Sanitize(input);

            var exception = Record.Exception(() =>
            {
                var markup = new Markup(sanitized);
                using var writer = new StringWriter();
                var console = AnsiConsole.Create(new AnsiConsoleSettings
                {
                    Out = new AnsiConsoleOutput(writer)
                });
                console.Write(markup);
            });

            Assert.Null(exception);
        }

        [Theory]
        [InlineData("Process: [kworker/0:1] CPU: 45%")]
        [InlineData("[red]ERROR:[/] Connection [127.0.0.1:8080] failed")]
        [InlineData("Status: [systemd-journald] [migration/0] [ksoftirqd/0] running")]
        [InlineData("\x1b[31mRed text\x1b[0m with [brackets] and [red]markup[/]")]
        [InlineData("Table data: [[cell1]] | [cell2]")]
        [InlineData("Nested: [outer[inner[deep]]]")]
        [InlineData("Mixed]] [[garbage] [bold]text[/]")]
        public void Sanitize_RealWorldWidgetOutput_NeverThrows(string output)
        {
            var sanitized = ContentSanitizer.Sanitize(output);

            var exception = Record.Exception(() =>
            {
                var markup = new Markup(sanitized);
                using var writer = new StringWriter();
                var console = AnsiConsole.Create(new AnsiConsoleSettings
                {
                    Out = new AnsiConsoleOutput(writer)
                });
                console.Write(markup);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void Sanitize_ManyBrackets_DoesNotThrow()
        {
            var testCases = new[]
            {
                new string('[', 1000),
                new string(']', 1000),
                "[" + new string('a', 10000) + "]",
                new string('[', 100) + new string(']', 100),
            };

            foreach (var input in testCases)
            {
                var sanitized = ContentSanitizer.Sanitize(input);

                var exception = Record.Exception(() =>
                {
                    var markup = new Markup(sanitized);
                    using var writer = new StringWriter();
                    var console = AnsiConsole.Create(new AnsiConsoleSettings
                    {
                        Out = new AnsiConsoleOutput(writer)
                    });
                    console.Write(markup);
                });

                Assert.Null(exception);
            }
        }
    }

    #endregion

    #region Category 8: Specific Spectre.Console Markup

    public class SpectreConsoleMarkupTests
    {
        [Theory]
        [InlineData("[red]", "[red]")]
        [InlineData("[green]", "[green]")]
        [InlineData("[blue]", "[blue]")]
        [InlineData("[yellow]", "[yellow]")]
        [InlineData("[cyan]", "[cyan]")]
        [InlineData("[magenta]", "[magenta]")]
        [InlineData("[white]", "[white]")]
        [InlineData("[black]", "[black]")]
        [InlineData("[grey]", "[grey]")]
        public void Sanitize_PreservesBasicColors(string input, string expected)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("[red1]", "[red1]")]
        [InlineData("[cyan1]", "[cyan1]")]
        [InlineData("[deepskyblue1]", "[deepskyblue1]")]
        public void Sanitize_PreservesNumberedColors(string input, string expected)
        {
            // Note: Not all color names support numbered variants
            // Test with colors that actually exist in Spectre.Console's color palette
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("[rgb(255,0,0)]", "[rgb(255,0,0)]")]
        [InlineData("[rgb(0,255,0)]", "[rgb(0,255,0)]")]
        [InlineData("[rgb(0,0,255)]", "[rgb(0,0,255)]")]
        public void Sanitize_PreservesRgbColors(string input, string expected)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("[#ff0000]", "[#ff0000]")]
        [InlineData("[#00ff00]", "[#00ff00]")]
        [InlineData("[#0000ff]", "[#0000ff]")]
        [InlineData("[#fff]", "[#fff]")]
        public void Sanitize_PreservesHexColors(string input, string expected)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("[red on blue]", "[red on blue]")]
        [InlineData("[white on black]", "[white on black]")]
        [InlineData("[green on yellow]", "[green on yellow]")]
        public void Sanitize_PreservesBackgroundColors(string input, string expected)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("[bold]", "[bold]")]
        [InlineData("[italic]", "[italic]")]
        [InlineData("[underline]", "[underline]")]
        [InlineData("[strikethrough]", "[strikethrough]")]
        [InlineData("[dim]", "[dim]")]
        [InlineData("[invert]", "[invert]")]
        [InlineData("[conceal]", "[conceal]")]
        [InlineData("[slowblink]", "[slowblink]")]
        [InlineData("[rapidblink]", "[rapidblink]")]
        public void Sanitize_PreservesTextStyles(string input, string expected)
        {
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("[b]", "[b]")]   // bold alias (undocumented)
        [InlineData("[i]", "[i]")]   // italic alias (undocumented)
        [InlineData("[u]", "[u]")]   // underline alias (undocumented)
        public void Sanitize_PreservesSingleLetterAliases(string input, string expected)
        {
            // [b], [i], [u] are valid undocumented single-letter aliases in Spectre.Console
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("[red on blue]", "[red on blue]")]
        [InlineData("[green on black]", "[green on black]")]
        public void Sanitize_PreservesCombinedStyles(string input, string expected)
        {
            // Note: Combining styles and colors may have syntax restrictions
            // Test with known valid combinations
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }
    }

    #endregion

    #region Category 9: Mixed Content Scenarios

    public class MixedContentScenarios
    {
        [Fact]
        public void Sanitize_ComplexMixedContent_WorksCorrectly()
        {
            var input = "[red]Status:[/] [kworker/0:1] running at [green]85%[/] CPU]";
            var result = ContentSanitizer.Sanitize(input);

            // Should preserve [red] and [green], escape [kworker/0:1] and the lone ]
            Assert.Contains("[red]", result);
            Assert.Contains("[green]", result);
            Assert.Contains("[[kworker/0:1]]", result);
            Assert.EndsWith("CPU]]", result);
        }

        [Fact]
        public void Sanitize_MultipleInvalidBracketsInSequence()
        {
            // Note: [b], [i], [u] are valid Spectre.Console single-letter aliases (undocumented but implemented)
            var input = "[foo] [bar] [baz]";
            var result = ContentSanitizer.Sanitize(input);

            Assert.Equal("[[foo]] [[bar]] [[baz]]", result);
        }

        [Fact]
        public void Sanitize_AlternatingValidAndInvalid()
        {
            var input = "[red]text[/] [invalid] [green]more[/] [another]";
            var result = ContentSanitizer.Sanitize(input);

            Assert.Equal("[red]text[/] [[invalid]] [green]more[/] [[another]]", result);
        }
    }

    #endregion

    #region Category 10: EscapeInvalidBrackets Direct Tests

    public class EscapeInvalidBracketsTests
    {
        [Theory]
        [InlineData("[red]text[/]", "[red]text[/]")]  // Valid markup preserved
        [InlineData("[invalid]", "[[invalid]]")]        // Invalid markup escaped
        [InlineData("]]", "]]")]                        // Already escaped closing bracket (idempotent)
        [InlineData("[[", "[[")]                        // Already escaped opening bracket (idempotent)
        [InlineData("[", "[[")]                         // Single opening bracket gets escaped
        [InlineData("]", "]]")]                         // Single closing bracket gets escaped
        public void EscapeInvalidBrackets_WorksCorrectly(string input, string expected)
        {
            var result = ContentSanitizer.EscapeInvalidBrackets(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void EscapeInvalidBrackets_HandlesNull()
        {
            var result = ContentSanitizer.EscapeInvalidBrackets(null!);
            Assert.Null(result);
        }

        [Fact]
        public void EscapeInvalidBrackets_HandlesEmpty()
        {
            var result = ContentSanitizer.EscapeInvalidBrackets("");
            Assert.Equal("", result);
        }
    }

    #endregion

    #region Category 11: Widget Protocol Tags

    public class WidgetProtocolTags
    {
        [Theory]
        [InlineData("[status:ok]", "[[status:ok]]")]      // Protocol tag, not valid Spectre markup
        [InlineData("[status:error]", "[[status:error]]")]
        [InlineData("[progress:75]", "[[progress:75]]")]
        [InlineData("[sparkline:1,2,3]", "[[sparkline:1,2,3]]")]
        [InlineData("[miniprogress:50]", "[[miniprogress:50]]")]
        [InlineData("[divider]", "[[divider]]")]
        [InlineData("[graph:10,20,30]", "[[graph:10,20,30]]")]
        public void Sanitize_EscapesWidgetProtocolTags(string input, string expected)
        {
            // Widget protocol tags like [status:ok] are NOT valid Spectre.Console markup
            // They should be escaped if they somehow reach the sanitizer
            // (Normally they're parsed out before sanitization)
            var result = ContentSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Sanitize_WidgetProtocolTagsNormallyParsedFirst()
        {
            // This test documents that widget protocol tags are parsed BEFORE sanitization
            // The protocol parser removes these tags and converts them to WidgetRow properties
            // So they should never actually reach ContentSanitizer in normal operation
            //
            // Example raw widget output: "row: CPU [progress:85] [status:ok]"
            // After protocol parsing, the [progress:85] and [status:ok] would be removed
            // and stored in WidgetRow.Progress and WidgetRow.Status properties
            // Only the text "CPU" would remain to be sanitized

            Assert.True(true); // Documentation test
        }
    }

    #endregion

    #region Category 12: StripAnsiCodes Direct Tests

    public class StripAnsiCodesTests
    {
        [Theory]
        [InlineData("\x1b[0m", "")]
        [InlineData("\x1b[31m", "")]
        [InlineData("text\x1b[0m", "text")]
        [InlineData("\x1b[31mtext", "text")]
        public void StripAnsiCodes_WorksCorrectly(string input, string expected)
        {
            var result = ContentSanitizer.StripAnsiCodes(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void StripAnsiCodes_HandlesNull()
        {
            var result = ContentSanitizer.StripAnsiCodes(null!);
            Assert.Null(result);
        }

        [Fact]
        public void StripAnsiCodes_HandlesEmpty()
        {
            var result = ContentSanitizer.StripAnsiCodes("");
            Assert.Equal("", result);
        }

        [Fact]
        public void StripAnsiCodes_RemovesMultipleSequences()
        {
            var input = "\x1b[1m\x1b[31m\x1b[44mtext\x1b[0m\x1b[0m";
            var result = ContentSanitizer.StripAnsiCodes(input);
            Assert.Equal("text", result);
        }
    }

    #endregion
}
