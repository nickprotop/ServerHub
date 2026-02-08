# Widget Development Guide

Complete guide for creating, testing, and deploying custom ServerHub widgets.

## Table of Contents

- [Quick Start](#quick-start)
- [Creating Widgets from Templates](#creating-widgets-from-templates)
- [Template Reference](#template-reference)
- [Customizing Your Widget](#customizing-your-widget)
- [Testing Widgets](#testing-widgets)
- [Deploying Widgets](#deploying-widgets)
- [CI/CD Integration](#cicd-integration)
- [Protocol Reference](#protocol-reference)

## Quick Start

The fastest way to create and deploy a working widget:

```bash
# 1. Create widget from template
serverhub new-widget bash-basic --name my-monitor

# 2. Make it executable
chmod +x my-monitor.sh

# 3. Test it
serverhub test-widget my-monitor.sh --yes

# 4. Customize the monitoring logic (edit the file)

# 5. Test again
serverhub test-widget my-monitor.sh --yes

# 6. Add to dashboard
serverhub --discover
```

That's it! Your widget is now in the dashboard.

## Creating Widgets from Templates

Templates provide a complete widget structure with best practices and example code.

### Interactive Widget Creation

The interactive wizard guides you through the process:

```bash
serverhub new-widget
```

1. Select a template from the list
2. Fill in variables (with examples and defaults)
3. Choose output file path
4. Preview the generated content (optional)
5. Confirm creation

### Non-Interactive Creation

Specify all options on the command line:

```bash
# Basic usage
serverhub new-widget bash-basic --name my-widget

# With custom variables
serverhub new-widget bash-basic \
  --name cpu-monitor \
  --title "CPU Monitor" \
  --refresh 10 \
  --author "Jane Smith"

# With custom output path
serverhub new-widget python-basic \
  --name api-checker \
  --output ~/custom-widgets/api-checker.py
```

### List Available Templates

```bash
serverhub new-widget list
```

## Template Reference

### Bash Templates

#### bash-basic
Simple shell script with status indicators and actions.

**Difficulty:** Beginner
**Best for:** Quick monitoring scripts, simple checks, getting started

**Features:**
- Basic widget protocol implementation
- Status indicators
- Configurable refresh interval
- Action buttons

**Example:**
```bash
serverhub new-widget bash-basic --name disk-monitor --refresh 10
```

#### bash-advanced
Advanced shell script with sparklines, graphs, tables, and history tracking.

**Difficulty:** Intermediate
**Best for:** Complex monitoring, trending data, detailed statistics

**Features:**
- Dashboard vs Extended mode support
- History tracking with configurable cache
- Sparklines for trend visualization
- Graphs in extended mode
- Statistical tables
- Dynamic actions

**Example:**
```bash
serverhub new-widget bash-advanced --name system-monitor --max-samples 50
```

### Python Templates

#### python-basic
Basic Python widget with protocol implementation.

**Difficulty:** Beginner
**Best for:** Python developers, API monitoring, complex logic

**Features:**
- Basic widget protocol in Python
- Easy to extend with Python libraries
- Status indicators and actions
- No external dependencies required

**Example:**
```bash
serverhub new-widget python-basic --name network-monitor --refresh 5
```

#### python-advanced
Python widget with rich visualizations and error handling.

**Difficulty:** Advanced
**Best for:** Complex monitoring, data visualization, production use

**Features:**
- Dashboard vs Extended mode support
- Sparklines and graphs
- Statistical tables
- Built-in error handling
- Easy integration with Python ecosystem

**Example:**
```bash
serverhub new-widget python-advanced --name api-monitor
```

### Other Templates

#### csharp-script
C# widget using dotnet-script (no compilation required).

**Difficulty:** Intermediate
**Best for:** .NET developers, leveraging existing C# libraries

**Prerequisites:**
```bash
dotnet tool install -g dotnet-script
```

**Example:**
```bash
serverhub new-widget csharp-script --name service-monitor
```

#### powershell
Cross-platform PowerShell widget.

**Difficulty:** Beginner
**Best for:** Windows admins, cross-platform PowerShell users

**Prerequisites:** PowerShell Core installed

**Example:**
```bash
serverhub new-widget powershell --name process-monitor
```

### Template Variables

All templates support these common variables:

| Variable | Required | Description | Example |
|----------|----------|-------------|---------|
| `WIDGET_NAME` | Yes | Widget identifier (lowercase, hyphens, no spaces) | `cpu-monitor` |
| `WIDGET_TITLE` | No | Display title shown in dashboard | `CPU Monitor` |
| `REFRESH_INTERVAL` | No | Refresh interval in seconds (default: 5) | `10` |
| `AUTHOR` | No | Widget author name | `John Doe` |
| `DESCRIPTION` | No | Widget description | `Monitors CPU usage` |

Advanced templates may have additional variables (e.g., `CACHE_FILE`, `MAX_SAMPLES`).

## Customizing Your Widget

After creating a widget from a template, customize it to monitor what you need.

### Replace TODO Sections

Templates include TODO comments showing where to add your monitoring logic:

```bash
# TODO: Add your monitoring logic here
echo "row: [status:ok] Status: Operational"
```

### Example Customizations

**Bash - CPU Usage:**
```bash
CPU_USAGE=$(top -bn1 | grep "Cpu(s)" | awk '{print $2}' | cut -d'%' -f1)

if (( $(echo "$CPU_USAGE > 80" | bc -l) )); then
    echo "row: [status:error] CPU: ${CPU_USAGE}%"
else
    echo "row: [status:ok] CPU: ${CPU_USAGE}%"
fi
```

**Python - Memory Usage:**
```python
import psutil

mem = psutil.virtual_memory()
status = "error" if mem.percent > 80 else "ok"
print(f"row: [status:{status}] Memory: {mem.percent}%")
```

### Status Indicators

Use status indicators to show health:

```bash
echo "row: [status:ok] Everything is fine"
echo "row: [status:warning] Check this"
echo "row: [status:error] Critical issue"
```

### Graphs and Sparklines

Advanced templates support data visualization:

```bash
# Sparkline (compact trend)
echo "row: [sparkline:10,20,30,40,50,40,30,20:green]"

# Multi-line graph (4 rows tall)
echo "row: [graph:10,20,30,40,50,40,30,20:cyan:CPU Load:0-100:30]"

# Line graph (smooth connected line)
echo "row: [line:10,20,15,30,25,35,40,30,20,10:cyan:Temperature:0-50:60:8]"
echo "row: [line:5,15,25,35,45:warm:CPU History:0-100:50:6:braille]"  # Braille style
echo "row: [line:5,15,25,35,45:blue:Memory:0-100:50:6:ascii]"          # ASCII style
```

### Historical Data and Storage System

**Recommended approach for tracking historical data:**

ServerHub includes a powerful SQLite-based storage system for persistent time-series data. This is the **recommended way** to track historical metrics in your widgets.

**Benefits over manual cache files:**
- ✅ Persistent storage (survives cache clears and reboots)
- ✅ Automatic cleanup (configurable retention policy, default 30 days)
- ✅ Flexible time ranges (sample-based or time-based queries)
- ✅ Rich aggregations (avg, max, min, sum, count)
- ✅ Multiple visualization options
- ✅ No manual file management needed
- ✅ Centralized database for all widgets

**How to use it:**

1. **Store data** using `datastore:` directives (InfluxDB line protocol):

```bash
# Simple measurement
echo "datastore: cpu_load value=$load_percent"

# Multiple fields
echo "datastore: system_stats load=$load,temp=$temp,uptime=$uptime"

# With tags for grouping
echo "datastore: disk_usage,mount=$mount value=$pct"
echo "datastore: network_traffic,interface=$iface rx_kb=$rx,tx_kb=$tx"
echo "datastore: docker_stats,container=$name cpu_pct=$cpu,mem_pct=$mem"
```

2. **Visualize historical data** using storage-based inline elements:

```bash
# Dashboard mode: Show last N samples
echo "row: [history_graph:cpu_load.value:last_30:cool:Load %:0-100:40]"
echo "row: [history_sparkline:network_traffic.rx_kb:last_10:green:15]"

# Extended mode: Show time-based ranges
echo "row: [history_graph:cpu_load.value:1h:warm:CPU (1h):0-100:60]"
echo "row: [history_line:memory_usage.value:24h:spectrum:Memory (24h):0-100:80:12:braille]"

# With tag filtering
echo "row: [history_graph:disk_usage.value,mount=/:last_30:warm:Root Usage:0-100:40]"
echo "row: [history_graph:docker_stats.cpu_pct,container=web:last_30:spectrum:Web CPU:0-100:40]"
```

**Time ranges:**
- Sample-based: `last_10`, `last_30`, `last_100`, etc.
- Time-based: `10s`, `30s`, `1m`, `5m`, `15m`, `1h`, `6h`, `12h`, `24h`, `7d`, `30d`

**Storage Configuration:**

Storage is enabled by default in `~/.config/serverhub/config.yaml`:

```yaml
storage:
  enabled: true
  database_path: ~/.config/serverhub/serverhub.db
  retention_days: 30
  cleanup_interval_hours: 1
  max_database_size_mb: 500
  auto_vacuum: true
```

**Migration from cache files:**

If you have existing widgets using cache files (e.g., `~/.cache/serverhub/widget-data.txt`), migrate to the storage system:

```bash
# OLD: Manual cache file approach (not recommended)
CACHE_DIR="$HOME/.cache/serverhub"
echo "$value" >> "$CACHE_DIR/widget-data.txt"
tail -n 30 "$CACHE_DIR/widget-data.txt" > "$CACHE_DIR/widget-data.tmp"
mv "$CACHE_DIR/widget-data.tmp" "$CACHE_DIR/widget-data.txt"
history=$(paste -sd',' "$CACHE_DIR/widget-data.txt")
echo "row: [graph:${history}:cyan:History:0-100]"

# NEW: Storage system (recommended)
echo "datastore: widget_metric value=$value"
echo "row: [history_graph:widget_metric.value:last_30:cyan:History:0-100:40]"
```

**See also:** All bundled widgets (cpu, memory, disk, network, docker) use the storage system as reference examples.

### Tables

Create structured data displays:

```bash
echo "row: table:Header1|Header2|Header3"
echo "row: table:Value1|Value2|Value3"
echo "row: table:Data1|Data2|Data3"
```

### Actions

Add interactive actions to your widget:

```bash
echo "action: Restart Service:sudo systemctl restart myservice"
echo "action: Clear Cache:rm -rf /tmp/cache/*"
echo "action: Refresh:bash /path/to/widget.sh"
```

### Dashboard vs Extended Mode

Advanced templates detect extended mode automatically:

```bash
EXTENDED_MODE=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED_MODE=true
fi

if [[ "$EXTENDED_MODE" == "false" ]]; then
    # Show compact view
    echo "row: sparkline:${HISTORY[*]}"
else
    # Show detailed view
    echo "row: graph:${HISTORY[*]}"
    echo "row: table:Metric|Value"
fi
```

## Testing Widgets

ServerHub provides comprehensive testing to validate widget scripts before deployment.

### Quick Test

```bash
serverhub test-widget mywidget.sh
```

The command will:
1. Show a security warning
2. Ask for confirmation to execute
3. Run the widget script
4. Display detailed analysis and validation results

### Test Options

| Option | Description |
|--------|-------------|
| `--extended` | Pass `--extended` flag to the widget (tests expanded view mode) |
| `--yes`, `-y` | Skip confirmation prompt (useful for automation/CI) |
| `--ui` | Launch UI preview mode (interactive rendering) |

### Test Examples

```bash
# Interactive test with confirmation
serverhub test-widget mywidget.sh

# Test extended mode
serverhub test-widget mywidget.sh --extended

# Skip confirmation (automation/CI)
serverhub test-widget mywidget.sh --yes

# Test both modes in sequence
serverhub test-widget mywidget.sh --yes && \
serverhub test-widget mywidget.sh --extended --yes

# UI preview mode (interactive)
serverhub test-widget mywidget.sh --ui
```

### Test Output

The test command provides comprehensive analysis:

#### 1. Execution Status
- Exit code validation
- Execution time measurement
- Error detection

```
Execution Status:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✓ Widget executed successfully
✓ Execution time: 28ms
```

#### 2. Parsed Output
- Title and refresh interval extraction
- Row parsing with status indicators
- Detection of protocol elements (progress bars, sparklines, graphs, tables)

```
Parsed Output:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  Title: System Monitor
  Refresh: 5 seconds

  Rows (4):
    OK System is operational
    INFO CPU usage normal

      PROGRESS: 75%
      SPARKLINE: 10 values
```

#### 3. Actions
- Action syntax validation
- Label and command presence checks
- Flag detection (danger, refresh, sudo, timeout)

```
Actions (2):
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  ✓ Restart Service
    Command: systemctl restart myservice
  ✓ Force Restart (danger, sudo)
    Command: systemctl restart --force myservice
```

#### 4. Protocol Validation
- Markup syntax errors
- Invalid status values
- Malformed protocol elements

```
Protocol Validation:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  ✓ No protocol errors
  ✓ All markup valid
```

#### 5. Warnings
- Missing health checks
- No interactive actions
- Insufficient data rows

```
Warnings:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  ⚠ No error status indicator found (consider adding health checks)
  ⚠ No actions defined (consider adding interactive actions)
```

#### 6. Suggestions
- Recommended protocol features
- Best practices
- Enhancement opportunities

```
Suggestions:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  → Consider adding sparklines or graphs for trends
  → Consider using progress bars for percentage values
  → Consider using tables for structured data
```

### Exit Codes

- `0` - Widget passed all tests
- `1` - Widget failed (execution error, protocol errors, or action validation errors)

### Security Considerations

The test command executes scripts with **minimal security restrictions** to allow testing from any location (including `/tmp` or development directories).

**Before testing:**
- Only test widgets from trusted sources
- Review the widget code before testing
- Understand what the script will do

Use `--yes` to skip the confirmation prompt in trusted environments (CI/CD).

## Deploying Widgets

After creating and testing your widget, deploy it to your dashboard.

### Step 1: Place Widget in Custom Directory

```bash
# Copy to custom widgets directory
cp my-widget.sh ~/.config/serverhub/widgets/

# Or create widgets in place
cd ~/.config/serverhub/widgets/
serverhub new-widget bash-basic --name my-widget
```

### Step 2: Discover and Add to Config

```bash
serverhub --discover
```

This will:
- Find your new widget
- Calculate its SHA256 checksum
- Add it to your config file with proper security
- Prompt for confirmation

### Step 3: Launch Dashboard

```bash
serverhub
```

Your new widget appears in the dashboard!

### Manual Configuration

Alternatively, manually add to `~/.config/serverhub/config.yaml`:

```yaml
widgets:
  my-widget:
    path: my-widget.sh
    location: custom
    sha256: a1b2c3d4e5f6...  # Required!
    refresh: 10
    timeout: 30
```

Calculate checksum:
```bash
sha256sum ~/.config/serverhub/widgets/my-widget.sh
```

## CI/CD Integration

### Exit Code Testing

The test command returns proper exit codes for integration with CI/CD pipelines:

```bash
#!/bin/bash
# Pre-deployment widget validation

for widget in widgets/*.sh; do
    echo "Testing $widget..."
    if ! serverhub test-widget "$widget" --yes; then
        echo "❌ Widget test failed: $widget"
        exit 1
    fi
done

echo "✓ All widgets passed validation"
```

### GitHub Actions Example

```yaml
name: Widget Testing

on: [push, pull_request]

jobs:
  test-widgets:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - name: Install ServerHub
        run: |
          curl -fsSL https://raw.githubusercontent.com/nickprotop/ServerHub/main/install.sh | bash

      - name: Test Widgets
        run: |
          for widget in widgets/*.sh; do
            serverhub test-widget "$widget" --yes || exit 1
          done
```

### Pre-commit Hook Example

Create `.git/hooks/pre-commit`:

```bash
#!/bin/bash
# Test modified widget files before commit

WIDGETS=$(git diff --cached --name-only --diff-filter=ACM | grep '\.sh$' | grep 'widgets/')

if [ -n "$WIDGETS" ]; then
    echo "Testing modified widgets..."
    for widget in $WIDGETS; do
        if [ -f "$widget" ]; then
            echo "  Testing $widget..."
            if ! serverhub test-widget "$widget" --yes; then
                echo "❌ Widget test failed: $widget"
                echo "Fix the widget or use 'git commit --no-verify' to bypass"
                exit 1
            fi
        fi
    done
    echo "✓ All widgets passed validation"
fi
```

## Protocol Reference

For complete details on the widget protocol, see [WIDGET_PROTOCOL.md](WIDGET_PROTOCOL.md).

### Quick Protocol Summary

**Widget output structure:**
```bash
echo "title: Widget Title"
echo "refresh: 5"
echo "row: Text content"
echo "row: [status:ok] Status with indicator"
echo "row: [progress:75]"
echo "row: sparkline:10 20 30 40"
echo "row: table:Col1|Col2"
echo "action: Label:command"
```

**Status indicators:**
- `[status:ok]` - Green checkmark
- `[status:warning]` - Yellow warning
- `[status:error]` - Red error

**Visualizations:**
- `[progress:N]` or `[progress:N:inline]` - Progress bars
- `sparkline:values` - Compact trend line
- `graph:values` - Full ASCII graph (extended mode)
- `table:col1|col2|col3` - Table data

**Actions:**
```bash
echo "action: Label:command"
echo "action: Danger Action:command:danger"
echo "action: With Sudo:command:sudo"
echo "action: With Timeout:command:timeout=60"
```

## Development Workflow

Recommended workflow for widget development:

```bash
# 1. Create from template
serverhub new-widget bash-basic --name my-widget

# 2. Make executable
chmod +x my-widget.sh

# 3. Test initial template
serverhub test-widget my-widget.sh --yes

# 4. Edit and customize
nano my-widget.sh

# 5. Test changes
serverhub test-widget my-widget.sh --yes

# 6. Test extended mode (if applicable)
serverhub test-widget my-widget.sh --extended --yes

# 7. Deploy to dashboard
mv my-widget.sh ~/.config/serverhub/widgets/
serverhub --discover

# 8. Launch and verify
serverhub
```

## Troubleshooting

### Template fetch fails

```
Error: Failed to fetch template index from GitHub
```

**Solution:** Check your internet connection. Templates are fetched from GitHub on demand.

### Widget doesn't execute

After creating a widget, if it doesn't run:

1. Make sure it's executable: `chmod +x widget.sh`
2. Test it directly: `./widget.sh`
3. Check syntax errors in your customizations
4. Use `serverhub test-widget widget.sh` to validate

### Variables not substituted

If you see `{{VARIABLE}}` in your widget output:

1. Check the template was created correctly
2. Verify you provided values for required variables
3. The template system runs at creation time, not runtime

### Test failures

If your widget fails tests:

1. Review the Protocol Validation section of the test output
2. Check for malformed action syntax (missing labels, commands)
3. Verify all protocol elements are correctly formatted
4. Look at Suggestions section for improvement ideas

## Examples

### Example 1: Simple CPU Monitor

```bash
#!/bin/bash
echo "title: CPU Monitor"
echo "refresh: 5"

CPU_USAGE=$(top -bn1 | grep "Cpu(s)" | awk '{print $2}' | cut -d'%' -f1 | cut -d'.' -f1)

if (( CPU_USAGE > 80 )); then
    echo "row: [status:error] CPU: ${CPU_USAGE}%"
elif (( CPU_USAGE > 50 )); then
    echo "row: [status:warning] CPU: ${CPU_USAGE}%"
else
    echo "row: [status:ok] CPU: ${CPU_USAGE}%"
fi

echo "row: [progress:${CPU_USAGE}]"
echo "action: View Top:top -b -n 1 | head -20"
```

### Example 2: Service Monitor with Actions

```bash
#!/bin/bash
echo "title: Service Monitor"
echo "refresh: 10"

SERVICE="nginx"

if systemctl is-active --quiet "$SERVICE"; then
    echo "row: [status:ok] $SERVICE is running"
    echo "action: Stop Service:sudo systemctl stop $SERVICE:danger,sudo"
    echo "action: Restart:sudo systemctl restart $SERVICE:sudo"
else
    echo "row: [status:error] $SERVICE is stopped"
    echo "action: Start Service:sudo systemctl start $SERVICE:sudo"
fi

echo "action: View Logs:journalctl -u $SERVICE -n 50"
```

### Example 3: Python API Monitor

```python
#!/usr/bin/env python3
import requests

print("title: API Monitor")
print("refresh: 30")

try:
    response = requests.get("https://api.example.com/health", timeout=5)
    if response.status_code == 200:
        print("row: [status:ok] API is healthy")
        print(f"row: Response time: {response.elapsed.total_seconds():.2f}s")
    else:
        print(f"row: [status:error] API returned {response.status_code}")
except Exception as e:
    print(f"row: [status:error] API unreachable: {str(e)}")

print("action: Test API:curl -v https://api.example.com/health")
```

## See Also

- [Widget Protocol Documentation](WIDGET_PROTOCOL.md) - Complete protocol reference
- [Custom Widgets Guide](../README.md#custom-widgets) - Overview
- [Marketplace Documentation](MARKETPLACE.md) - Publishing widgets to marketplace
- [Examples](EXAMPLES.md) - Real-world widget examples
