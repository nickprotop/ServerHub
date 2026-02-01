# ServerHub Test Suite

Comprehensive test suite for ServerHub, with a focus on the ContentSanitizer and rendering pipeline.

## Test Structure

### Utils/ContentSanitizerTests.cs
Unit tests for the ContentSanitizer utility class.

**Coverage:**
- ✅ Basic bracket escaping (valid Spectre.Console markup preserved)
- ✅ Invalid bracket escaping (process names, invalid tags)
- ✅ Already-escaped content handling (idempotency)
- ✅ ANSI code stripping
- ✅ Edge cases (empty, null, special characters, long content)
- ✅ Table content sanitization
- ✅ Performance benchmarks
- ✅ Robustness (never throws exceptions)
- ✅ Specific Spectre.Console markup patterns
- ✅ Mixed content scenarios

**Test Count:** 100+ test cases

### Integration/RenderingPipelineTests.cs
Integration tests for the full rendering pipeline from script output to display.

**Coverage:**
- ✅ Protocol parser sanitization
- ✅ Title and row content sanitization
- ✅ Table cell sanitization
- ✅ Dashboard vs expanded dialog consistency
- ✅ Complex mixed content
- ✅ Real-world widget scenarios

**Test Count:** 15+ integration tests

### Regression/DoubleEscapingTests.cs
Regression tests to prevent double-escaping bugs.

**Coverage:**
- ✅ AnsiConsoleHelper integration
- ✅ Documentation of correct usage patterns
- ✅ Idempotency verification
- ✅ Known anti-patterns (what NOT to do)
- ✅ Regression prevention for both rendering paths

**Test Count:** 12+ regression tests

### Performance/SanitizationPerformanceTests.cs
Performance benchmarks for ContentSanitizer operations.

**Coverage:**
- ✅ Large content processing (< 1 second)
- ✅ Many small calls (< 500ms for 1000 calls)
- ✅ Complex mixed content
- ✅ ANSI code stripping performance
- ✅ Worst-case scenarios
- ✅ Baseline metrics

**Test Count:** 15+ performance tests

## Running Tests

### Run All Tests
```bash
cd /home/nick/source/ServerHub
dotnet test
```

### Run Specific Test Suite
```bash
# ContentSanitizer unit tests
dotnet test --filter "FullyQualifiedName~ContentSanitizerTests"

# Integration tests
dotnet test --filter "FullyQualifiedName~RenderingPipelineTests"

# Regression tests
dotnet test --filter "FullyQualifiedName~DoubleEscapingTests"

# Performance tests
dotnet test --filter "FullyQualifiedName~SanitizationPerformanceTests"
```

### Run Specific Test Category
```bash
# Basic bracket escaping tests
dotnet test --filter "FullyQualifiedName~BasicBracketEscaping"

# Robustness tests (no exceptions)
dotnet test --filter "FullyQualifiedName~RobustnessTests"

# Performance tests
dotnet test --filter "FullyQualifiedName~Performance"
```

### Run with Detailed Output
```bash
dotnet test --logger "console;verbosity=detailed"
```

### Run with Coverage
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### Watch Mode (auto-run on file changes)
```bash
dotnet watch test
```

## Test Principles

### 1. Single-Escaping Principle
Content is escaped exactly once, during protocol parsing in `WidgetProtocolParser`.

**Correct Flow:**
```
Widget Script Output → ContentSanitizer.Sanitize() → WidgetData (sanitized) → Rendering
```

**Incorrect Flow (avoided):**
```
Widget Script Output → Sanitize → ... → Sanitize again → Double-escaped! ❌
```

### 2. No Double-Escaping
Already-escaped content is never re-escaped. The sanitizer is idempotent.

**Example:**
```csharp
var input = "[kworker/0:1]";
var pass1 = ContentSanitizer.Sanitize(input);  // "[[kworker/0:1]]"
var pass2 = ContentSanitizer.Sanitize(pass1);  // "[[kworker/0:1]]" (unchanged)
```

### 3. Valid Markup Preserved
Spectre.Console markup tags like `[red]`, `[bold]`, `[/]` remain unchanged.

**Example:**
```csharp
var input = "[red]Error[/]";
var sanitized = ContentSanitizer.Sanitize(input);  // "[red]Error[/]" (unchanged)
```

### 4. Invalid Brackets Escaped
Process names and other invalid brackets are escaped.

**Example:**
```csharp
var input = "[kworker/0:1]";
var sanitized = ContentSanitizer.Sanitize(input);  // "[[kworker/0:1]]"
```

### 5. Rendering Consistency
Dashboard and expanded dialog produce identical output from the same sanitized data.

### 6. Never Throws Exceptions
Sanitized content MUST NEVER throw exceptions when rendered by Spectre.Console.

**Critical Test:**
```csharp
var sanitized = ContentSanitizer.Sanitize(anyUserInput);
var markup = new Markup(sanitized);  // MUST NOT THROW
```

## Expected Coverage

- **ContentSanitizer.cs**: 100% line coverage ✅
- **WidgetProtocolParser.cs** (sanitization paths): 100% coverage ✅
- **Integration paths**: Full pipeline coverage ✅

## Test Categories

### ✅ Functional Requirements
- All 130+ test cases pass
- No double-escaping occurs
- Valid markup is preserved
- Invalid brackets are escaped
- Already-escaped content is not re-escaped
- ANSI codes are stripped

### ✅ Non-Functional Requirements
- Test execution time < 10 seconds
- Individual performance benchmarks met
- Tests are maintainable and documented
- Regression tests prevent known issues

### ✅ Robustness Requirements
- Sanitized content never throws exceptions
- Handles edge cases gracefully
- Handles malformed input safely
- Handles very large content efficiently

## Common Test Patterns

### Testing Valid Markup Preservation
```csharp
[Theory]
[InlineData("[red]text[/]", "[red]text[/]")]
[InlineData("[bold]text[/]", "[bold]text[/]")]
public void Sanitize_PreservesValidSpectreMarkup(string input, string expected)
{
    var result = ContentSanitizer.Sanitize(input);
    Assert.Equal(expected, result);
}
```

### Testing Invalid Bracket Escaping
```csharp
[Theory]
[InlineData("[kworker/0:1]", "[[kworker/0:1]]")]
[InlineData("[systemd]", "[[systemd]]")]
public void Sanitize_EscapesInvalidBrackets(string input, string expected)
{
    var result = ContentSanitizer.Sanitize(input);
    Assert.Equal(expected, result);
}
```

### Testing Robustness (No Exceptions)
```csharp
[Theory]
[InlineData("[kworker/0:1]")]
[InlineData("]]]]]]")]
public void Sanitize_NeverThrows(string rawInput)
{
    var sanitized = ContentSanitizer.Sanitize(rawInput);

    var exception = Record.Exception(() =>
    {
        var markup = new Markup(sanitized);
        console.Write(markup);
    });

    Assert.Null(exception);  // MUST NOT THROW
}
```

### Testing Performance
```csharp
[Fact]
public void Sanitize_Performance_LargeContent()
{
    var input = CreateLargeTestContent();

    var stopwatch = Stopwatch.StartNew();
    var result = ContentSanitizer.Sanitize(input);
    stopwatch.Stop();

    Assert.True(stopwatch.ElapsedMilliseconds < 1000);
}
```

## Troubleshooting

### Test Failures

**"Expected [[...]] but got [[[[...]]]]"**
- Indicates double-escaping issue
- Check that ContentSanitizer.Sanitize() is only called once
- Verify no additional escaping functions are called on sanitized content

**"Markup threw exception"**
- Indicates sanitizer failed to escape invalid brackets
- Check the IsValidMarkupTag() logic
- Verify the EscapeInvalidBrackets() implementation

**Performance test failures**
- May be due to slow CI/test environment
- Rerun locally to verify
- Check for regex compilation issues

### Adding New Tests

1. Identify the test category (unit, integration, regression, performance)
2. Add test to appropriate file
3. Follow existing naming conventions
4. Include clear test description
5. Use `[Theory]` with `[InlineData]` for parameterized tests
6. Use `[Fact]` for single-case tests

## CI Integration

Tests run automatically on:
- Every commit (GitHub Actions)
- Pull requests
- Before merges to main

**CI Configuration:** `.github/workflows/build.yml`

## Success Criteria

✅ All tests pass
✅ 100% coverage of ContentSanitizer
✅ No double-escaping in any scenario
✅ Performance benchmarks met
✅ Regression tests prevent known issues
✅ Documentation is clear and complete

## References

- **ContentSanitizer**: `src/Utils/ContentSanitizer.cs`
- **WidgetProtocolParser**: `src/Services/WidgetProtocolParser.cs`
- **Spectre.Console**: [Documentation](https://spectreconsole.net/)

## Contributing

When adding new features that affect content sanitization:

1. Add unit tests to `ContentSanitizerTests.cs`
2. Add integration tests to `RenderingPipelineTests.cs`
3. Add regression tests if fixing a bug
4. Update this README if adding new test categories
5. Ensure all tests pass before submitting PR

## Test Metrics

- **Total Tests**: 140+
- **Unit Tests**: 100+
- **Integration Tests**: 15+
- **Regression Tests**: 12+
- **Performance Tests**: 15+
- **Expected Runtime**: < 10 seconds
- **Coverage Target**: 100% for ContentSanitizer
