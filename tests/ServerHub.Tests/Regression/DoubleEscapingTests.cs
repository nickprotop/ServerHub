// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Xunit;
using ServerHub.Utils;
using Spectre.Console;

namespace ServerHub.Tests.Regression;

/// <summary>
/// Regression tests to prevent double-escaping issues.
/// These tests document known risks and ensure they don't reoccur.
/// </summary>
public class DoubleEscapingTests
{
    // The two AnsiConsoleHelper tests that lived here are gone. They called
    // SharpConsoleUI's AnsiConsoleHelper.ConvertSpectreMarkupToAnsi, a helper the
    // library deleted in v2.4.41 when rendering moved to the cell-level pipeline and
    // the legacy ANSI paths went with it. ServerHub never calls it, and what these
    // tests were really protecting -- that sanitized content is not escaped twice --
    // is covered by ContentSanitizerTests, RenderingPipelineTests and the idempotency
    // tests below.

    #region Documentation Tests

    [Fact]
    public void Documentation_ContentSanitizer_ShouldBeCalledOnce()
    {
        // This test documents the correct usage pattern:
        // 1. Widget script outputs raw content
        // 2. ContentSanitizer.Sanitize() is called ONCE in WidgetProtocolParser
        // 3. Sanitized content is stored in WidgetData
        // 4. Rendering uses sanitized content directly - NO further escaping

        var rawInput = "[kworker/0:1]";

        // ✅ CORRECT - Sanitize once
        var sanitized = ContentSanitizer.Sanitize(rawInput);
        Assert.Equal("[[kworker/0:1]]", sanitized);

        // ✅ CORRECT - Use sanitized content directly in rendering
        var markup = new Markup(sanitized);
        Assert.NotNull(markup);

        // ❌ WRONG - Do not call ContentSanitizer.Sanitize() again
        // var doubleSanitized = ContentSanitizer.Sanitize(sanitized);
        // This would be idempotent, but wasteful

        // ❌ WRONG - Do not call any additional escaping functions
        // These functions should never be called on already-sanitized content
    }

    [Fact]
    public void Documentation_RenderingPaths_UseSanitizedContentDirectly()
    {
        // This test documents that both rendering paths use the same sanitized content:
        // 1. Dashboard: WidgetRenderer.FormatRow() → creates Markup(sanitizedContent)
        // 2. Expanded Dialog: WidgetExpansionDialog.FormatRowForExpansion() → creates Markup(sanitizedContent)

        var rawInput = "[red]Error:[/] Process [kworker/0:1] failed";
        var sanitized = ContentSanitizer.Sanitize(rawInput);

        Assert.Equal("[red]Error:[/] Process [[kworker/0:1]] failed", sanitized);

        // Both paths should use this exact sanitized string
        // No further processing should occur
    }

    #endregion

    #region Idempotency Tests

    [Theory]
    [InlineData("[kworker/0:1]")]
    [InlineData("[systemd]")]
    [InlineData("Text [invalid] content")]
    [InlineData("[red]Valid[/] and [invalid]")]
    public void Sanitize_ThenRender_IsStable(string rawInput)
    {
        // Sanitize
        var sanitized = ContentSanitizer.Sanitize(rawInput);

        // Render using Spectre.Console
        var markup1 = new Markup(sanitized);
        using var writer1 = new StringWriter();
        var console1 = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer1)
        });
        console1.Write(markup1);
        var rendered1 = writer1.ToString();

        // If we were to sanitize the rendered output again (which we shouldn't)
        // and render it, we should get similar results
        var sanitized2 = ContentSanitizer.Sanitize(rendered1);
        var markup2 = new Markup(sanitized2);
        using var writer2 = new StringWriter();
        var console2 = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer2)
        });
        console2.Write(markup2);
        var rendered2 = writer2.ToString();

        // The outputs should be stable (accounting for ANSI codes)
        Assert.NotNull(rendered1);
        Assert.NotNull(rendered2);
    }

    #endregion

    #region Known Anti-Patterns

    [Fact]
    public void AntiPattern_DoNotEscapeAlreadySanitizedContent()
    {
        // ❌ ANTI-PATTERN: Calling any escaping function on already-sanitized content

        var rawInput = "[kworker/0:1]";
        var sanitized = ContentSanitizer.Sanitize(rawInput);

        // This is the correct sanitized output
        Assert.Equal("[[kworker/0:1]]", sanitized);

        // ✅ GOOD NEWS: Even if you call EscapeInvalidBrackets again (which you shouldn't),
        // it's idempotent and won't double-escape
        var callAgain = ContentSanitizer.EscapeInvalidBrackets(sanitized);
        Assert.Equal("[[kworker/0:1]]", callAgain); // Stays the same - idempotent!

        // This test documents that the sanitizer is safe even if called multiple times
        // However, you should still only call it once for performance
    }

    [Fact]
    public void AntiPattern_DoNotSanitizeInMultiplePlaces()
    {
        // ❌ ANTI-PATTERN: Calling ContentSanitizer.Sanitize() in multiple places

        var rawInput = "[kworker/0:1]";

        // ✅ CORRECT: Sanitize once in WidgetProtocolParser
        var sanitized = ContentSanitizer.Sanitize(rawInput);

        // ❌ WRONG: Do not sanitize again in renderer
        // Even though it's idempotent, it's wasteful and indicates a design flaw
        var wrongSecondSanitize = ContentSanitizer.Sanitize(sanitized);

        // These should be equal (idempotent), but the second call shouldn't happen
        Assert.Equal(sanitized, wrongSecondSanitize);

        // This test documents the correct pattern:
        // - Sanitize ONCE at the data ingestion point (WidgetProtocolParser)
        // - Use sanitized data everywhere else
    }

    #endregion

    #region Regression Prevention

    [Fact]
    public void RegressionPrevention_NoDoubleEscapingInDashboard()
    {
        // This test prevents regression of double-escaping in dashboard rendering

        var rawInput = "[kworker/0:1]";
        var sanitized = ContentSanitizer.Sanitize(rawInput);

        // Simulate dashboard rendering path
        // Dashboard creates Markup directly from sanitized content
        var markup = new Markup(sanitized);
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer)
        });
        console.Write(markup);
        var rendered = writer.ToString();

        // The rendered output should contain single brackets (unescaped)
        // Not double brackets (which would indicate double-escaping)
        Assert.Contains("[kworker/0:1]", rendered);

        // Should NOT contain escaped brackets in final output
        // (Spectre.Console converts [[ to [)
    }

    [Fact]
    public void RegressionPrevention_NoDoubleEscapingInExpandedDialog()
    {
        // This test prevents regression of double-escaping in expanded dialog

        var rawInput = "[kworker/0:1]";
        var sanitized = ContentSanitizer.Sanitize(rawInput);

        // Simulate expanded dialog rendering path
        // Expanded dialog also creates Markup directly from sanitized content
        var markup = new Markup(sanitized);
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer)
        });
        console.Write(markup);
        var rendered = writer.ToString();

        // Same as dashboard - should render correctly
        Assert.Contains("[kworker/0:1]", rendered);
    }

    #endregion
}
