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

    #region Line Graph Parsing

    [Fact]
    public void ProtocolParser_ParsesBasicLineGraph()
    {
        var scriptOutput = "row: [line:1,2,3,4,5]";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var lineGraph = widgetData.Rows[0].LineGraph;
        Assert.NotNull(lineGraph);
        Assert.Equal(5, lineGraph.Values.Count);
        Assert.Equal(new List<double> { 1, 2, 3, 4, 5 }, lineGraph.Values);
        Assert.Equal(60, lineGraph.Width);
        Assert.Equal(8, lineGraph.Height);
        Assert.Equal("braille", lineGraph.Style);
    }

    [Fact]
    public void ProtocolParser_ParsesLineGraphWithColor()
    {
        var scriptOutput = "row: [line:10,20,15,25,30:cyan]";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var lineGraph = widgetData.Rows[0].LineGraph;
        Assert.NotNull(lineGraph);
        Assert.Equal("cyan", lineGraph.Color);
        Assert.Null(lineGraph.Gradient);
    }

    [Fact]
    public void ProtocolParser_ParsesLineGraphWithGradient()
    {
        var scriptOutput = "row: [line:5,10,15,10,5:cool]";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var lineGraph = widgetData.Rows[0].LineGraph;
        Assert.NotNull(lineGraph);
        Assert.Null(lineGraph.Color);
        Assert.Equal("cool", lineGraph.Gradient);
    }

    [Fact]
    public void ProtocolParser_ParsesLineGraphWithLabel()
    {
        var scriptOutput = "row: [line:1,2,3,2,1:red:CPU Usage]";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var lineGraph = widgetData.Rows[0].LineGraph;
        Assert.NotNull(lineGraph);
        Assert.Equal("red", lineGraph.Color);
        Assert.Equal("CPU Usage", lineGraph.Label);
    }

    [Fact]
    public void ProtocolParser_ParsesLineGraphWithMinMax()
    {
        var scriptOutput = "row: [line:50,60,55,65,70:green:Memory:0-100]";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var lineGraph = widgetData.Rows[0].LineGraph;
        Assert.NotNull(lineGraph);
        Assert.Equal(0, lineGraph.MinValue);
        Assert.Equal(100, lineGraph.MaxValue);
    }

    [Fact]
    public void ProtocolParser_ParsesLineGraphWithCustomWidth()
    {
        var scriptOutput = "row: [line:1,2,3,4,5:blue:Network:0-10:80]";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var lineGraph = widgetData.Rows[0].LineGraph;
        Assert.NotNull(lineGraph);
        Assert.Equal(80, lineGraph.Width);
        Assert.Equal(8, lineGraph.Height); // Default height
    }

    [Fact]
    public void ProtocolParser_ParsesLineGraphWithCustomHeight()
    {
        var scriptOutput = "row: [line:1,2,3,4,5:yellow:Disk:0-10:60:12]";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var lineGraph = widgetData.Rows[0].LineGraph;
        Assert.NotNull(lineGraph);
        Assert.Equal(60, lineGraph.Width);
        Assert.Equal(12, lineGraph.Height);
    }

    [Fact]
    public void ProtocolParser_ParsesLineGraphWithAsciiStyle()
    {
        var scriptOutput = "row: [line:1,2,3,4,5:red:Temperature:0-10:60:8:ascii]";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var lineGraph = widgetData.Rows[0].LineGraph;
        Assert.NotNull(lineGraph);
        Assert.Equal("ascii", lineGraph.Style);
    }

    [Fact]
    public void ProtocolParser_ParsesLineGraphWithAllParameters()
    {
        var scriptOutput = "row: [line:10,20,30,40,50:warm:Complete:0-100:100:15:braille]";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var lineGraph = widgetData.Rows[0].LineGraph;
        Assert.NotNull(lineGraph);
        Assert.Equal(new List<double> { 10, 20, 30, 40, 50 }, lineGraph.Values);
        Assert.Equal("warm", lineGraph.Gradient);
        Assert.Null(lineGraph.Color);
        Assert.Equal("Complete", lineGraph.Label);
        Assert.Equal(0, lineGraph.MinValue);
        Assert.Equal(100, lineGraph.MaxValue);
        Assert.Equal(100, lineGraph.Width);
        Assert.Equal(15, lineGraph.Height);
        Assert.Equal("braille", lineGraph.Style);
    }

    [Fact]
    public void ProtocolParser_SkipsLineGraphWithEmptyValues()
    {
        var scriptOutput = "row: [line:]";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var lineGraph = widgetData.Rows[0].LineGraph;
        Assert.Null(lineGraph);
    }

    [Fact]
    public void ProtocolParser_RemovesLineGraphTagFromContent()
    {
        var scriptOutput = "row: Before [line:1,2,3] After";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var row = widgetData.Rows[0];
        Assert.NotNull(row.LineGraph);
        Assert.DoesNotContain("[line:", row.Content);
        Assert.Contains("Before", row.Content);
        Assert.Contains("After", row.Content);
    }

    [Fact]
    public void ProtocolParser_ClampsLineGraphWidthToReasonableRange()
    {
        var scriptOutput1 = "row: [line:1,2,3::::10]"; // Too small
        var scriptOutput2 = "row: [line:1,2,3::::500]"; // Too large

        var parser = new WidgetProtocolParser();

        var data1 = parser.Parse(scriptOutput1);
        Assert.Equal(20, data1.Rows[0].LineGraph?.Width); // Clamped to min

        var data2 = parser.Parse(scriptOutput2);
        Assert.Equal(200, data2.Rows[0].LineGraph?.Width); // Clamped to max
    }

    [Fact]
    public void ProtocolParser_ClampsLineGraphHeightToReasonableRange()
    {
        var scriptOutput1 = "row: [line:1,2,3:::::2]"; // Too small
        var scriptOutput2 = "row: [line:1,2,3:::::100]"; // Too large

        var parser = new WidgetProtocolParser();

        var data1 = parser.Parse(scriptOutput1);
        Assert.Equal(4, data1.Rows[0].LineGraph?.Height); // Clamped to min

        var data2 = parser.Parse(scriptOutput2);
        Assert.Equal(40, data2.Rows[0].LineGraph?.Height); // Clamped to max
    }

    [Fact]
    public void ProtocolParser_HandlesInvalidNumbersInLineGraph()
    {
        var scriptOutput = "row: [line:1,abc,3,def,5]";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var lineGraph = widgetData.Rows[0].LineGraph;
        Assert.NotNull(lineGraph);
        Assert.Equal(5, lineGraph.Values.Count);
        // Invalid numbers are parsed as 0 by ParseDataPoints
        Assert.Equal(new List<double> { 1, 0, 3, 0, 5 }, lineGraph.Values);
    }

    [Fact]
    public void ProtocolParser_RecognizesGradientArrowSyntax()
    {
        var scriptOutput = "row: [line:1,2,3:blue→red]";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var lineGraph = widgetData.Rows[0].LineGraph;
        Assert.NotNull(lineGraph);
        Assert.Equal("blue→red", lineGraph.Gradient);
        Assert.Null(lineGraph.Color);
    }

    #endregion
}
