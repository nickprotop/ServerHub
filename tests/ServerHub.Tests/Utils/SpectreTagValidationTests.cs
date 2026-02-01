// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Xunit;
using Spectre.Console;

namespace ServerHub.Tests.Utils;

/// <summary>
/// Tests to verify which tags Spectre.Console actually accepts
/// </summary>
public class SpectreTagValidationTests
{
    [Fact]
    public void SingleLetterAliases_AreValid()
    {
        // [b], [i], [u] were implemented in PR #42 (Aug 2020) as single-letter aliases
        // They are VALID but UNDOCUMENTED in the official Spectre.Console markup reference
        var exception1 = Record.Exception(() => new Markup("[b]bold[/]"));
        var exception2 = Record.Exception(() => new Markup("[i]italic[/]"));
        var exception3 = Record.Exception(() => new Markup("[u]underline[/]"));

        Assert.Null(exception1); // [b] is valid (bold)
        Assert.Null(exception2); // [i] is valid (italic)
        Assert.Null(exception3); // [u] is valid (underline)
    }

    [Fact]
    public void OtherSingleLetterTags_AreInvalid()
    {
        // Only [b], [i], [u] are valid aliases
        // Other single letters should be invalid
        var testData = new[] { "[a]", "[c]", "[d]", "[e]", "[f]" };

        foreach (var tag in testData)
        {
            var exception = Record.Exception(() => new Markup($"{tag}test[/]"));
            Assert.NotNull(exception); // Should throw - not a valid tag
        }
    }
}
