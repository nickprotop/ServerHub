// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Xunit;
using ServerHub.Services;
using ServerHub.Utils;

namespace ServerHub.Tests.Integration;

/// <summary>
/// Integration tests for the full rendering pipeline:
/// Widget Script Output → WidgetProtocolParser → ContentSanitizer → Rendering
/// </summary>
public class RenderingPipelineTests
{
    #region Protocol Parser Integration

    [Fact]
    public void ProtocolParser_SanitizesTitle()
    {
        var scriptOutput = "title: Process [kworker/0:1] Status";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        Assert.Equal("Process [[kworker/0:1]] Status", widgetData.Title);
    }

    [Fact]
    public void ProtocolParser_SanitizesRowContent()
    {
        var scriptOutput = "row: Process: [kworker/0:1] running";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        Assert.Single(widgetData.Rows);
        Assert.Equal("Process: [[kworker/0:1]] running", widgetData.Rows[0].Content);
    }

    [Fact]
    public void ProtocolParser_SanitizesRowWithValidMarkup()
    {
        var scriptOutput = "row: [red]Error:[/] Process [kworker/0:1] failed";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        Assert.Equal("[red]Error:[/] Process [[kworker/0:1]] failed", widgetData.Rows[0].Content);
    }

    [Fact]
    public void ProtocolParser_SanitizesTableCellContent()
    {
        var scriptOutput = @"
[table:Process|Status]
[tablerow:[kworker/0:1]|Running]
[tablerow:[systemd]|Active]
";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var table = widgetData.Rows[0].Table;
        Assert.NotNull(table);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("[[kworker/0:1]]", table.Rows[0][0]);
        Assert.Equal("[[systemd]]", table.Rows[1][0]);
    }

    [Fact]
    public void ProtocolParser_PreservesValidMarkupInTableCells()
    {
        var scriptOutput = @"
[table:Process|Status]
[tablerow:[red]systemd[/]|[green]Active[/]]
[tablerow:[kworker/0:1]|Running]
";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var table = widgetData.Rows[0].Table;
        Assert.NotNull(table);
        Assert.Equal("[red]systemd[/]", table.Rows[0][0]);
        Assert.Equal("[green]Active[/]", table.Rows[0][1]);
        Assert.Equal("[[kworker/0:1]]", table.Rows[1][0]);
    }

    [Fact]
    public void ProtocolParser_HandlesAnsiCodesInRows()
    {
        var scriptOutput = "row: \x1b[31m[kworker/0:1]\x1b[0m running";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        // ANSI codes should be stripped, invalid brackets should be escaped
        Assert.Equal("[[kworker/0:1]] running", widgetData.Rows[0].Content);
    }

    #endregion

    #region Dashboard vs Expanded Dialog Consistency

    [Theory]
    [InlineData("row: Test [invalid] content")]
    [InlineData("row: [red]Valid[/] and [invalid]")]
    [InlineData("row: Multiple [a] [b] [c] brackets")]
    [InlineData("row: Process [kworker/0:1] at [green]85%[/]")]
    public void ProtocolParser_ProducesConsistentOutput(string scriptOutput)
    {
        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        // Both dashboard and expanded dialog use the same parsed data
        // The sanitized content should be identical
        Assert.Single(widgetData.Rows);
        var sanitizedContent = widgetData.Rows[0].Content;

        // Verify content is properly sanitized
        Assert.NotNull(sanitizedContent);
        // Check for ANSI escape sequences (ESC character = 0x1B)
        Assert.DoesNotContain('\x1b', sanitizedContent); // No ANSI codes (using char, not string)
    }

    #endregion

    #region Complex Mixed Content

    [Fact]
    public void ProtocolParser_ComplexMixedContent()
    {
        var scriptOutput = @"
title: System [server1] Status
row: [red]ERROR:[/] Process [kworker/0:1] failed
row: CPU: [green]85%[/] [systemd] running
row: Memory [progress:75]
[table:Process|CPU|Status]
[tablerow:[kworker/0:1]|45%|[green]Running[/]]
[tablerow:[migration/0]|12%|Active]
";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        // Validate title
        Assert.Equal("System [[server1]] Status", widgetData.Title);

        // Validate rows
        Assert.Equal(4, widgetData.Rows.Count);
        Assert.Equal("[red]ERROR:[/] Process [[kworker/0:1]] failed", widgetData.Rows[0].Content);
        Assert.Equal("CPU: [green]85%[/] [[systemd]] running", widgetData.Rows[1].Content);
        Assert.Contains("Memory", widgetData.Rows[2].Content);

        // Validate table
        var table = widgetData.Rows[3].Table;
        Assert.NotNull(table);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("[[kworker/0:1]]", table.Rows[0][0]);
        Assert.Equal("[green]Running[/]", table.Rows[0][2]);
        Assert.Equal("[[migration/0]]", table.Rows[1][0]);
    }

    #endregion

    #region Multiple Sanitization Passes

    [Fact]
    public void ProtocolParser_DoesNotDoubleSanitize()
    {
        // Simulate content that might look already escaped
        var scriptOutput = "row: Already escaped [[text]]";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        // Should not re-escape already escaped content
        Assert.Equal("Already escaped [[text]]", widgetData.Rows[0].Content);
    }

    #endregion

    #region Status and Progress Elements

    [Fact]
    public void ProtocolParser_HandlesStatusWithInvalidBrackets()
    {
        var scriptOutput = "row: [status:ok] Process [kworker/0:1] running";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        // Status element should be parsed out, invalid brackets should be escaped
        var row = widgetData.Rows[0];
        Assert.NotNull(row.Status);
        Assert.Contains("[[kworker/0:1]]", row.Content);
    }

    [Fact]
    public void ProtocolParser_HandlesProgressWithInvalidBrackets()
    {
        var scriptOutput = "row: [progress:75] for [kworker/0:1]";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var row = widgetData.Rows[0];
        Assert.NotNull(row.Progress);
        Assert.Contains("[[kworker/0:1]]", row.Content);
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public void ProtocolParser_TopProcessesWidget()
    {
        var scriptOutput = @"
title: Top Processes
row: [kworker/0:1] - CPU: 45%
row: [systemd-journald] - CPU: 12%
row: [ksoftirqd/0] - CPU: 8%
";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        Assert.Equal("Top Processes", widgetData.Title);
        Assert.Equal(3, widgetData.Rows.Count);
        Assert.Equal("[[kworker/0:1]] - CPU: 45%", widgetData.Rows[0].Content);
        Assert.Equal("[[systemd-journald]] - CPU: 12%", widgetData.Rows[1].Content);
        Assert.Equal("[[ksoftirqd/0]] - CPU: 8%", widgetData.Rows[2].Content);
    }

    [Fact]
    public void ProtocolParser_NetworkConnectionsWidget()
    {
        var scriptOutput = @"
title: Network Connections
row: [green]Established:[/] [127.0.0.1:8080]
row: [yellow]Listen:[/] [0.0.0.0:443]
row: [red]Failed:[/] [192.168.1.1:22]
";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        Assert.Equal("Network Connections", widgetData.Title);
        Assert.Equal(3, widgetData.Rows.Count);
        Assert.Equal("[green]Established:[/] [[127.0.0.1:8080]]", widgetData.Rows[0].Content);
        Assert.Equal("[yellow]Listen:[/] [[0.0.0.0:443]]", widgetData.Rows[1].Content);
        Assert.Equal("[red]Failed:[/] [[192.168.1.1:22]]", widgetData.Rows[2].Content);
    }

    #endregion
}
