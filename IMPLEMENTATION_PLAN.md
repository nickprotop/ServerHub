# ServerHub Implementation Plan

## Overview
ServerHub is a TUI dashboard application that executes configurable widget scripts and displays their output in a SharpConsoleUI-based interface. Each widget runs on an independent async thread with configurable refresh intervals.

## Architecture

**Key Design Decision: User-Local Installation Only**
- No system-wide install (`/usr/share`) - follows modern CLI tool trends (cargo, go install, pipx)
- Everything in `~/.local/` and `~/.config/` - no sudo required
- Bundled widgets with build-time hardcoded checksums (tamper-proof)
- 2-tier widget search: user custom (priority) → bundled (fallback)
- `--discover` mode for easy custom widget addition with security preview

### Installation Structure (User-Local Only)
```
~/.local/bin/serverhub                    # The executable
~/.local/share/serverhub/widgets/         # Bundled widgets (trusted, hardcoded checksums)
~/.config/serverhub/config.yaml           # User configuration
~/.config/serverhub/widgets/              # User's custom widgets (optional checksums)
```

### Component Flow
```
config.yaml → ConfigLoader → ServerHubConfig
                                    ↓
Widget Scripts → ScriptValidator → ScriptRunner → Output Text
       ↑                ↑
BundledWidgets.g.cs  WidgetPaths (2-tier search)
                                                        ↓
                                    OutputParser → WidgetData
                                                        ↓
                                    WidgetFactory → SharpConsoleUI Controls
                                                        ↓
                                    Application → ConsoleWindowSystem
```

### Security Model
- **Bundled widgets**: SHA256 checksums hardcoded at build time, verified before each execution
- **User custom widgets**: Optional checksums in config (warn if missing)
- **Widget search paths** (in priority order):
  1. Custom path (if `--widgets-path` specified) - for development/testing
  2. `~/.config/serverhub/widgets/` - user custom widgets
  3. `~/.local/share/serverhub/widgets/` - bundled widgets
- **Clean environment** with minimal PATH, no inherited vars
- **10-second timeout** per script execution
- **Direct execution** (no shell wrapping)
- **Path restrictions**: Bundled/user paths enforced, custom path allowed with warnings
- **Symlink detection** to prevent escaping allowed roots

## UI/UX Plan (Finalized Design)

### Visual Style (ConsoleTop-Inspired)
- **Full-screen borderless** immersive mode (like htop/btop)
- **Color scheme**: Dark grays (Grey11 bg, Grey15 panels, Grey19 detail, Grey23 borders), Cyan1 accents, Grey93/Grey70 text
- **Widget chrome**: Borderless with background colors for subtle visual separation
- **Dynamic heights**: Widget height based on content rows (no fixed heights)

### Layout System
- **Responsive columns** based on terminal width:
  - `< 100 cols`: 1 column (vertical stack)
  - `100-160 cols`: 2 columns
  - `160-220 cols`: 3 columns
  - `220+ cols`: 4 columns
- **Pinned widgets**: Optional `pinned: true` flag renders widgets as horizontal tiles at top (like ConsoleTop metrics)
- **Dynamic height allocation**: Distribute vertical space based on content needs (min 5 rows, preferred = content+2, max 30 rows)
- **Priority system**: `priority: 1-3` (1=critical, 2=normal, 3=low) - hide low-priority widgets if terminal too small
- **Resize handling**: Debounced (100ms) recalculation of layout on terminal size changes

### Top Status Bar
```
│ ServerHub • hostname • 4 widgets                              15:42:31         │
```
Shows: app name, hostname (for SSH contexts), widget count, timestamp

### Bottom Status Bar
```
│ Ctrl+C: Exit • F5: Refresh • Enter: Actions • Tab: Next  │  [dynamic stats]  │
```
Left: essential keyboard shortcuts. Right: aggregate stats if not redundant with widgets

### Widget Headers
```
│  System Monitor • ⟳ 2s                                       │
│  system.sh                                                   │
```
- Line 1: `[cyan1 bold]Title[/] • ⟳ countdown`
- Line 2: `[grey70]script.sh[/]` (optional, for debugging)
- During refresh: `Title • ⠋ refreshing...` (brief spinner)

### Status Indicators
- Format: `● text` with color coding
- Colors: green (ok), yellow (warn), red (error), grey (unknown)
- Example: `[green]●[/] Running`

### Progress Bars (Adaptive)
- **Default**: Inline blocks for compactness: `[████████░░] 58%`
- **BarChart mode**: If widget specifies `[progress:75:chart]` or auto-detected (>10 rows, multiple progress values)
- **Spectre BarChart**: Multi-line horizontal bars with labels (like ConsoleTop)

### Interactive Widgets (Action Protocol)
- Widgets can define actions in output: `action: Restart nginx:restart-docker.sh nginx`
- Arrow keys navigate widget rows, Enter/click shows actions modal
- Actions are validated scripts in allowed paths (`~/.local/share/serverhub/actions/` or `~/.config/serverhub/actions/`)
- Security: same validation as widgets (checksums, path restrictions)

### Error Display
- Grey out widget + show small error indicator
- Display: `❌ Script error (exit 1)` + stderr snippet
- Show retry countdown: `retrying in 4s`
- Keep attempting refresh at regular intervals

### Small Terminal Handling
- `< 80x24`: Force 1 column, truncate content, show warning in status bar
- Hide low-priority widgets, show indicator: `⚠ 2 more widgets (resize to view)`
- Graceful degradation - never crash, show what fits

### Local ConsoleEx Integration
- Reference local project `../ConsoleEx/SharpConsoleUI/SharpConsoleUI.csproj` instead of NuGet
- Use modern API: `ConsoleWindowSystem(RenderMode.Buffer)`, `WindowBuilder`, `.WithAsyncWindowThread()`
- Controls: `MarkupControl` (text), `SpectreRenderableControl` (BarChart), `ListControl` (interactive rows)
- Layout: DOM-based (measure/arrange/paint), vertical stacking, clamp sizes

## ConsoleEx Integration Patterns (from ~/source/ConsoleEx)
- Prefer modern API: `ConsoleWindowSystem(RenderMode.Buffer)` + `WindowBuilder` fluent methods; avoid direct `Console.WriteLine` (per README). Use `.WithAsyncWindowThread(UpdateWidgetAsync)` for per-widget loops.
- Controls: use `MarkupControl` (rich text) and `SpectreRenderableControl` for Spectre widgets (e.g., `BarChart`); use `FindControl<T>("name")` only if naming controls; default to rebuild controls each refresh for simplicity.
- Layout system: DOM-based (measure/arrange/paint). Keep controls simple (vertical stacking). Clamp sizes to avoid negative widths; rely on auto layout rather than manual coordinates.
- Logging: use built-in `_windowSystem.LogService` for errors; avoid console output. Respect env-based debug logging (`SHARPCONSOLEUI_DEBUG_LOG`, `SHARPCONSOLEUI_DEBUG_LEVEL`).
- Themes/status bars: keep TopStatus/BottomStatus populated; stick to Classic/ModernGray theme defaults unless overridden. Avoid direct cursor manipulation.
- Project reference: in `ServerHub.csproj`, add `<ProjectReference Include="../ConsoleEx/SharpConsoleUI/SharpConsoleUI.csproj" />` and remove the NuGet reference; ensure TargetFramework net9.0 aligns.
- Control creation pattern (modern):
  ```csharp
  var window = new WindowBuilder(windowSystem)
      .WithTitle(widgetData.Title)
      .WithSize(width, height)
      .AtPosition(x, y)
      .WithAsyncWindowThread(ct => UpdateWidgetAsync(id, window, ct))
      .Build();
  var markup = MarkupControl.Create().AddLine(text).Build();
  var bar = new BarChart().Width(barWidth).AddItem("", value, Color.Green);
  var barCtl = SpectreRenderableControl.Create().WithRenderable(bar).Build();
  window.AddControl(markup);
  window.AddControl(barCtl);
  ```
- Status indicator pattern:
  ```csharp
  var color = state switch { "ok" => "green", "warn" => "yellow", "error" => "red", _ => "white" };
  var status = MarkupControl.Create().AddLine($"[{color}]●[/] {text}").Build();
  window.AddControl(status);
  ```
- Progress bar palette: value <50 -> Green; 50-79 -> Yellow; 80+ -> Red. Use `BarChart.AddItem("", value, color)` with `.Width(min(maxWidth, windowWidth - 4))`; if window too narrow (<20), show text fallback like `"[progress:NN%]"` without chart.
- Layout/resizing defaults: rely on DOM layout (vertical stacking). For small widths (<20), skip progress bars and show text fallback ("NN%"); clamp sizes to max(windowWidth) and ellipsis text. On resize events, recalc layout and allow controls to re-measure; avoid fixed widths where possible except for progress bars (cap 30-40 chars, min 6-8 for textual fallback).

## Implementation Steps (updated with robustness/security notes)

### 0. Cross-Cutting Validation & Defaults
- Define `Result<T>` type used by ConfigLoader and ensure it carries error messages.
- Config validation: non-empty widget IDs/paths, refresh > 0, no duplicate IDs, layout rows not empty, all layout widget IDs exist, and flag unused widget definitions.
- Decide policy on repeated widgets in layout (allow vs. reject vs. share execution) and enforce consistently.
- Path expansion: use `Environment.SpecialFolder.UserProfile` (Windows-friendly) for `~`.
- Normalize path separators on Windows and reject relative/whitespace paths early.
- Widget path syntax:
  - **Shorthand** (no path separators): `"system.sh"` → searches `~/.config/serverhub/widgets/` then `~/.local/share/serverhub/widgets/`
  - **Full path**: `"~/.config/serverhub/widgets/custom.sh"` → must be under allowed roots
- Enforce allowed roots: `~/.local/share/serverhub/` (bundled) and `~/.config/serverhub/` (user custom)
- Default behaviors: missing title → empty; missing refresh → default 5s.

### 1. Project Structure & Dependencies

**File: src/ServerHub/ServerHub.csproj**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <PublishAot>true</PublishAot>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="SharpConsoleUI" Version="2.0.0" />
    <PackageReference Include="YamlDotNet" Version="16.2.0" />
  </ItemGroup>
</Project>
```

**Dependencies:**
- SharpConsoleUI (ConsoleEx) - for TUI
- YamlDotNet - for config parsing
- System.Security.Cryptography - for SHA256
- System.Diagnostics.Process - for script execution

**Build Script: generate-checksums.sh**
```bash
#!/bin/bash
# Generates BundledWidgets.g.cs from widgets/*.sh before build
cd "$(dirname "$0")"

echo "Generating BundledWidgets.g.cs..."
cat > src/ServerHub/Config/BundledWidgets.g.cs << 'HEADER'
// Auto-generated file - do not edit manually
// Generated from widgets/ directory during build

namespace ServerHub.Config;

public static class BundledWidgets
{
    public static readonly Dictionary<string, string> Checksums = new()
    {
HEADER

for file in widgets/*.sh; do
    if [ -f "$file" ]; then
        filename=$(basename "$file")
        checksum=$(sha256sum "$file" | awk '{print $1}')
        echo "        [\"$filename\"] = \"$checksum\"," >> src/ServerHub/Config/BundledWidgets.g.cs
    fi
done

cat >> src/ServerHub/Config/BundledWidgets.g.cs << 'FOOTER'
    };
}
FOOTER

echo "✓ Generated checksums for $(ls widgets/*.sh 2>/dev/null | wc -l) widgets"
```

**Pre-build hook** (add to ServerHub.csproj):
```xml
<Target Name="GenerateChecksums" BeforeTargets="BeforeBuild">
  <Exec Command="bash $(ProjectDir)../../generate-checksums.sh" />
</Target>
```

### 1.5. Bundled Widgets & Search Paths

**File: src/ServerHub/Config/BundledWidgets.g.cs** (auto-generated)
```csharp
// Auto-generated file - do not edit manually
namespace ServerHub.Config;

public static class BundledWidgets
{
    public static readonly Dictionary<string, string> Checksums = new()
    {
        ["system.sh"] = "a1b2c3d4e5f6...",
        ["disks.sh"] = "f6e5d4c3b2a1...",
        ["docker.sh"] = "1234567890ab...",
    };
}
```

**File: src/ServerHub/Config/WidgetPaths.cs**
```csharp
namespace ServerHub.Config;

public static class WidgetPaths
{
    private static string? _customWidgetsPath;

    /// <summary>
    /// Set custom widgets path (from --widgets-path argument).
    /// This path will be searched first, before default paths.
    /// </summary>
    public static void SetCustomWidgetsPath(string? path)
    {
        _customWidgetsPath = path;
    }

    public static IEnumerable<string> GetSearchPaths()
    {
        // 0. Custom path from --widgets-path (highest priority for dev/testing)
        if (!string.IsNullOrEmpty(_customWidgetsPath) && Directory.Exists(_customWidgetsPath))
        {
            yield return _customWidgetsPath;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 1. User custom (can override bundled)
        yield return Path.Combine(home, ".config", "serverhub", "widgets");

        // 2. Bundled widgets
        yield return Path.Combine(home, ".local", "share", "serverhub", "widgets");
    }

    public static string? FindWidget(string name)
    {
        foreach (var searchPath in GetSearchPaths())
        {
            if (!Directory.Exists(searchPath))
                continue;

            var fullPath = Path.Combine(searchPath, name);
            if (File.Exists(fullPath))
                return fullPath;
        }
        return null;
    }

    public static bool IsBundledPath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var bundledPath = Path.Combine(home, ".local", "share", "serverhub", "widgets");
        return path.StartsWith(bundledPath, StringComparison.Ordinal);
    }

    public static bool IsCustomPath(string path)
    {
        return !string.IsNullOrEmpty(_customWidgetsPath) &&
               path.StartsWith(_customWidgetsPath, StringComparison.Ordinal);
    }
}
```

### 2. Configuration Models

**File: src/ServerHub/Config/ServerHubConfig.cs**
```csharp
public class ServerHubConfig
{
    public LayoutConfig Layout { get; set; } = new();
    public Dictionary<string, WidgetConfig> Widgets { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
}

public class LayoutConfig
{
    // Simple list (auto-layout, responsive columns)
    public List<string>? Order { get; set; }

    // Explicit grid (advanced, backward compat)
    public List<LayoutRow>? Rows { get; set; }
}

public class LayoutRow
{
    public List<string> Row { get; set; } = new(); // Widget IDs
}

public class WidgetConfig
{
    public string Path { get; set; } = "";
    public string? Sha256 { get; set; }
    public int Refresh { get; set; } = 5; // seconds
    public bool Pinned { get; set; } = false; // Render as top tile (like ConsoleTop metrics)
    public int Priority { get; set; } = 2; // 1=critical, 2=normal, 3=low (for space constraints)
}

public class SecurityConfig
{
    public bool RequireChecksums { get; set; } = false;
    public bool AllowUserWidgets { get; set; } = true;
    public bool AllowActions { get; set; } = true;
}
```

**File: src/ServerHub/Config/ConfigLoader.cs**
- Use YamlDotNet deserializer
- Validate: all layout widgets exist in widgets section
- Expand `~` to home directory in paths
- Return Result<ServerHubConfig, string> for error handling

### 3. Security Layer

**File: src/ServerHub/Widgets/ScriptValidator.cs**
```csharp
public class ScriptValidator
{
    public record ValidationResult(
        bool IsValid,
        string? ErrorMessage,
        bool HasWarning = false,
        string? WarningMessage = null,
        string? ResolvedPath = null,
        string? ResolvedChecksum = null);

    public ValidationResult Validate(
        string path,
        string? configChecksum,
        SecurityConfig security)
    {
        // 1. Check if shorthand widget name (no path separators)
        if (!path.Contains('/') && !path.Contains('\\'))
        {
            // Search for widget in search paths
            var resolved = WidgetPaths.FindWidget(path);
            if (resolved == null)
                return new(false, $"Widget '{path}' not found in search paths");

            var filename = Path.GetFileName(resolved);

            // Check if it's in bundled directory
            if (WidgetPaths.IsBundledPath(resolved))
            {
                // Bundled widget: validate against hardcoded checksum
                if (!BundledWidgets.Checksums.TryGetValue(filename, out var expectedChecksum))
                    return new(false, $"No checksum registered for bundled widget '{filename}'");

                var actualChecksum = ComputeSha256(resolved);
                if (!actualChecksum.Equals(expectedChecksum, StringComparison.OrdinalIgnoreCase))
                    return new(false, $"Bundled widget '{filename}' has been tampered with!");

                if (!IsExecutable(resolved))
                    return new(false, $"Bundled widget '{filename}' is not executable");

                return new(true, null, false, null, resolved, actualChecksum);
            }
            // Check if it's in custom widgets path (from --widgets-path)
            else if (WidgetPaths.IsCustomPath(resolved))
            {
                // Custom path widget: optional checksum, warn if missing
                if (string.IsNullOrEmpty(configChecksum))
                {
                    var warn = $"Widget '{filename}' from custom path has no checksum (dev mode)";
                    if (security.RequireChecksums)
                        return new(false, warn);
                    return new(true, null, true, warn, resolved, null);
                }

                var actualChecksum = ComputeSha256(resolved);
                if (!actualChecksum.Equals(configChecksum, StringComparison.OrdinalIgnoreCase))
                    return new(false, $"Checksum mismatch for '{filename}'");

                if (!IsExecutable(resolved))
                    return new(false, $"Widget '{filename}' is not executable");

                return new(true, null, false, null, resolved, actualChecksum);
            }
            else
            {
                // User override: requires checksum in config
                if (string.IsNullOrEmpty(configChecksum))
                {
                    var warn = $"User widget '{filename}' overrides bundled widget but has no checksum";
                    if (security.RequireChecksums)
                        return new(false, warn);
                    return new(true, null, true, warn, resolved, null);
                }

                var actualChecksum = ComputeSha256(resolved);
                if (!actualChecksum.Equals(configChecksum, StringComparison.OrdinalIgnoreCase))
                    return new(false, $"Checksum mismatch for '{filename}'");

                if (!IsExecutable(resolved))
                    return new(false, $"Widget '{filename}' is not executable");

                return new(true, null, false, null, resolved, actualChecksum);
            }
        }

        // 2. Explicit path - full validation
        // Expand ~ using Environment.SpecialFolder.UserProfile (cross-platform)
        var expanded = ExpandHome(path);

        if (string.IsNullOrWhiteSpace(expanded))
            return new(false, "Path is empty");

        if (!Path.IsPathRooted(expanded))
            return new(false, "Path must be absolute or a widget name");

        if (expanded.Contains(".."))
            return new(false, "Path traversal (..) is not allowed");

        // Restrict to allowed roots
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var allowedRoots = new[]
        {
            Path.Combine(home, ".config", "serverhub"),
            Path.Combine(home, ".local", "share", "serverhub")
        };

        if (!allowedRoots.Any(root => expanded.StartsWith(root, StringComparison.Ordinal)))
            return new(false, "Path must be under ~/.config/serverhub or ~/.local/share/serverhub");

        var resolved = ResolveRealPath(expanded);
        if (resolved == null)
            return new(false, "Symlink resolution failed");

        if (!File.Exists(resolved))
            return new(false, "File does not exist");

        if (IsSymlinkedOutsideAllowedRoot(expanded, resolved, allowedRoots))
            return new(false, "Symlink escapes allowed root");

        if (!IsExecutable(resolved))
            return new(false, "File is not executable");

        // Validate checksum (required for explicit paths)
        if (string.IsNullOrEmpty(configChecksum))
        {
            var warn = "Custom widget missing checksum";
            if (security.RequireChecksums)
                return new(false, warn);
            return new(true, null, true, warn, resolved, null);
        }

        var checksum = ComputeSha256(resolved);
        if (!checksum.Equals(configChecksum, StringComparison.OrdinalIgnoreCase))
            return new(false, "Checksum mismatch");

        return new(true, null, false, null, resolved, checksum);
    }

    private static string ExpandHome(string path)
    {
        if (path.StartsWith("~"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Join(home, path.TrimStart('~', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        return path;
    }

    public static string ComputeSha256(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? ResolveRealPath(string path)
    {
        try
        {
            return Path.GetFullPath(new FileInfo(path).FullName);
        }
        catch { return null; }
    }

    private static bool IsSymlinkedOutsideAllowedRoot(string original, string resolved, string[] allowedRoots)
    {
        return !allowedRoots.Any(root => resolved.StartsWith(root, StringComparison.Ordinal));
    }

    private static bool IsExecutable(string path)
    {
#if WINDOWS
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".exe" or ".cmd" or ".bat" or ".sh";
#else
        // On Unix, check execute permission via P/Invoke or file.UnixFileMode
        var file = new FileInfo(path);
        return file.Exists && (file.Attributes & FileAttributes.Directory) == 0;
        // For stronger check, use: (file.UnixFileMode & UnixFileMode.UserExecute) != 0
#endif
    }
}
```

**Validation Rules:**
- **Bundled widgets** (`~/.local/share/serverhub/widgets/`): Checksums hardcoded in binary, verified on every execution
- **User custom widgets** (`~/.config/serverhub/widgets/`): Optional checksums in config (warn if missing)
- **User overrides**: If user places same-named widget in `~/.config/serverhub/widgets/`, checksum required in config
- **Explicit paths**: Must be under allowed roots, checksums recommended (required if `security.require_checksums: true`)
- **Verification timing**: Checksums verified on **every execution**, not just startup (TOCTOU protection)

### 4. Script Execution

**File: src/ServerHub/Widgets/ScriptRunner.cs**
```csharp
public class ScriptRunner
{
    private readonly ScriptValidator _validator;

    public enum ExecutionStatus { Success, NonZeroExit, Timeout, ValidationFailed, Crashed }

    public record ExecutionResult(
        ExecutionStatus Status,
        string Output,
        string? Error,
        int ExitCode,
        string? ResolvedPath,
        string? ResolvedChecksum);

    public async Task<ExecutionResult> ExecuteAsync(
        WidgetConfig config,
        SecurityConfig security,
        CancellationToken ct)
    {
        // 1. Validate script (including checksum) and keep resolved path/checksum
        // 2. Create Process with:
        //    - FileName: resolved absolute path
        //    - Arguments: none (self-contained scripts)
        //    - Environment: cleared, then set:
        //      * PATH=/usr/bin:/bin
        //      * HOME=(user home)
        //      * USER=(username)
        //      * LANG=C.UTF-8
        //    - Remove sensitive vars: LD_PRELOAD, LD_LIBRARY_PATH, SSH_*, GIT_*, PYTHON*, TERM
        //    - WorkingDirectory: script directory
        //    - UseShellExecute: false
        //    - RedirectStandardOutput: true
        //    - RedirectStandardError: true
        // 3. Start process, read stdout/stderr asynchronously with caps (e.g., 64KB)
        // 4. Wait with timeout (10 seconds)
        // 5. If timeout, kill process tree (cross-platform) and mark Status=Timeout
        // 6. On non-zero exit, Status=NonZeroExit, include stderr
        // 7. On success, Status=Success, include output
    }
}
```

**Environment Security:**
- Clear all inherited environment variables
- Never pass through: `LD_PRELOAD`, `LD_LIBRARY_PATH`, etc.
- Minimal PATH: `/usr/bin:/bin`
- Timeout enforcement with process tree kill

### 5. Data Models

**File: src/ServerHub/Models/WidgetData.cs**
```csharp
public class WidgetData
{
    public string Title { get; set; } = "";
    public int RefreshInterval { get; set; } = 5;
    public List<WidgetRow> Rows { get; set; } = new();
    public List<WidgetAction> Actions { get; set; } = new();
}
```

**File: src/ServerHub/Models/WidgetRow.cs**
```csharp
public class WidgetRow
{
    public string Text { get; set; } = "";
    public List<RowElement> Elements { get; set; } = new();
    public bool IsSelectable { get; set; } = false; // True if actions available
}
```

**File: src/ServerHub/Models/RowElement.cs**
```csharp
public abstract class RowElement { }

public class ProgressElement : RowElement
{
    public int Value { get; set; } // 0-100
    public ProgressStyle Style { get; set; } = ProgressStyle.Inline;
}

public enum ProgressStyle
{
    Inline,  // [████░░] compact blocks
    Chart    // Spectre BarChart multi-line
}

public class StatusElement : RowElement
{
    public string State { get; set; } = "ok"; // ok, warn, error
}
```

**File: src/ServerHub/Models/WidgetAction.cs**
```csharp
public class WidgetAction
{
    public string Label { get; set; } = "";
    public string ScriptPath { get; set; } = "";
    public string Arguments { get; set; } = "";
}
```

### 6. Output Parser

**File: src/ServerHub/Widgets/OutputParser.cs**
```csharp
public class OutputParser
{
    private static readonly Regex ProgressRegex = new(
        @"\[progress:(\d+)(?::(chart|inline))?\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StatusRegex = new(
        @"\[status:(ok|warn|error)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ActionRegex = new(
        @"^action:\s*(.+?):(.+?)(?:\s+(.*))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public WidgetData Parse(string scriptOutput)
    {
        var data = new WidgetData { Title = string.Empty, RefreshInterval = 5 };
        var lines = scriptOutput.Split('\n');

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Parse title
            if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
            {
                data.Title = line[6..].Trim();
                continue;
            }

            // Parse refresh interval
            if (line.StartsWith("refresh:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(line[8..].Trim(), out var r) && r > 0)
                    data.RefreshInterval = r;
                continue;
            }

            // Parse actions (NEW)
            if (line.StartsWith("action:", StringComparison.OrdinalIgnoreCase))
            {
                var match = ActionRegex.Match(line);
                if (match.Success)
                {
                    data.Actions.Add(new WidgetAction
                    {
                        Label = match.Groups[1].Value.Trim(),
                        ScriptPath = match.Groups[2].Value.Trim(),
                        Arguments = match.Groups.Count > 3 ? match.Groups[3].Value.Trim() : ""
                    });
                }
                continue;
            }

            // Parse rows
            if (line.StartsWith("row:", StringComparison.OrdinalIgnoreCase))
            {
                var body = line[4..].Trim();
                var row = new WidgetRow
                {
                    Text = body,
                    IsSelectable = data.Actions.Count > 0 // Rows selectable if actions defined
                };

                // Parse progress elements with style hint
                foreach (Match m in ProgressRegex.Matches(body))
                {
                    if (int.TryParse(m.Groups[1].Value, out var val))
                    {
                        var style = m.Groups[2].Success && m.Groups[2].Value.Equals("chart", StringComparison.OrdinalIgnoreCase)
                            ? ProgressStyle.Chart
                            : ProgressStyle.Inline;

                        row.Elements.Add(new ProgressElement
                        {
                            Value = Math.Clamp(val, 0, 100),
                            Style = style
                        });
                    }
                }

                // Parse status elements
                foreach (Match m in StatusRegex.Matches(body))
                {
                    row.Elements.Add(new StatusElement
                    {
                        State = m.Groups[1].Value.ToLowerInvariant()
                    });
                }

                data.Rows.Add(row);
            }
        }

        // Cap rows to avoid UI overload (max 50 rows per widget)
        if (data.Rows.Count > 50)
            data.Rows = data.Rows.Take(50).ToList();

        return data;
    }
}
```

**Protocol (Extended):**
```
title: System Monitor
refresh: 2
row: CPU   [progress:75] 75%
row: Memory [progress:45:chart] 4.5GB / 10GB
row: Status [status:ok] Running
row: Alert  [status:error] Disk full
action: Restart service:restart-system.sh
action: View logs:view-logs.sh
```

**Protocol Elements:**

1. **title**: Widget title (displayed in header)
   - `title: System Monitor`

2. **refresh**: Refresh interval in seconds (overrides config)
   - `refresh: 5`

3. **row**: Content row with optional embedded elements
   - Plain text: `row: Simple text`
   - Progress bar (inline): `row: CPU [progress:75] 75%`
   - Progress bar (chart): `row: Memory [progress:45:chart] Used`
   - Status indicator: `row: Status [status:ok] Running`
   - Multiple elements: `row: Disk [progress:58] [status:warn] 45GB/100GB`

4. **action**: Define interactive action (NEW)
   - Format: `action: Label:script-path arguments`
   - Example: `action: Restart nginx:restart-docker.sh nginx`
   - Actions shown when row selected and Enter pressed

**Element Patterns:**
- `[progress:NN]` - Progress bar (0-100), inline blocks by default
- `[progress:NN:chart]` - Progress bar (0-100), use Spectre BarChart
- `[progress:NN:inline]` - Force inline blocks even if chart mode would be auto-detected
- `[status:STATE]` - Status indicator with color (ok=green, warn=yellow, error=red)
- Action rows are not visible in widget, only in action modal when row selected

### 7. Widget Factory

**File: src/ServerHub/Widgets/WidgetFactory.cs**
```csharp
public class WidgetFactory
{
    public List<IWindowControl> CreateControls(WidgetData data)
    {
        // Convert parsed data to SharpConsoleUI controls
        // Use MarkupControl for text
        // Use SpectreRenderableControl with BarChart for progress
        // Use colored markup for status indicators
        // Clamp progress values and guard against negative sizes
        // Truncate overly long text to fit window width
        // Handle multiple row elements in a single row
    }
}
```

**Control Creation Pattern:**
```csharp
// Text row
var control = MarkupControl.Create()
    .AddLine($"[white]{row.Text}[/]")
    .Build();

// Progress row
var progressBar = new Spectre.Console.BarChart()
    .Width(40)
    .AddItem("", progressValue, Color.Green);
var progressControl = SpectreRenderableControl.Create()
    .WithRenderable(progressBar)
    .Build();

// Status indicator
var statusColor = state switch {
    "ok" => "green",
    "warn" => "yellow",
    "error" => "red",
    _ => "white"
};
var statusControl = MarkupControl.Create()
    .AddLine($"[{statusColor}]●[/] {text}")
    .Build();
```

### 8. Layout Engine

**File: src/ServerHub/Layout/LayoutEngine.cs**
```csharp
public class LayoutEngine
{
    public record WidgetPlacement(
        string WidgetId,
        int X, int Y,
        int Width, int Height,
        bool IsPinned);

    public List<WidgetPlacement> CalculateLayout(
        ServerHubConfig config,
        int terminalWidth,
        int terminalHeight)
    {
        var placements = new List<WidgetPlacement>();

        // Reserve space for status bars
        const int topBarHeight = 2;
        const int bottomBarHeight = 2;
        int availableHeight = terminalHeight - topBarHeight - bottomBarHeight;
        int currentY = topBarHeight;

        // 1. Place pinned widgets (horizontal tiles at top)
        var pinnedWidgets = config.Widgets
            .Where(kv => kv.Value.Pinned)
            .OrderBy(kv => kv.Key)
            .ToList();

        if (pinnedWidgets.Any())
        {
            const int pinnedHeight = 8;
            int pinnedWidth = terminalWidth / pinnedWidgets.Count;

            for (int i = 0; i < pinnedWidgets.Count; i++)
            {
                placements.Add(new WidgetPlacement(
                    pinnedWidgets[i].Key,
                    i * pinnedWidth, currentY,
                    pinnedWidth, pinnedHeight,
                    IsPinned: true));
            }

            currentY += pinnedHeight + 1; // +1 for separator
            availableHeight -= pinnedHeight + 1;
        }

        // 2. Place regular widgets (responsive grid)
        var regularWidgets = config.Widgets
            .Where(kv => !kv.Value.Pinned)
            .OrderBy(kv => kv.Value.Priority) // Sort by priority
            .ToList();

        // Determine column count based on terminal width
        int columnCount = terminalWidth switch
        {
            < 100 => 1,
            < 160 => 2,
            < 220 => 3,
            _ => 4
        };

        // Use layout configuration (Order or Rows)
        List<string> widgetOrder;
        if (config.Layout.Order != null)
        {
            // Simple list layout
            widgetOrder = config.Layout.Order;
        }
        else if (config.Layout.Rows != null)
        {
            // Explicit grid layout - flatten to list
            widgetOrder = config.Layout.Rows
                .SelectMany(r => r.Row)
                .ToList();
        }
        else
        {
            // Fallback: use all regular widgets
            widgetOrder = regularWidgets.Select(kv => kv.Key).ToList();
        }

        // Calculate grid positions
        int rowCount = (int)Math.Ceiling((double)widgetOrder.Count / columnCount);
        int rowHeight = availableHeight / Math.Max(rowCount, 1);
        int columnWidth = terminalWidth / columnCount;

        for (int i = 0; i < widgetOrder.Count; i++)
        {
            int row = i / columnCount;
            int col = i % columnCount;

            placements.Add(new WidgetPlacement(
                widgetOrder[i],
                col * columnWidth, currentY + (row * rowHeight),
                columnWidth, rowHeight,
                IsPinned: false));
        }

        return placements;
    }
}
```

### 9. Application Core

**File: src/ServerHub/Application.cs**
```csharp
public class Application
{
    private readonly ConsoleWindowSystem _windowSystem;
    private readonly ServerHubConfig _config;
    private readonly ScriptRunner _scriptRunner;
    private readonly OutputParser _parser;
    private readonly WidgetFactory _factory;

    public Application(ServerHubConfig config)
    {
        _config = config;
        _windowSystem = new ConsoleWindowSystem(RenderMode.Buffer)
        {
            TopStatus = "ServerHub - Press Ctrl+C to quit",
            BottomStatus = "Alt+1-9: Switch windows | F1: Help"
        };
        _scriptRunner = new ScriptRunner(new ScriptValidator());
        _parser = new OutputParser();
        _factory = new WidgetFactory();
    }

    public async Task<int> RunAsync()
    {
        // 1. Show startup warnings for unchecksummed scripts
        // 2. Calculate window layout from config; handle small terminals and resize
        // 3. For each widget:
        //    - Create Window with WindowBuilder
        //    - Set .WithAsyncWindowThread(UpdateWidgetAsync)
        //    - Position window based on layout
        // 4. Add all windows to system
        // 5. Run: _windowSystem.Run()
    }

    private async Task UpdateWidgetAsync(
        string widgetId,
        Window window,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 1. Execute script via ScriptRunner
                var result = await _scriptRunner.ExecuteAsync(...);

                if (result.Status != ExecutionStatus.Success)
                {
                    // Show error/stderr/exit info in window, delay retry
                    window.ClearControls();
                    window.AddControl(Controls.Error(
                        $"{result.Status}: {result.Error ?? "no error"}"));
                    await Task.Delay(5000, ct);
                    continue;
                }

                // 2. Parse output
                var widgetData = _parser.Parse(result.Output);

                // 3. Create controls
                var controls = _factory.CreateControls(widgetData);

                // 4. Update window
                window.ClearControls();
                foreach (var control in controls)
                    window.AddControl(control);

                // 5. Wait for refresh interval
                await Task.Delay(
                    widgetData.RefreshInterval * 1000,
                    ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // Log error and show in window
                _windowSystem.LogService.LogError(
                    $"Widget {widgetId} error", ex, "Application");

                // Show error in window
                window.ClearControls();
                window.AddControl(Controls.Error(
                    $"Error: {ex.Message}"));

                await Task.Delay(5000, ct); // Retry after 5s
            }
        }
    }

    private void CalculateLayout(
        out Dictionary<string, WindowBounds> positions)
    {
        // Calculate window positions based on layout config
        // Terminal size: Console.WindowWidth, Console.WindowHeight
        // Grid layout: divide screen into rows and columns
        // Each row can have multiple widgets (columns)
        // Handle repeated widgets if allowed (shared data vs separate execution?)
        // Example: 2 rows, first row has 2 widgets, second has 1
        //   Row 1: [0,0,W/2,H/2] [W/2,0,W/2,H/2]
        //   Row 2: [0,H/2,W,H/2]
    }
}

public record WindowBounds(int X, int Y, int Width, int Height);
```

**Layout Algorithm:**
```
1. Get terminal dimensions
2. Calculate row height = terminal_height / num_rows (guard >0)
3. For each row:
   - Calculate column width = terminal_width / num_widgets_in_row (guard >0)
   - For each widget in row:
     - Position: (col_index * col_width, row_index * row_height)
     - Size: (col_width, row_height)
4. On resize, recompute and apply positions
```


**Layout Algorithm:**
```
1. Get terminal dimensions
2. Calculate row height = terminal_height / num_rows
3. For each row:
   - Calculate column width = terminal_width / num_widgets_in_row
   - For each widget in row:
     - Position: (col_index * col_width, row_index * row_height)
     - Size: (col_width, row_height)
```

### 9. Program Entry Point

**File: src/ServerHub/Program.cs**
```csharp
class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            // 1. Parse command-line arguments
            string? widgetsPath = null;
            string? configPath = null;
            var remainingArgs = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--widgets-path" && i + 1 < args.Length)
                {
                    widgetsPath = args[++i];
                }
                else if (args[i] == "--compute-checksums")
                {
                    return await ComputeChecksumsAsync(args.Skip(i + 1).ToArray());
                }
                else if (args[i] == "--discover")
                {
                    var autoAdd = args.Length > i + 1 && args[i + 1] == "--auto-add";
                    return await DiscoverWidgetsAsync(autoAdd);
                }
                else if (args[i] == "--help" || args[i] == "-h")
                {
                    ShowHelp();
                    return 0;
                }
                else if (!args[i].StartsWith("--"))
                {
                    configPath = args[i];
                }
            }

            // 2. Set custom widgets path if provided
            if (!string.IsNullOrEmpty(widgetsPath))
            {
                if (!Directory.Exists(widgetsPath))
                {
                    Console.Error.WriteLine($"Error: Widgets path does not exist: {widgetsPath}");
                    return 1;
                }
                WidgetPaths.SetCustomWidgetsPath(widgetsPath);
                Console.WriteLine($"Using custom widgets path: {widgetsPath}");
            }

            // 3. Load config from ./config.yaml or ~/.config/serverhub/config.yaml
            configPath ??= GetDefaultConfigPath();

            var configLoader = new ConfigLoader();
            var configResult = configLoader.Load(configPath);

            if (!configResult.IsSuccess)
            {
                Console.Error.WriteLine($"Config error: {configResult.Error}");
                return 1;
            }

            // 4. Validate all widget scripts
            var validator = new ScriptValidator();
            var warnings = new List<string>();

            foreach (var (id, widget) in configResult.Value.Widgets)
            {
                var validation = validator.Validate(
                    widget.Path,
                    widget.Sha256,
                    configResult.Value.Security);

                if (!validation.IsValid)
                {
                    Console.Error.WriteLine(
                        $"Widget '{id}' validation failed: {validation.ErrorMessage}");
                    return 1;
                }

                if (validation.HasWarning)
                {
                    warnings.Add($"Widget '{id}': {validation.WarningMessage}");
                }
            }

            // 4. Show warnings and prompt if any
            if (warnings.Count > 0)
            {
                Console.WriteLine("⚠ WARNINGS:");
                foreach (var warning in warnings)
                    Console.WriteLine($"  {warning}");

                Console.Write("\nContinue? [y/N] ");
                var response = Console.ReadLine();
                if (response?.ToLower() != "y")
                    return 1;
            }

            // 5. Run application
            var app = new Application(configResult.Value);
            return await app.RunAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static string GetDefaultConfigPath()
    {
        // Try ./config.yaml, then ~/.config/serverhub/config.yaml
        if (File.Exists("config.yaml"))
            return "config.yaml";

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "serverhub", "config.yaml");
    }

    static async Task<int> ComputeChecksumsAsync(string[] extraArgs)
    {
        // Load config and print SHA256 per widget
        var configPath = extraArgs.Length > 0 ? extraArgs[0] : GetDefaultConfigPath();
        var configLoader = new ConfigLoader();
        var configResult = configLoader.Load(configPath);

        if (!configResult.IsSuccess)
        {
            Console.Error.WriteLine($"Config error: {configResult.Error}");
            return 1;
        }

        Console.WriteLine("Widget Checksums:");
        Console.WriteLine("─────────────────────────────────────");

        foreach (var (id, widget) in configResult.Value.Widgets)
        {
            var resolved = WidgetPaths.FindWidget(widget.Path);
            if (resolved != null)
            {
                var checksum = ScriptValidator.ComputeSha256(resolved);
                Console.WriteLine($"{id,-15} {checksum}");
                Console.WriteLine($"                {resolved}");
            }
            else
            {
                Console.WriteLine($"{id,-15} [NOT FOUND]");
            }
        }

        return 0;
    }

    static async Task<int> DiscoverWidgetsAsync(bool autoAdd)
    {
        // Scan ~/.config/serverhub/widgets/ for unconfigured executables
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var customWidgetsPath = Path.Combine(home, ".config", "serverhub", "widgets");
        var configPath = GetDefaultConfigPath();

        if (!Directory.Exists(customWidgetsPath))
        {
            Console.WriteLine($"No custom widgets directory found: {customWidgetsPath}");
            Console.WriteLine("Create it and add your widget scripts there.");
            return 0;
        }

        // Load existing config to know which widgets are already configured
        var configLoader = new ConfigLoader();
        var configResult = configLoader.Load(configPath);
        var configuredPaths = configResult.IsSuccess
            ? configResult.Value.Widgets.Values.Select(w => Path.GetFullPath(WidgetPaths.FindWidget(w.Path) ?? w.Path)).ToHashSet()
            : new HashSet<string>();

        // Find all executables in custom widgets directory
        var files = Directory.GetFiles(customWidgetsPath)
            .Where(f => IsExecutable(f) && !configuredPaths.Contains(Path.GetFullPath(f)))
            .ToList();

        if (files.Count == 0)
        {
            Console.WriteLine("No unconfigured widgets found.");
            return 0;
        }

        Console.WriteLine($"Found {files.Count} unconfigured widget(s):\n");

        var approved = new List<string>();

        foreach (var file in files)
        {
            var filename = Path.GetFileName(file);
            var fileInfo = new FileInfo(file);
            var checksum = ScriptValidator.ComputeSha256(file);

            Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.WriteLine($"Widget: {filename}");
            Console.WriteLine($"Path:   {file}");
            Console.WriteLine($"Size:   {fileInfo.Length} bytes");
            Console.WriteLine($"SHA256: {checksum}");
            Console.WriteLine();

            // Show preview (first 50 lines for text files)
            if (IsTextFile(file))
            {
                Console.WriteLine("Preview:");
                var lines = File.ReadLines(file).Take(50).ToArray();
                for (int i = 0; i < lines.Length; i++)
                {
                    Console.WriteLine($"    {i + 1,3}  {lines[i]}");
                }
                if (lines.Length == 50)
                    Console.WriteLine("    ... (truncated)");
            }
            else
            {
                Console.WriteLine("[Binary file - no preview]");
            }

            Console.WriteLine();
            Console.Write($"Add '{filename}' to config? [y/N] ");
            var response = Console.ReadLine();

            if (response?.ToLower() == "y")
            {
                approved.Add(file);
            }
        }

        if (approved.Count == 0)
        {
            Console.WriteLine("\nNo widgets added.");
            return 0;
        }

        // Generate YAML snippets
        Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("Add to config.yaml:");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

        foreach (var file in approved)
        {
            var id = Path.GetFileNameWithoutExtension(file);
            var checksum = ScriptValidator.ComputeSha256(file);

            Console.WriteLine($"  {id}:");
            Console.WriteLine($"    path: {file}");
            Console.WriteLine($"    sha256: {checksum}");
            Console.WriteLine($"    refresh: 5");
            Console.WriteLine();
        }

        Console.WriteLine("Don't forget to add widget IDs to your layout!");

        if (autoAdd)
        {
            // TODO: Append to config.yaml (preserve comments, formatting)
            Console.WriteLine("\n(--auto-add not yet implemented)");
        }

        return 0;
    }

    static bool IsExecutable(string path)
    {
#if WINDOWS
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".exe" or ".cmd" or ".bat" or ".sh";
#else
        try
        {
            var file = new FileInfo(path);
            return file.Exists && (file.Attributes & FileAttributes.Directory) == 0;
        }
        catch { return false; }
#endif
    }

    static bool IsTextFile(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".sh" or ".bash" or ".py" or ".rb" or ".pl" or ".js" or ".txt")
                return true;

            // Read first 1KB and check for null bytes
            var buffer = new byte[1024];
            using var fs = File.OpenRead(path);
            var read = fs.Read(buffer, 0, buffer.Length);
            return !buffer.Take(read).Contains((byte)0);
        }
        catch { return false; }
    }

    static void ShowHelp()
    {
        Console.WriteLine("ServerHub - TUI dashboard for server monitoring");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  serverhub [options] [config.yaml]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --widgets-path <path>            Load widgets from custom directory (dev mode)");
        Console.WriteLine("                                   Searches this path first, before default paths");
        Console.WriteLine("  --compute-checksums              Print checksums for configured widgets");
        Console.WriteLine("  --discover                       Find and add new custom widgets");
        Console.WriteLine("  --discover --auto-add            Auto-add discovered widgets to config");
        Console.WriteLine("  --help, -h                       Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  serverhub                                    Run with default config");
        Console.WriteLine("  serverhub myconfig.yaml                      Run with custom config");
        Console.WriteLine("  serverhub --widgets-path ./dev-widgets       Load widgets from ./dev-widgets");
        Console.WriteLine("  serverhub --widgets-path ~/widgets myconf.yaml");
        Console.WriteLine();
        Console.WriteLine("Config locations (searched in order):");
        Console.WriteLine("  1. ./config.yaml");
        Console.WriteLine("  2. ~/.config/serverhub/config.yaml");
        Console.WriteLine();
        Console.WriteLine("Widget search paths (searched in order):");
        Console.WriteLine("  0. Custom path (if --widgets-path specified)");
        Console.WriteLine("  1. ~/.config/serverhub/widgets/");
        Console.WriteLine("  2. ~/.local/share/serverhub/widgets/");
    }
}
```

### 10. Example Widget Scripts

**File: widgets/system.sh**
```bash
#!/bin/bash
echo "title: System"
echo "refresh: 2"
echo "row: CPU   [progress:$(top -bn1 | grep "Cpu(s)" | awk '{print int($2)}')]"
echo "row: Memory [progress:$(free | awk '/Mem:/ {printf("%.0f", $3/$2*100)}')]"
echo "row: Status [status:ok] Running"
```

**File: widgets/disks.sh**
```bash
#!/bin/bash
echo "title: Disk Usage"
echo "refresh: 30"

df -h / /home | tail -n +2 | while read -r line; do
    mount=$(echo "$line" | awk '{print $6}')
    usage=$(echo "$line" | awk '{print int($5)}')
    used=$(echo "$line" | awk '{print $3}')
    total=$(echo "$line" | awk '{print $2}')

    if [ "$usage" -gt 90 ]; then
        status="error"
    elif [ "$usage" -gt 75 ]; then
        status="warn"
    else
        status="ok"
    fi

    echo "row: $mount [progress:$usage] [status:$status] $used / $total"
done
```

**File: widgets/docker.sh**
```bash
#!/bin/bash
echo "title: Docker Containers"
echo "refresh: 5"

if ! command -v docker &> /dev/null; then
    echo "row: [status:error] Docker not installed"
    exit 0
fi

# Define actions (available when container row is selected)
echo "action: Restart:restart-container.sh"
echo "action: Logs:view-container-logs.sh"
echo "action: Stop:stop-container.sh"

docker ps --format "{{.Names}}\t{{.Status}}" 2>/dev/null | while IFS=$'\t' read -r name status; do
    if [[ "$status" == *"Up"* ]]; then
        echo "row: $name [status:ok] Running"
    else
        echo "row: $name [status:error] Stopped"
    fi
done
```

**Note**: Action scripts (restart-container.sh, view-container-logs.sh, etc.) would be placed in:
- `~/.local/share/serverhub/actions/` (bundled actions)
- `~/.config/serverhub/actions/` (user custom actions)

Example action script:
```bash
#!/bin/bash
# File: ~/.local/share/serverhub/actions/restart-container.sh
# Receives selected row text as $1, can extract container name
container_name="$1"
docker restart "$container_name"
```

Make scripts executable:
```bash
chmod +x widgets/*.sh
```

### 11. Configuration File

**File: config.yaml** (generated by install.sh)
```yaml
# ServerHub Configuration
#
# Bundled widgets (system, disks, docker) are automatically validated
# against checksums built into the binary. No sha256 needed in config!
#
# Widget paths can be:
#   - Bundled widget name: "system.sh" (searches ~/.local/share/serverhub/widgets/)
#   - Custom widget name: "mywidget.sh" (searches ~/.config/serverhub/widgets/ first, then bundled)
#   - Full path: "~/.config/serverhub/widgets/custom.sh" (requires sha256)
#
# Layout options:
#   - Simple list (auto-layout, responsive columns based on terminal width):
#       layout:
#         order: [system, disks, docker]
#
#   - Explicit grid (advanced, fixed column layout):
#       layout:
#         rows:
#           - row: [system, disks]
#           - row: [docker]
#
# To add custom widgets:
#   1. Place scripts in ~/.config/serverhub/widgets/
#   2. Run 'serverhub --discover' to find and add them
#   3. Add checksums for security (recommended)

# Simple list layout (responsive, recommended)
layout:
  order: [system, disks, docker]

# Alternative: Explicit grid layout (uncomment to use)
# layout:
#   rows:
#     - row: [system, disks]
#     - row: [docker]

widgets:
  # Bundled widgets (no checksum needed - validated automatically)
  system:
    path: system.sh       # Shorthand: searches bundled paths
    refresh: 2
    priority: 1           # 1=critical, 2=normal, 3=low (for small terminals)
    pinned: false         # true = render as top tile (like ConsoleTop metrics)

  disks:
    path: disks.sh        # Shorthand: searches bundled paths
    refresh: 30
    priority: 2

  docker:
    path: docker.sh       # Shorthand: searches bundled paths
    refresh: 5
    priority: 2

  # Example: Pinned widget (renders as tile at top)
  # cpu_tile:
  #   path: cpu-tile.sh
  #   pinned: true
  #   refresh: 1
  #   priority: 1

  # Example: Custom widget with actions
  # myserver:
  #   path: ~/.config/serverhub/widgets/myserver.sh
  #   sha256: abc123def456...
  #   refresh: 10
  #   priority: 2
  #   # Widget script can define actions:
  #   #   action: Restart:restart-service.sh myserver
  #   #   action: Logs:view-logs.sh myserver

security:
  require_checksums: false    # true = require checksums for all custom widgets
  allow_user_widgets: true    # false = only allow bundled widgets
  allow_actions: true         # false = disable interactive actions
```

**Checksum helpers:**
```bash
# Print checksums for all configured widgets
serverhub --compute-checksums

# Discover and add new custom widgets
serverhub --discover

# Auto-add discovered widgets to config (coming soon)
serverhub --discover --auto-add
```

### 12. Installation Script

**File: install.sh**
```bash
#!/bin/bash
set -e

INSTALL_DIR="$HOME/.local"
CONFIG_DIR="$HOME/.config/serverhub"

echo "🚀 Installing ServerHub (user-local)..."

# 1. Create directories
mkdir -p "$INSTALL_DIR/bin"
mkdir -p "$INSTALL_DIR/share/serverhub/widgets"
mkdir -p "$CONFIG_DIR/widgets"

# 2. Copy binary
if [ ! -f "bin/serverhub" ]; then
    echo "❌ Error: bin/serverhub not found. Run 'dotnet publish' first."
    exit 1
fi

cp bin/serverhub "$INSTALL_DIR/bin/"
chmod +x "$INSTALL_DIR/bin/serverhub"
echo "✓ Installed binary to $INSTALL_DIR/bin/serverhub"

# 3. Copy bundled widgets
if [ -d "widgets" ] && [ -n "$(ls -A widgets/*.sh 2>/dev/null)" ]; then
    cp widgets/*.sh "$INSTALL_DIR/share/serverhub/widgets/" 2>/dev/null || true
    chmod +x "$INSTALL_DIR/share/serverhub/widgets/"*.sh 2>/dev/null || true
    widget_count=$(ls -1 "$INSTALL_DIR/share/serverhub/widgets/"*.sh 2>/dev/null | wc -l)
    echo "✓ Installed $widget_count bundled widget(s) to $INSTALL_DIR/share/serverhub/widgets/"
fi

# 4. Generate example config if not exists
if [ ! -f "$CONFIG_DIR/config.yaml" ]; then
    cat > "$CONFIG_DIR/config.yaml" << 'EOF'
# ServerHub Configuration
#
# Bundled widgets are automatically validated - no checksum needed!
# Custom widgets should include sha256 for security.

layout:
  - row: [system, disks]
  - row: [docker]

widgets:
  system:
    path: system.sh
    refresh: 2

  disks:
    path: disks.sh
    refresh: 30

  docker:
    path: docker.sh
    refresh: 5

security:
  require_checksums: false
  allow_user_widgets: true

EOF
    echo "✓ Created example config at $CONFIG_DIR/config.yaml"
else
    echo "ℹ Config already exists at $CONFIG_DIR/config.yaml"
fi

# 5. Check PATH
if ! echo "$PATH" | grep -q "$INSTALL_DIR/bin"; then
    echo ""
    echo "⚠️  Add ~/.local/bin to your PATH:"
    echo ""
    if [ -f "$HOME/.bashrc" ]; then
        echo "  echo 'export PATH=\"\$HOME/.local/bin:\$PATH\"' >> ~/.bashrc"
        echo "  source ~/.bashrc"
    elif [ -f "$HOME/.zshrc" ]; then
        echo "  echo 'export PATH=\"\$HOME/.local/bin:\$PATH\"' >> ~/.zshrc"
        echo "  source ~/.zshrc"
    else
        echo "  export PATH=\"\$HOME/.local/bin:\$PATH\""
    fi
else
    echo "✓ ~/.local/bin is already in PATH"
fi

echo ""
echo "✅ Installation complete!"
echo ""
echo "Quick start:"
echo "  serverhub              - Run with default config"
echo "  serverhub --discover   - Find and add custom widgets"
echo "  serverhub --help       - Show all options"
```

**File: uninstall.sh**
```bash
#!/bin/bash
set -e

echo "🗑️  Uninstalling ServerHub..."

rm -f "$HOME/.local/bin/serverhub"
rm -rf "$HOME/.local/share/serverhub"

echo "✓ Removed binary and bundled widgets"
echo ""
echo "ℹ️  Configuration and custom widgets preserved at:"
echo "   ~/.config/serverhub/"
echo ""
echo "   To completely remove (including your config and custom widgets):"
echo "   rm -rf ~/.config/serverhub/"
```

## Critical Files Summary

### New Files to Create
1. `src/ServerHub/ServerHub.csproj` - Project file with dependencies
2. `src/ServerHub/Config/ServerHubConfig.cs` - Configuration models (with Pinned, Priority)
3. `src/ServerHub/Config/ConfigLoader.cs` - YAML config loader (supports Order and Rows layouts)
4. `src/ServerHub/Config/BundledWidgets.g.cs` - Auto-generated checksums (by build script)
5. `src/ServerHub/Config/WidgetPaths.cs` - Widget search path utilities
6. `src/ServerHub/Widgets/ScriptValidator.cs` - Security validation
7. `src/ServerHub/Widgets/ScriptRunner.cs` - Script execution
8. `src/ServerHub/Models/WidgetData.cs` - Data model (with Actions)
9. `src/ServerHub/Models/WidgetRow.cs` - Row model (with IsSelectable)
10. `src/ServerHub/Models/RowElement.cs` - Element models (ProgressElement with Style, StatusElement)
11. `src/ServerHub/Models/WidgetAction.cs` - Action model (Label, ScriptPath, Arguments)
12. `src/ServerHub/Widgets/OutputParser.cs` - Protocol parser (with action parsing)
13. `src/ServerHub/Widgets/WidgetFactory.cs` - UI factory (adaptive progress bars, status colors)
14. `src/ServerHub/Layout/LayoutEngine.cs` - Responsive layout calculator (NEW)
15. `src/ServerHub/Application.cs` - Main application logic (with action handler, layout engine)
16. `src/ServerHub/Program.cs` - Entry point with --discover support
17. `widgets/system.sh` - System monitoring widget
18. `widgets/disks.sh` - Disk usage widget
19. `widgets/docker.sh` - Docker status widget (with example actions)
20. `config.yaml` - Example configuration file (with layout options)
21. `generate-checksums.sh` - Build-time checksum generator
22. `install.sh` - User-local installation script
23. `uninstall.sh` - Uninstallation script

## Development Workflow

### Using --widgets-path for Development

The `--widgets-path` option allows loading widgets from a custom directory without installing them. This is ideal for:
- **Widget development**: Test widgets without running install.sh
- **Custom deployments**: Load widgets from non-standard locations
- **CI/CD**: Run widgets from build artifacts

**Examples:**

```bash
# Development: test widgets in current directory
serverhub --widgets-path ./widgets

# Development: test specific widget folder
serverhub --widgets-path ~/projects/my-widgets

# Combined with custom config
serverhub --widgets-path ./dev-widgets ./dev-config.yaml

# Run compute-checksums on dev widgets
serverhub --widgets-path ./dev-widgets --compute-checksums
```

**Search Path Priority:**
When `--widgets-path` is specified, widgets are searched in this order:
1. Custom path (highest priority)
2. `~/.config/serverhub/widgets/`
3. `~/.local/share/serverhub/widgets/`

This means custom path widgets can override bundled widgets by using the same filename.

**Security Behavior:**
- Widgets from custom path are treated like user widgets (not bundled)
- Checksums optional but warned if missing
- Validation still enforced (executable check, no path traversal)
- Warning shown: `Widget 'foo.sh' from custom path has no checksum (dev mode)`

**Example Development Session:**

```bash
# 1. Create dev widgets directory
mkdir -p ~/dev/serverhub-widgets
cd ~/dev/serverhub-widgets

# 2. Create test widget
cat > test.sh << 'EOF'
#!/bin/bash
echo "title: Test Widget"
echo "refresh: 1"
echo "row: [status:ok] Testing from dev path"
echo "row: Time: $(date +%H:%M:%S)"
EOF
chmod +x test.sh

# 3. Create dev config
cat > config.yaml << 'EOF'
layout:
  order: [test]

widgets:
  test:
    path: test.sh
    refresh: 1
    priority: 1

security:
  require_checksums: false
  allow_user_widgets: true
EOF

# 4. Run ServerHub with dev widgets
serverhub --widgets-path . config.yaml
```

## Testing & Verification

### Manual Testing Steps
1. **Build**: `dotnet build`
2. **Compute checksums**: `dotnet run -- --compute-checksums` (prints only)
3. **Update config.yaml** with checksums
4. **Run**: `dotnet run`
5. **Verify**:
   - All 3 widgets display in correct layout
   - System widget updates every 2 seconds
   - Disks widget updates every 30 seconds
   - Docker widget updates every 5 seconds
   - Progress bars show correct values/clamped 0-100
   - Status indicators use correct colors
   - Respects small terminal sizes (no negative width/height)
   - Pressing Ctrl+C exits cleanly and stops child processes

### Security Testing
1. **Checksum tamper**: Modify a script after config load, verify rejection (hash on each execution)
2. **Symlink attack**: Symlink to different script, verify detection on any symlinked segment
3. **Path traversal**: Try `../` in paths, verify rejection
4. **Timeout**: Create slow script (sleep 15), verify 10s timeout with kill
5. **Environment isolation**: Script tries to read sensitive env or $HOME/.ssh, verify limited access
6. **Missing checksum**: Remove checksum from system widget, verify error; user widget shows warning if policy allows
7. **Output size**: Script prints huge output, verify truncation/capping

### Error Handling
1. **Missing script**: Remove a script file, verify error display in widget
2. **Invalid YAML**: Corrupt config.yaml, verify error message
3. **Parse failure**: Widget outputs invalid protocol, verify error display
4. **Script failure**: Widget exits with non-zero, verify error display and retry
5. **Non-zero/timeout differentiation**: Ensure UI shows status vs. timeout distinctly

## NativeAOT Compatibility

All code uses NativeAOT-compatible patterns:
- No reflection for core functionality
- YamlDotNet compiled serialization
- Process creation is AOT-safe
- SharpConsoleUI is AOT-compatible

Build for NativeAOT:
```bash
dotnet publish -r linux-x64 -c Release
# Single-file executable in bin/Release/net9.0/linux-x64/publish/
```

## Reference Skeletons

### ScriptValidator (Updated for User-Local)
```csharp
public class ScriptValidator
{
    public record ValidationResult(
        bool IsValid,
        string? ErrorMessage,
        bool HasWarning = false,
        string? WarningMessage = null,
        string? ResolvedPath = null,
        string? ResolvedChecksum = null);

    public ValidationResult Validate(string path, string? configChecksum, SecurityConfig security)
    {
        // Shorthand widget name (no path separators)
        if (!path.Contains('/') && !path.Contains('\\'))
        {
            var resolved = WidgetPaths.FindWidget(path);
            if (resolved == null)
                return new(false, $"Widget '{path}' not found in search paths");

            var filename = Path.GetFileName(resolved);

            // Bundled widget?
            if (WidgetPaths.IsBundledPath(resolved))
            {
                if (!BundledWidgets.Checksums.TryGetValue(filename, out var expectedChecksum))
                    return new(false, $"No checksum registered for bundled widget '{filename}'");

                var actualChecksum = ComputeSha256(resolved);
                if (!actualChecksum.Equals(expectedChecksum, StringComparison.OrdinalIgnoreCase))
                    return new(false, $"Bundled widget '{filename}' has been tampered with!");

                if (!IsExecutable(resolved))
                    return new(false, $"Bundled widget '{filename}' is not executable");

                return new(true, null, false, null, resolved, actualChecksum);
            }
            else
            {
                // User override
                if (string.IsNullOrEmpty(configChecksum))
                {
                    var warn = $"User widget '{filename}' overrides bundled widget but has no checksum";
                    if (security.RequireChecksums)
                        return new(false, warn);
                    return new(true, null, true, warn, resolved, null);
                }

                var actualChecksum = ComputeSha256(resolved);
                if (!actualChecksum.Equals(configChecksum, StringComparison.OrdinalIgnoreCase))
                    return new(false, $"Checksum mismatch for '{filename}'");

                if (!IsExecutable(resolved))
                    return new(false, $"Widget '{filename}' is not executable");

                return new(true, null, false, null, resolved, actualChecksum);
            }
        }

        // Explicit path validation
        var expanded = ExpandHome(path);
        if (string.IsNullOrWhiteSpace(expanded))
            return new(false, "Path is empty");

        if (!Path.IsPathRooted(expanded))
            return new(false, "Path must be absolute or a widget name");

        if (expanded.Contains(".."))
            return new(false, "Path traversal (..) is not allowed");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var allowedRoots = new[]
        {
            Path.Combine(home, ".config", "serverhub"),
            Path.Combine(home, ".local", "share", "serverhub")
        };

        if (!allowedRoots.Any(root => expanded.StartsWith(root, StringComparison.Ordinal)))
            return new(false, "Path must be under ~/.config/serverhub or ~/.local/share/serverhub");

        var resolved = ResolveRealPath(expanded);
        if (resolved == null)
            return new(false, "Symlink resolution failed");

        if (!File.Exists(resolved))
            return new(false, "File does not exist");

        if (IsSymlinkedOutsideAllowedRoot(expanded, resolved, allowedRoots))
            return new(false, "Symlink escapes allowed root");

        if (!IsExecutable(resolved))
            return new(false, "File is not executable");

        if (string.IsNullOrEmpty(configChecksum))
        {
            var warn = "Custom widget missing checksum";
            if (security.RequireChecksums)
                return new(false, warn);
            return new(true, null, true, warn, resolved, null);
        }

        var checksum = ComputeSha256(resolved);
        if (!checksum.Equals(configChecksum, StringComparison.OrdinalIgnoreCase))
            return new(false, "Checksum mismatch");

        return new(true, null, false, null, resolved, checksum);
    }

    private static string ExpandHome(string path)
    {
        if (path.StartsWith("~"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Join(home, path.TrimStart('~', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        return path;
    }

    public static string ComputeSha256(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? ResolveRealPath(string path)
    {
        try
        {
            return Path.GetFullPath(new FileInfo(path).FullName);
        }
        catch { return null; }
    }

    private static bool IsSymlinkedOutsideAllowedRoot(string original, string resolved, string[] allowedRoots)
    {
        return !allowedRoots.Any(root => resolved.StartsWith(root, StringComparison.Ordinal));
    }

    private static bool IsExecutable(string path)
    {
#if WINDOWS
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".exe" or ".cmd" or ".bat" or ".sh";
#else
        var file = new FileInfo(path);
        return file.Exists && (file.Attributes & FileAttributes.Directory) == 0;
        // For stronger check, use: (file.UnixFileMode & UnixFileMode.UserExecute) != 0
#endif
    }
}
```

### ScriptRunner
```csharp
public class ScriptRunner
{
    public enum ExecutionStatus { Success, NonZeroExit, Timeout, ValidationFailed, Crashed }

    public record ExecutionResult(
        ExecutionStatus Status,
        string Output,
        string? Error,
        int ExitCode,
        string? ResolvedPath,
        string? ResolvedChecksum);

    private readonly ScriptValidator _validator;
    public ScriptRunner(ScriptValidator validator) => _validator = validator;

    public async Task<ExecutionResult> ExecuteAsync(WidgetConfig config, SecurityConfig security, CancellationToken ct)
    {
        var validation = _validator.Validate(config.Path, config.Sha256, security);
        if (!validation.IsValid)
            return new(ExecutionStatus.ValidationFailed, "", validation.ErrorMessage, -1, null, null);

        var psi = new ProcessStartInfo
        {
            FileName = validation.ResolvedPath!,
            WorkingDirectory = Path.GetDirectoryName(validation.ResolvedPath!) ?? Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        ConfigureEnv(psi.Environment);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        if (!process.Start())
            return new(ExecutionStatus.Crashed, "", "Failed to start process", -1, validation.ResolvedPath, validation.ResolvedChecksum);

        var stdoutTask = ReadWithCapAsync(process.StandardOutput, 64 * 1024, ct);
        var stderrTask = ReadWithCapAsync(process.StandardError, 32 * 1024, ct);

        var waitTask = Task.Run(() => process.WaitForExit(10_000), ct);
        var completed = await Task.WhenAny(waitTask, Task.Delay(10_000, ct));
        if (completed != waitTask && !process.HasExited)
        {
            KillProcessTree(process);
            return new(ExecutionStatus.Timeout, await stdoutTask, await stderrTask, -1, validation.ResolvedPath, validation.ResolvedChecksum);
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var exit = process.HasExited ? process.ExitCode : -1;

        if (exit != 0)
            return new(ExecutionStatus.NonZeroExit, stdout, stderr, exit, validation.ResolvedPath, validation.ResolvedChecksum);

        return new(ExecutionStatus.Success, stdout, null, 0, validation.ResolvedPath, validation.ResolvedChecksum);
    }

    private static void ConfigureEnv(StringDictionary env)
    {
        env.Clear();
        env["PATH"] = "/usr/bin:/bin";
        env["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        env["USER"] = Environment.UserName;
        env["LANG"] = "C.UTF-8";
        // Explicitly avoid LD_PRELOAD, LD_LIBRARY_PATH, SSH_*, GIT_*, PYTHON*, TERM, etc.
    }

    private static async Task<string> ReadWithCapAsync(StreamReader reader, int capBytes, CancellationToken ct)
    {
        var buffer = new char[capBytes];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        return new string(buffer, 0, read);
    }

    private static void KillProcessTree(Process process)
    {
#if WINDOWS
        // Use JobObject or Process.Kill(entireProcessTree:true) on .NET 9
        process.Kill(entireProcessTree: true);
#else
        try { process.Kill(entireProcessTree: true); }
        catch { try { process.Kill(); } catch { } }
#endif
    }
}
```

### OutputParser
```csharp
public class OutputParser
{
    private static readonly Regex ProgressRegex = new("\\[progress:(\\d+)\\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StatusRegex = new("\\[status:(ok|warn|error)\\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public WidgetData Parse(string scriptOutput)
    {
        var data = new WidgetData { Title = string.Empty, RefreshInterval = 5 };
        var lines = scriptOutput.Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
            {
                data.Title = line[6..].Trim();
                continue;
            }

            if (line.StartsWith("refresh:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(line[8..].Trim(), out var r) && r > 0)
                    data.RefreshInterval = r;
                continue;
            }

            if (line.StartsWith("row:", StringComparison.OrdinalIgnoreCase))
            {
                var body = line[4..].Trim();
                var row = new WidgetRow { Text = body };

                foreach (Match m in ProgressRegex.Matches(body))
                {
                    if (int.TryParse(m.Groups[1].Value, out var val))
                        row.Elements.Add(new ProgressElement { Value = Math.Clamp(val, 0, 100) });
                }

                foreach (Match m in StatusRegex.Matches(body))
                {
                    row.Elements.Add(new StatusElement { State = m.Groups[1].Value.ToLowerInvariant() });
                }

                data.Rows.Add(row);
            }
        }
        return data;
    }
}
```

### Result<T> & Config Validation Outline
```csharp
public readonly record struct Result<T>(bool IsSuccess, T? Value, string? Error)
{
    public static Result<T> Ok(T value) => new(true, value, null);
    public static Result<T> Fail(string error) => new(false, default, error);
}
```

ConfigLoader validation steps:
- Deserialize YAML to interim object.
- Validate non-empty widget IDs/paths, refresh > 0.
- Enforce unique widget IDs; flag unused widget definitions.
- Validate layout rows are non-empty and all IDs exist; decide policy on duplicates.
- Expand `~` via `Environment.SpecialFolder.UserProfile`, normalize separators.
- Widget path validation delegated to ScriptValidator (handles search paths and bundled vs custom).
- Apply `AllowUserWidgets` policy if needed (or remove if unused).
- Return `Result<ServerHubConfig>` with clear error strings.

## Notes

### ConsoleEx Integration
- Use `ConsoleWindowSystem(RenderMode.Buffer)` for efficient rendering
- Use `.WithAsyncWindowThread()` for independent widget refresh
- Use `MarkupControl` for text with Spectre markup
- Use `SpectreRenderableControl` wrapping `BarChart` for progress bars
- Use `window.FindControl<T>("name")` for control updates
- Never use `Console.WriteLine()` - corrupts the UI

### Future Enhancements (Not in Initial Implementation)
- Button actions (run commands on click)
- Interactive widgets (text input, dropdowns)
- Plugin system for custom widgets
- Remote widget execution (SSH)
- Widget templates/marketplace
- Historical data graphing
- Alert/notification system
