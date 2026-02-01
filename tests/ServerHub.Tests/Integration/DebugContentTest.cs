// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Xunit;
using Xunit.Abstractions;
using ServerHub.Services;
using System.Linq;

namespace ServerHub.Tests.Integration;

public class DebugContentTest
{
    private readonly ITestOutputHelper _output;

    public DebugContentTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Debug_CheckActualContent()
    {
        var scriptOutput = "row: Test [invalid] content";

        var parser = new WidgetProtocolParser();
        var widgetData = parser.Parse(scriptOutput);

        var content = widgetData.Rows[0].Content;

        _output.WriteLine($"Content: '{content}'");
        _output.WriteLine($"Length: {content.Length}");
        _output.WriteLine($"Bytes: {string.Join(" ", content.Select(c => ((int)c).ToString("X2")))}");

        // Check for ESC character
        bool hasEsc = content.Contains('\x1b');
        _output.WriteLine($"Contains ESC (0x1B): {hasEsc}");

        // Check for [ character
        bool hasBracket = content.Contains('[');
        _output.WriteLine($"Contains '[': {hasBracket}");

        Assert.False(hasEsc);
    }
}
