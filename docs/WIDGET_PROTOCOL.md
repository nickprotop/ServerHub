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

Displays a visual progress bar with optional gradient coloring.

```
[progress:VALUE]
[progress:VALUE:GRADIENT]
[progress:VALUE:GRADIENT:STYLE]
[progress:VALUE:STYLE]
```

- **VALUE**: Integer 0-100
- **GRADIENT**: Optional gradient name or custom gradient (see Gradients section)
- **STYLE**: `inline` (default) or `chart`

**Default Behavior** (no gradient specified):
- Green for 0-69%
- Yellow for 70-89%
- Red for 90-100%

**Predefined Gradients:**
- `cool` - Blue to cyan (temperature/load themes)
- `warm` - Yellow to orange to red (heat/pressure themes)
- `spectrum` - Blue to green to yellow to red (full range)
- `grayscale` - Grey11 to grey100 (monochrome)

**Custom Gradients:**
- Two colors: `blue→red`
- Multiple colors: `blue→green→yellow→red`

Example:
```
row: [progress:75]                          # Default threshold colors
row: [progress:45:inline]                   # Default with explicit style
row: [progress:90:warm]                     # Warm gradient
row: [progress:75:cool:inline]              # Cool gradient with style
row: [progress:60:blue→green→red]           # Custom gradient
```

### Sparkline

Displays an inline mini-graph using smooth Unicode block characters for compact trend visualization.

```
[sparkline:VALUES]
[sparkline:VALUES:COLOR]
[sparkline:VALUES:COLOR:WIDTH]
[sparkline:VALUES:GRADIENT]
[sparkline:VALUES:GRADIENT:WIDTH]
```

- **VALUES**: Comma-separated numbers (e.g., `10,20,15,25,30`)
- **COLOR**: Optional Spectre.Console color (default: `grey70`)
- **GRADIENT**: Optional gradient name or custom gradient (see Gradients section)
- **WIDTH**: Optional character width (default: 30, pads with background if data points < width)

**Character Set:** Uses Unicode block characters (▁▂▃▄▅▆▇█) for smooth visualization

**Predefined Gradients:**
- `cool`, `warm`, `spectrum`, `grayscale`

**Custom Gradients:**
- `blue→red`, `green→yellow→red`, etc.

Example:
```
row: Load trend: [sparkline:45,48,52,55,50,53:green]         # Solid color, default width (30)
row: Memory: [sparkline:60,62,65,70,68,72,75:warm] 75%       # Warm gradient, default width
row: CPU: [sparkline:10,20,30,40,50:blue→red]                # Custom gradient, default width
row: Traffic: [sparkline:5,10,15,20:green:15]                # Narrow sparkline (15 chars)
```

### Mini Progress Bar

Displays a compact inline progress indicator with optional gradient coloring.

```
[miniprogress:VALUE]
[miniprogress:VALUE:WIDTH]
[miniprogress:VALUE:WIDTH:GRADIENT]
```

- **VALUE**: Integer 0-100
- **WIDTH**: Character width 3-20 (default: 10)
- **GRADIENT**: Optional gradient name or custom gradient (see Gradients section)

**Default Behavior** (no gradient specified):
- Green for 0-69%
- Yellow for 70-89%
- Red for 90-100%

**Predefined Gradients:**
- `cool`, `warm`, `spectrum`, `grayscale`

**Custom Gradients:**
- `blue→red`, `green→yellow→red`, etc.

Example:
```
row: CPU: [miniprogress:75] 75%                         # Default threshold colors
row: RAM: [miniprogress:85:15] 85%                      # Default with custom width
row: Temp: [miniprogress:60:12:cool] 60°C               # Cool gradient
row: Load: [miniprogress:90:10:warm]                    # Warm gradient
row: Disk: [miniprogress:45:15:blue→green→red]          # Custom gradient
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

Displays a smooth block character chart for data visualization (4 lines tall) with optional gradient coloring.

```
[graph:VALUES]
[graph:VALUES:COLOR]
[graph:VALUES:COLOR:LABEL]
[graph:VALUES:COLOR:LABEL:MIN-MAX]
[graph:VALUES:COLOR:LABEL:MIN-MAX:WIDTH]
[graph:VALUES:GRADIENT]
[graph:VALUES:GRADIENT:LABEL]
[graph:VALUES:GRADIENT:LABEL:MIN-MAX]
[graph:VALUES:GRADIENT:LABEL:MIN-MAX:WIDTH]
```

- **VALUES**: Comma-separated numbers
- **COLOR**: Optional Spectre.Console color (default: `cyan1`)
- **GRADIENT**: Optional gradient name or custom gradient (see Gradients section)
- **LABEL**: Optional label text
- **MIN-MAX**: Optional fixed scale range (e.g., `0-100` for percentage graphs)
- **WIDTH**: Optional character width (default: 30, pads with background if data points < width)

**Character Set:** Uses Unicode block characters (▁▂▃▄▅▆▇█) for smooth visualization with 36 vertical levels (4 rows × 9 levels)

**Predefined Gradients:**
- `cool` - Blue to cyan (temperature/load themes)
- `warm` - Yellow to orange to red (heat/pressure themes)
- `spectrum` - Blue to green to yellow to red (full range)
- `grayscale` - Grey11 to grey100 (monochrome)

**Custom Gradients:**
- `blue→red`, `green→yellow→red`, etc.

Example:
```
row: [graph:10,20,15,25,30,28,35,40:green:CPU Load]        # Solid color, default width (30)
row: [graph:60,62,65,70,68,72,75,78:warm:Temperature]      # Warm gradient
row: [graph:5,10,15,20,25,30:blue→red:Load]                # Custom gradient
row: [graph:45,50,55,60,58,62:cool]                        # Cool gradient no label
row: [graph:45,50,62,58,65,70:cool:CPU %:0-100]            # Fixed 0-100% scale
row: [graph:10,15,20,25:green:Load:0-100:40]               # Custom width (40 chars)
```

### Line Graph

Displays a smooth line chart with connected points for time-series visualization. Supports both Braille characters (default, smooth) and ASCII style rendering.

```
[line:VALUES:COLOR:LABEL:MIN-MAX:WIDTH:HEIGHT]
[line:VALUES:COLOR:LABEL:MIN-MAX:WIDTH:HEIGHT:STYLE]
[line:VALUES:GRADIENT:LABEL:MIN-MAX:WIDTH:HEIGHT]
[line:VALUES:GRADIENT:LABEL:MIN-MAX:WIDTH:HEIGHT:STYLE]
```

- **VALUES**: Comma-separated numbers (e.g., `10,20,15,30,25`)
- **COLOR**: Optional Spectre.Console color (default: `cyan1`)
- **GRADIENT**: Optional gradient name or custom gradient (see Gradients section)
- **LABEL**: Optional label text displayed above the graph
- **MIN-MAX**: Optional fixed scale range (e.g., `0-100` for percentage graphs)
- **WIDTH**: Character width of the graph (default: 60)
- **HEIGHT**: Character height of the graph (default: 8)
- **STYLE**: Optional rendering style - `braille` (default, smooth) or `ascii` (block characters)

**Rendering Styles:**
- **braille** (default): Uses Braille Unicode characters for smooth, high-resolution line rendering
- **ascii**: Uses block characters (█, ▀, ▄) for a more traditional ASCII art look

**Predefined Gradients:**
- `cool` - Blue to cyan (temperature/load themes)
- `warm` - Yellow to orange to red (heat/pressure themes)
- `spectrum` - Blue to green to yellow to red (full range)
- `grayscale` - Grey11 to grey100 (monochrome)

**Custom Gradients:**
- `blue→red`, `green→yellow→red`, etc.

Example:
```
row: [line:10,20,15,30,25,35,40,30,20,10:cyan:Sample Data:0-50:60:8]           # Braille style (default)
row: [line:10,20,15,30,25,35,40,30,20,10:cyan:Sample Data:0-50:60:8:ascii]    # ASCII style
row: [line:5,15,10,25,20,35,30,45,40,50:warm:CPU Usage:0-100:50:6]            # Warm gradient, compact
row: [line:10,15,20,25,30,25,20,15,10:blue:Memory:0-40:40:4]                  # Small graph
row: [line:25,25,25,25,25:grey70:Constant:0-50:30:4]                          # Flat line
```

### Storage-Based Elements

ServerHub includes a built-in time-series storage system that allows widgets to persist data and query historical metrics. These elements interact with the storage backend.

#### Datastore Directive

Persists metric data to the SQLite database using InfluxDB-style line protocol.

```
datastore: MEASUREMENT[,tag=val,tag=val] field=val[,field=val] [timestamp]
```

- **MEASUREMENT**: Required alphanumeric identifier (e.g., `cpu_usage`, `memory_used`)
- **tags**: Optional comma-separated key=value pairs for grouping (e.g., `core=0,host=srv01`)
- **fields**: Required comma-separated key=value pairs (numeric, boolean, or quoted strings)
- **timestamp**: Optional Unix timestamp in seconds (auto-generated if omitted)

Examples:
```bash
# Simple metric
echo "datastore: cpu_usage value=75.5"

# With tags
echo "datastore: cpu_usage,core=0,host=srv01 value=75.5,temp=65"

# Multiple fields
echo "datastore: disk_io,device=sda reads=1500,writes=2300"

# Explicit timestamp
echo "datastore: metric,tag=x value=100 1707348000"
```

**Note:** Data is automatically scoped to the widget ID. Each widget's data is isolated in the database.

#### Datafetch (Inline)

Retrieves and displays a single value from stored data.

```
[datafetch:KEY]
[datafetch:KEY:AGGREGATION:TIMERANGE]
```

- **KEY**: Measurement key in format `measurement.field` (e.g., `cpu_usage.value`)
- **AGGREGATION**: `latest` (default), `avg`, `max`, `min`, `sum`, `count`
- **TIMERANGE**: Time range like `30s`, `5m`, `1h`, `24h`, `7d`, or `last_10`

Examples:
```bash
# Latest value
echo "row: CPU: [datafetch:cpu_usage.value]%"

# Average over last 30 seconds
echo "row: 30s avg: [datafetch:cpu_usage.value:avg:30s]%"

# Maximum over 1 hour
echo "row: Peak (1h): [datafetch:cpu_usage.value:max:1h]%"

# Count of samples
echo "row: Samples: [datafetch:cpu_usage.value:count:1h]"
```

If no data is available, renders as `--`.

#### History Graph

Renders stored time-series data as a vertical bar chart (4 lines tall).

```
[history_graph:KEY:TIMERANGE]
[history_graph:KEY:TIMERANGE:COLOR]
[history_graph:KEY:TIMERANGE:COLOR:LABEL]
[history_graph:KEY:TIMERANGE:COLOR:LABEL:MIN-MAX]
[history_graph:KEY:TIMERANGE:COLOR:LABEL:MIN-MAX:WIDTH]
```

- **KEY**: Measurement key (e.g., `cpu_usage.value`)
- **TIMERANGE**: Time range to query (e.g., `30s`, `1m`, `1h`, `24h`)
- **COLOR/GRADIENT**: Optional color or gradient name
- **LABEL**: Optional label text
- **MIN-MAX**: Optional fixed scale (e.g., `0-100`)
- **WIDTH**: Character width (default: 30)

Examples:
```bash
# Short-term (last 60 seconds)
echo "row: [history_graph:cpu_usage.value:60s:cool:Load %:0-100]"

# Medium-term (last hour)
echo "row: [history_graph:cpu_usage.value:1h:cyan:CPU Load:0-100:40]"

# Long-term (24 hours)
echo "row: [history_graph:cpu_usage.value:24h:warm:Temperature:0-100]"
```

#### History Sparkline (Inline)

Renders stored time-series data as an inline sparkline.

```
[history_sparkline:KEY:TIMERANGE]
[history_sparkline:KEY:TIMERANGE:COLOR]
[history_sparkline:KEY:TIMERANGE:COLOR:WIDTH]
```

- **KEY**: Measurement key (e.g., `cpu_usage.value`)
- **TIMERANGE**: Time range to query
- **COLOR/GRADIENT**: Optional color or gradient name
- **WIDTH**: Character width (default: 30)

Examples:
```bash
# Inline trend for last 30 seconds
echo "row: CPU trend: [history_sparkline:cpu_usage.value:30s:cool:20]"

# Memory trend over 5 minutes
echo "row: Mem: [history_sparkline:memory.used:5m:warm:25]"
```

#### History Line Graph

Renders stored time-series data as a smooth line chart.

```
[history_line:KEY:TIMERANGE:COLOR:LABEL:MIN-MAX:WIDTH:HEIGHT]
[history_line:KEY:TIMERANGE:COLOR:LABEL:MIN-MAX:WIDTH:HEIGHT:STYLE]
```

- **KEY**: Measurement key (e.g., `cpu_usage.value`)
- **TIMERANGE**: Time range to query (e.g., `5m`, `1h`, `24h`)
- **COLOR/GRADIENT**: Optional color or gradient name
- **LABEL**: Optional label text
- **MIN-MAX**: Optional fixed scale (e.g., `0-100`)
- **WIDTH**: Character width (default: 60)
- **HEIGHT**: Character height (default: 8)
- **STYLE**: `braille` (default) or `ascii`

Examples:
```bash
# 1 minute history
echo "row: [history_line:cpu_usage.value:1m:cyan:CPU:0-100:60:8:braille]"

# 5 minute history with gradient
echo "row: [history_line:cpu_usage.value:5m:warm:Load:0-100:50:6:braille]"

# 24 hour history
echo "row: [history_line:memory.used:24h:cool:Memory:0-100:80:10:braille]"
```

#### Time Range Format

All storage elements support these time range formats:

- **Seconds**: `10s`, `30s`, `60s`
- **Minutes**: `1m`, `5m`, `15m`, `30m`
- **Hours**: `1h`, `6h`, `12h`, `24h`
- **Days**: `7d`, `30d`
- **Samples**: `last_10`, `last_30`, `last_100`

#### Complete Storage Example

```bash
#!/bin/bash
echo "title: CPU Monitor"
echo "refresh: 5"

# Get current CPU load
CPU_LOAD=$(awk '{print $1}' /proc/loadavg)
CPU_PERCENT=$(awk "BEGIN {printf \"%.0f\", ($CPU_LOAD / $(nproc)) * 100}")

# Store the metric
echo "datastore: cpu_usage,host=$(hostname) value=$CPU_PERCENT"

# Display current + historical data
echo "row: [status:ok] Current: ${CPU_PERCENT}%"
echo "row: Latest: [datafetch:cpu_usage.value] | 30s avg: [datafetch:cpu_usage.value:avg:30s]"
echo "row: Trend: [history_sparkline:cpu_usage.value:1m:cool:20]"
echo "row: "
echo "row: [history_graph:cpu_usage.value:1m:cool:Last Minute:0-100:40]"

# Extended mode: longer history
if [[ "$1" == "--extended" ]]; then
    echo "row: "
    echo "row: [bold]24 Hour History[/]"
    echo "row: [history_line:cpu_usage.value:24h:warm:CPU Load:0-100:80:12:braille]"
fi
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
