# Widget Protocol Reference

Widgets are executable scripts that output structured text to stdout. ServerHub parses this output to render dashboard widgets.

## Protocol Elements

### title: \<text\>

Sets the widget title displayed in the header.

```
title: CPU Usage
```

### refresh: \<seconds\>

Suggested refresh interval in seconds. Note: The config file `refresh` value takes precedence over this.

```
refresh: 5
```

### row: \<content\>

A display row. Supports inline elements and Spectre.Console markup.

```
row: Hello World
row: [status:ok] All systems operational
row: [progress:75:inline]
row: [grey70]Muted text[/]
```

### action: [flags] \<Label\>:\<command\>

Defines an interactive action that users can execute from the expanded widget view.

**Basic Syntax:**
```
action: Label:command
action: [flags] Label:command
```

**Flags** (comma-separated in brackets):

| Flag | Description |
|------|-------------|
| `danger` | Shows warning indicator, requires confirmation |
| `sudo` | Executes with elevated privileges (prompts for password if needed) |
| `refresh` | Refreshes the widget after successful execution |
| `timeout=N` | Custom timeout in seconds (default: 60) |

**Timeout Values:**
- `timeout=N` - Command times out after N seconds
- `timeout=0` - No timeout (runs indefinitely until terminated)
- No flag - Uses default 60 second timeout

**Examples:**
```bash
# Simple action
action: View logs:journalctl -n 50

# Action with flags
action: [danger,refresh] Restart nginx:systemctl restart nginx
action: [sudo] Update packages:apt update
action: [sudo,danger,refresh] Reboot:reboot

# Actions with custom timeout
action: [sudo,timeout=120] Update cache:apt update
action: [sudo,danger,timeout=600] Full upgrade:apt full-upgrade -y
action: [timeout=0] View live log:journalctl -f -n 100
```

**UI Behavior:**
- Actions appear as buttons in the expanded widget view
- `danger` actions show a warning banner before execution
- `sudo` actions show a password prompt if credentials aren't cached
- `timeout=0` actions show a "no timeout limit" warning
- Progress bar shows elapsed time vs timeout (or pulsing animation for infinite)

## Inline Elements

### Status Indicator

Displays a colored status icon before the row content.

```
[status:STATE]
```

| State | Color | Use Case |
|-------|-------|----------|
| `ok` | Green | Normal operation |
| `info` | Blue | Informational |
| `warn` | Yellow | Warning condition |
| `error` | Red | Error/critical state |

Example:
```
row: [status:ok] Service running
row: [status:warn] High memory usage
row: [status:error] Disk full
```

### Progress Bar

Displays a visual progress bar.

```
[progress:VALUE]
[progress:VALUE:STYLE]
```

- **VALUE**: Integer 0-100
- **STYLE**: `inline` (default) or `chart`

Example:
```
row: [progress:75]
row: [progress:45:inline]
row: [progress:90:chart]
```

### Braille Sparkline

Displays an inline mini-graph using braille characters for compact trend visualization.

```
[sparkline:VALUES]
[sparkline:VALUES:COLOR]
```

- **VALUES**: Comma-separated numbers (e.g., `10,20,15,25,30`)
- **COLOR**: Optional Spectre.Console color (default: `grey70`)

Example:
```
row: Load trend: [sparkline:45,48,52,55,50,53:green]
row: Memory: [sparkline:60,62,65,70,68,72,75:yellow] 75%
```

### Mini Progress Bar

Displays a compact inline progress indicator (smaller than the standard progress bar).

```
[miniprogress:VALUE]
[miniprogress:VALUE:WIDTH]
```

- **VALUE**: Integer 0-100
- **WIDTH**: Character width 3-20 (default: 10)

Example:
```
row: CPU: [miniprogress:75] 75%
row: RAM: [miniprogress:85:15] 85%
```

### Multi-Column Table

Displays structured data in aligned columns. Table directives are used without the `row:` prefix.

```
[table:HEADER1|HEADER2|HEADER3]
[tablerow:VALUE1|VALUE2|VALUE3]
[tablerow:VALUE1|VALUE2|VALUE3]
```

- **Headers**: Pipe-separated column names
- **Rows**: Pipe-separated values (supports Spectre.Console markup)

Example:
```
[table:Service|Status|Memory]
[tablerow:nginx|[green]running[/]|45MB]
[tablerow:mysql|[green]running[/]|320MB]
[tablerow:redis|[yellow]warning[/]|89MB]
```

### Horizontal Divider

Displays a full-width horizontal line to separate sections.

```
[divider]
[divider:CHARACTER]
[divider:CHARACTER:COLOR]
```

- **CHARACTER**: Single character (default: `─`)
- **COLOR**: Optional Spectre.Console color (default: `grey70`)

Example:
```
row: [bold]Section 1[/]
row: Content here...
row: [divider]
row: [bold]Section 2[/]
row: More content...
row: [divider:═:cyan1]
```

### Multi-Line Graph

Displays a braille chart for data visualization (4 lines tall).

```
[graph:VALUES]
[graph:VALUES:COLOR]
[graph:VALUES:COLOR:LABEL]
```

- **VALUES**: Comma-separated numbers
- **COLOR**: Optional Spectre.Console color (default: `cyan1`)
- **LABEL**: Optional label text

Example:
```
row: [graph:10,20,15,25,30,28,35,40:green:CPU Load]
row: [graph:60,62,65,70,68,72,75,78:yellow]
```

## Spectre.Console Markup

Row content supports [Spectre.Console markup](https://spectreconsole.net/markup) for styling:

### Colors
```
[red]Error text[/]
[green]Success text[/]
[yellow]Warning text[/]
[blue]Info text[/]
[cyan1]Accent text[/]
[grey70]Muted text[/]
```

### Styles
```
[bold]Bold text[/]
[italic]Italic text[/]
[underline]Underlined text[/]
[dim]Dimmed text[/]
```

### Combined
```
[bold red]Bold red text[/]
[grey70 italic]Muted italic[/]
```

## Complete Example

```bash
#!/bin/bash
# my-widget.sh - Example custom widget with rich elements

echo "title: System Monitor"
echo "refresh: 5"

# Status with sparkline trend
cpu_history="45,48,52,55,50,53,58,60,57,62"
echo "row: CPU: 62% [sparkline:${cpu_history}:green]"
echo "row: [miniprogress:62:15] Current load"

# Divider
echo "row: [divider]"

# Table for services
echo "row: [bold]Services[/]"
echo "[table:Name|Status|Memory]"
echo "[tablerow:nginx|[green]running[/]|45MB]"
echo "[tablerow:mysql|[green]running[/]|320MB]"

# Divider
echo "row: [divider:─:cyan1]"

# Graph for extended mode
if [[ "$1" == "--extended" ]]; then
    memory_history="60,62,65,70,68,72,75,78,80,85"
    echo "row: [graph:${memory_history}:yellow:Memory usage (10 samples)]"
fi
```

## Security

ServerHub enforces mandatory checksum validation to protect against widget tampering.

### Trust Hierarchy

| Source | Checksum Location | Trust Level | Bypass with --dev-mode? |
|--------|-------------------|-------------|-------------------------|
| **Bundled** | Hardcoded at build | Highest (maintainer reviewed) | No - always validated |
| **Config sha256** | User adds to YAML | High (user verified) | Yes |
| **--discover** | Captured at approval | High (user reviewed code) | N/A |

### Security Principle

Checksums must come from a **trusted source at a trusted moment**:
- **Bundled**: Build-time (maintainer reviewed code)
- **Third-party**: Author provides checksum, user verifies
- **Self-developed**: Author calculates after code is finalized
- **--discover**: Captured after user reviews code preview

Checksums should **NOT** be auto-generated from current file state (attacker could modify then regenerate).

### Custom Widgets (Required)

All custom widgets (in `~/.config/serverhub/widgets/`) **require** SHA256 checksum validation:

```yaml
widgets:
  my-widget:
    path: my-widget.sh
    sha256: a1b2c3d4e5f6...  # REQUIRED for custom widgets
    refresh: 10
```

Without a checksum, custom widgets will fail to load with a helpful error showing the required checksum.

### Adding Widgets

**Option 1: Discovery (Recommended)**
```bash
# Discover and add new widgets interactively
serverhub --discover

# Review code preview → approve → auto-added with checksum
```

**Option 2: Manual**
```bash
# Calculate checksum
sha256sum ~/.config/serverhub/widgets/my-widget.sh

# Add to config.yaml with checksum
```

### Verifying Checksums

```bash
# Verify all widget checksums (shows VALID/MISMATCH/MISSING)
serverhub --verify-checksums
```

### Development Mode

For widget development, use `--dev-mode` to skip custom widget checksum validation:

```bash
# Skip checksum validation for custom widgets only
serverhub --dev-mode --widgets-path ./my-dev-widgets
```

**Important:**
- Bundled widgets are **always** validated, even in dev mode
- Dev mode shows prominent warnings (status bar, orange border, startup dialog)
- **Never use --dev-mode in production**

| Validation | Normal Mode | --dev-mode |
|------------|-------------|------------|
| Path restrictions | Enforced | Enforced |
| Symlink detection | Enforced | Enforced |
| Executable check | Enforced | Enforced |
| Bundled checksum | Enforced | Enforced |
| Custom widget checksum | Enforced | **Skipped** |

### Bundled Widgets

Bundled widgets (in `~/.local/share/serverhub/widgets/`) are pre-validated at build time with hardcoded checksums and don't require checksums in your config.

### Workflow Examples

**Third-Party Widget:**
```bash
# 1. Download widget
wget https://example.com/monitoring.sh

# 2. Verify author's checksum
sha256sum monitoring.sh
# Compare with author's published checksum

# 3. Move to widgets directory
mv monitoring.sh ~/.config/serverhub/widgets/

# 4. Discover and add
serverhub --discover
```

**Self-Developed Widget:**
```bash
# 1. Develop with --dev-mode
serverhub --dev-mode --widgets-path ./my-widgets

# 2. When done, add via --discover
cp my-widgets/custom.sh ~/.config/serverhub/widgets/
serverhub --discover

# 3. Run normally
serverhub
```

## Extended Mode

When a user opens the expanded view (double-click or Enter on a widget), the script is re-executed with the `--extended` argument. This allows scripts to output additional detail that wouldn't fit in the dashboard view.

### Detecting Extended Mode

Check for `--extended` in your script arguments:

```bash
#!/bin/bash
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi
```

### Usage Pattern

```bash
echo "title: My Widget"
echo "refresh: 5"

# Always show summary
echo "row: [status:ok] Service running"

# Show additional detail only in extended mode
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [bold]Extended Details:[/]"
    echo "row: PID: 12345"
    echo "row: Uptime: 5 days"
    echo "row: Memory: 256MB"
    # ... more detailed info
fi
```

### Best Practices

- **Dashboard view**: Keep output concise (fits within `max_lines`)
- **Extended view**: Include full details, logs, stats, etc.
- Scripts without `--extended` handling work normally (same output in both views)
- Extended mode is optional - scripts don't need to handle it

## Output Requirements

- Scripts must be executable (`chmod +x`)
- Output to stdout only
- One protocol element per line
- Exit code 0 for success (non-zero shows error state)
- Keep execution time short (< refresh interval)
