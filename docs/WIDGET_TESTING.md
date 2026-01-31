# Widget Testing

ServerHub provides a comprehensive testing framework for validating widget scripts before deployment.

## Table of Contents

- [Overview](#overview)
- [Quick Start](#quick-start)
- [Command Reference](#command-reference)
- [Output Details](#output-details)
- [Security Considerations](#security-considerations)
- [CI/CD Integration](#cicd-integration)
- [Examples](#examples)

## Overview

The `test-widget` command validates widget scripts against the ServerHub protocol, ensuring they:
- Execute successfully without errors
- Output valid protocol syntax (title, refresh, rows, actions)
- Use proper action syntax and flags
- Follow protocol best practices

This helps catch issues before adding widgets to your dashboard.

## Quick Start

Test a widget script:

```bash
serverhub test-widget mywidget.sh
```

The command will:
1. Show a security warning
2. Ask for confirmation to execute
3. Run the widget script
4. Display detailed analysis and validation results

## Command Reference

### Basic Usage

```bash
serverhub test-widget <widget-script> [options]
```

### Options

| Option | Description |
|--------|-------------|
| `--extended` | Pass `--extended` flag to the widget (tests expanded view mode) |
| `--yes`, `-y` | Skip confirmation prompt (useful for automation/CI) |
| `--ui` | Launch UI preview mode (not yet implemented) |

### Examples

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
```

## Output Details

The test command provides comprehensive analysis:

### 1. Execution Status
- Exit code validation
- Execution time measurement
- Error detection

```
Execution Status:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✓ Widget executed successfully
✓ Execution time: 28ms
```

### 2. Parsed Output
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

### 3. Actions
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

### 4. Protocol Validation
- Markup syntax errors
- Invalid status values
- Malformed protocol elements

```
Protocol Validation:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  ✓ No protocol errors
  ✓ All markup valid
```

### 5. Warnings
- Missing health checks
- No interactive actions
- Insufficient data rows

```
Warnings:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  ⚠ No error status indicator found (consider adding health checks)
  ⚠ No actions defined (consider adding interactive actions)
```

### 6. Suggestions
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

### 7. Exit Code

- `0` - Widget passed all tests
- `1` - Widget failed (execution error, protocol errors, or action validation errors)

## Security Considerations

### Security Warning

The test command executes scripts with **minimal security restrictions** to allow testing from any location (including `/tmp` or development directories).

**Before testing:**
- Only test widgets from trusted sources
- Review the widget code before testing
- Understand what the script will do

### Confirmation Prompt

By default, the command shows a security warning and requires confirmation:

```
⚠  Security Notice
This will execute the widget script with minimal security restrictions.
Only test widgets from trusted sources.

Script path: /tmp/mywidget.sh

Execute this widget? [y/n] (n):
```

### Bypassing Confirmation

For automation or CI/CD, use the `--yes` flag to skip the prompt:

```bash
serverhub test-widget mywidget.sh --yes
```

**Warning:** Only use `--yes` in trusted environments where you control the widget source.

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

## Examples

### Example 1: Simple Monitoring Widget

**Widget:** `cpu-monitor.sh`
```bash
#!/bin/bash
echo "title: CPU Monitor"
echo "refresh: 5"
echo "row: [status:ok] CPU usage normal"
echo "row: [progress:45]"
echo "action: View Details:top -b -n 1"
```

**Test Output:**
```bash
$ serverhub test-widget cpu-monitor.sh --yes

Testing widget: cpu-monitor.sh
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✓ Widget file exists

Execution Status:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✓ Widget executed successfully
✓ Execution time: 15ms

Parsed Output:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  Title: CPU Monitor
  Refresh: 5 seconds

  Rows (2):
    OK CPU usage normal

      PROGRESS: 45%

Actions (1):
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  ✓ View Details
    Command: top -b -n 1

Protocol Validation:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  ✓ No protocol errors
  ✓ All markup valid

Warnings:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  ⚠ No error status indicator found (consider adding health checks)

Suggestions:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  → Consider adding sparklines or graphs for trends

✓ Widget test PASSED
```

### Example 2: Failed Widget Test

**Widget:** `broken-widget.sh`
```bash
#!/bin/bash
echo "title: Broken Widget"
echo "row: [status:invalid] Bad status"
echo "action: test-action.sh"  # Missing label
```

**Test Output:**
```bash
$ serverhub test-widget broken-widget.sh --yes

Testing widget: broken-widget.sh
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✓ Widget file exists

Execution Status:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✓ Widget executed successfully
✓ Execution time: 12ms

Parsed Output:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  Title: Broken Widget
  Refresh: 5 seconds

  Rows (1):
    Bad status

Actions (1):
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  ✗ (no label)

Protocol Validation:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  ✗ Action has no label

Warnings:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  ⚠ No error status indicator found (consider adding health checks)
  ⚠ Very few rows (consider adding more information)

Suggestions:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  → Consider adding sparklines or graphs for trends

✗ Widget test FAILED
```

### Example 3: Extended Mode Testing

Test both normal and extended modes:

```bash
# Test normal mode
serverhub test-widget service-monitor.sh --yes

# Test extended mode
serverhub test-widget service-monitor.sh --extended --yes
```

This ensures your widget properly handles the `--extended` flag for expanded views.

## Future Enhancements

Planned features for the widget testing framework:

- **UI Preview Mode** (`--ui` flag) - Launch TUI to see widget rendered in real-time
- **Watch Mode** - Auto-retest on file changes during development
- **Batch Testing** - Test multiple widgets in one command
- **JSON Output** - Machine-readable output for tooling integration
- **Benchmark Mode** - Performance testing and optimization suggestions
- **Mock Mode** - Test widgets without executing actual commands

## See Also

- [Widget Protocol Documentation](WIDGET_PROTOCOL.md) - Complete protocol reference
- [Custom Widgets Guide](../README.md#custom-widgets) - Creating custom widgets
- [Marketplace Documentation](MARKETPLACE.md) - Publishing widgets to marketplace
