// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Xunit;
using ServerHub.Utils;
using System.Diagnostics;
using System.Text;

namespace ServerHub.Tests.Performance;

/// <summary>
/// Performance tests for ContentSanitizer.
/// Ensures sanitization operations complete within acceptable time bounds.
/// </summary>
public class SanitizationPerformanceTests
{
    #region Large Content Performance

    [Fact]
    public void Sanitize_Performance_LargeContent_Under1Second()
    {
        // Arrange - create large content with many lines
        var sb = new StringBuilder();
        for (int i = 0; i < 10000; i++)
        {
            sb.Append($"Line {i}: [process{i}] running\n");
        }
        var input = sb.ToString();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = ContentSanitizer.Sanitize(input);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 1000,
            $"Sanitization took {stopwatch.ElapsedMilliseconds}ms, expected < 1000ms");
        Assert.NotEmpty(result);
        Assert.Contains("[[process0]]", result);
    }

    [Fact]
    public void Sanitize_Performance_VeryLongLine_Under100Ms()
    {
        // Arrange - create a very long single line
        var input = new string('a', 100000) + "[invalid]" + new string('b', 100000);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = ContentSanitizer.Sanitize(input);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 100,
            $"Sanitization took {stopwatch.ElapsedMilliseconds}ms, expected < 100ms");
        Assert.Contains("[[invalid]]", result);
    }

    #endregion

    #region Many Small Calls Performance

    [Fact]
    public void Sanitize_Performance_1000SmallCalls_Under500Ms()
    {
        // Arrange - prepare 1000 different inputs
        var inputs = new string[1000];
        for (int i = 0; i < 1000; i++)
        {
            inputs[i] = $"[process{i}] running with [red]status[/]";
        }

        // Act
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            _ = ContentSanitizer.Sanitize(inputs[i]);
        }
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 500,
            $"1000 sanitizations took {stopwatch.ElapsedMilliseconds}ms, expected < 500ms");
    }

    [Fact]
    public void Sanitize_Performance_RepeatSameCalls_Under100Ms()
    {
        // Arrange - same input repeated (tests caching if any)
        var input = "[kworker/0:1] running at [green]85%[/]";

        // Act
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            _ = ContentSanitizer.Sanitize(input);
        }
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 100,
            $"1000 identical sanitizations took {stopwatch.ElapsedMilliseconds}ms, expected < 100ms");
    }

    #endregion

    #region Complex Content Performance

    [Fact]
    public void Sanitize_Performance_ComplexMixedContent_Under500Ms()
    {
        // Arrange - create complex mixed content
        var sb = new StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            sb.Append($"[red]Line {i}:[/] Process [kworker/{i}:{i % 10}] running\n");
            sb.Append($"CPU: [green]{i % 100}%[/] Memory: [yellow]{(i * 2) % 100}%[/]\n");
            sb.Append($"Status: [systemd-{i}] [migration/{i}] [ksoftirqd/{i}]\n");
        }
        var input = sb.ToString();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = ContentSanitizer.Sanitize(input);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 500,
            $"Complex content sanitization took {stopwatch.ElapsedMilliseconds}ms, expected < 500ms");
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Sanitize_Performance_ManyBrackets_Under200Ms()
    {
        // Arrange - content with many brackets to escape
        var sb = new StringBuilder();
        for (int i = 0; i < 5000; i++)
        {
            sb.Append($"[bracket{i}]");
        }
        var input = sb.ToString();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = ContentSanitizer.Sanitize(input);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 200,
            $"Many brackets sanitization took {stopwatch.ElapsedMilliseconds}ms, expected < 200ms");
        Assert.NotEmpty(result);
    }

    #endregion

    #region ANSI Code Stripping Performance

    [Fact]
    public void StripAnsiCodes_Performance_LargeContentWithAnsi_Under200Ms()
    {
        // Arrange - large content with many ANSI codes
        var sb = new StringBuilder();
        for (int i = 0; i < 10000; i++)
        {
            sb.Append($"\x1b[31mRed {i}\x1b[0m \x1b[32mGreen {i}\x1b[0m\n");
        }
        var input = sb.ToString();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = ContentSanitizer.StripAnsiCodes(input);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 200,
            $"ANSI stripping took {stopwatch.ElapsedMilliseconds}ms, expected < 200ms");
        Assert.NotEmpty(result);
        Assert.DoesNotContain("\x1b[", result);
    }

    #endregion

    #region Combined Operations Performance

    [Fact]
    public void Sanitize_Performance_AnsiAndBrackets_Under300Ms()
    {
        // Arrange - content with both ANSI codes and brackets to escape
        var sb = new StringBuilder();
        for (int i = 0; i < 5000; i++)
        {
            sb.Append($"\x1b[31m[process{i}]\x1b[0m running\n");
        }
        var input = sb.ToString();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = ContentSanitizer.Sanitize(input);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 300,
            $"Combined ANSI and bracket sanitization took {stopwatch.ElapsedMilliseconds}ms, expected < 300ms");
        Assert.NotEmpty(result);
        // Check that ANSI codes are stripped
        Assert.DoesNotContain('\x1b', result);
        // Check that brackets are escaped
        Assert.Contains("[[process0]]", result);
    }

    #endregion

    #region Baseline Performance Metrics

    [Fact]
    public void Sanitize_Performance_EmptyString_Under1Ms()
    {
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            _ = ContentSanitizer.Sanitize("");
        }
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 10,
            $"10000 empty string sanitizations took {stopwatch.ElapsedMilliseconds}ms, expected < 10ms");
    }

    [Fact]
    public void Sanitize_Performance_SimpleString_Under10Ms()
    {
        var input = "Simple text without special characters";

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            _ = ContentSanitizer.Sanitize(input);
        }
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 50,
            $"10000 simple string sanitizations took {stopwatch.ElapsedMilliseconds}ms, expected < 50ms");
    }

    #endregion

    #region Worst-Case Scenarios

    [Fact]
    public void Sanitize_Performance_NestedBrackets_Under200Ms()
    {
        // Arrange - worst case: deeply nested brackets
        var sb = new StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            sb.Append("[outer[inner[deep]]]");
        }
        var input = sb.ToString();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = ContentSanitizer.Sanitize(input);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 200,
            $"Nested brackets sanitization took {stopwatch.ElapsedMilliseconds}ms, expected < 200ms");
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Sanitize_Performance_AlternatingValidInvalid_Under500Ms()
    {
        // Arrange - alternating valid and invalid markup
        var sb = new StringBuilder();
        for (int i = 0; i < 5000; i++)
        {
            sb.Append($"[red]text[/] [invalid{i}] ");
        }
        var input = sb.ToString();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = ContentSanitizer.Sanitize(input);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 500,
            $"Alternating valid/invalid sanitization took {stopwatch.ElapsedMilliseconds}ms, expected < 500ms");
        Assert.NotEmpty(result);
    }

    #endregion
}
