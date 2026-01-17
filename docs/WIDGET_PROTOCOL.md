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

### action: \<Label\>:\<script\> [arguments]

Defines an interactive action (future feature).

```
action: Restart Service:/usr/local/bin/restart.sh nginx
```

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
# my-widget.sh - Example custom widget

echo "title: My Custom Widget"
echo "refresh: 10"

# Status row with indicator
if systemctl is-active --quiet nginx; then
    echo "row: [status:ok] Nginx is running"
else
    echo "row: [status:error] Nginx is stopped"
fi

# Progress bar
cpu_usage=$(top -bn1 | grep "Cpu(s)" | awk '{print int($2)}')
echo "row: CPU: ${cpu_usage}%"
echo "row: [progress:${cpu_usage}:inline]"

# Styled text
echo "row: [grey70]Last checked: $(date '+%H:%M:%S')[/]"
```

## Security

### Custom Widgets

Custom widgets (in `~/.config/serverhub/widgets/`) require SHA256 checksum validation:

```yaml
widgets:
  my-widget:
    path: my-widget.sh
    sha256: a1b2c3d4e5f6...  # Required for custom widgets
    refresh: 10
```

### Generating Checksums

```bash
# Discover new widgets and generate config snippets
serverhub --discover

# Show checksums for all configured widgets
serverhub --compute-checksums
```

### Bundled Widgets

Bundled widgets (in `~/.local/share/serverhub/widgets/`) are pre-validated at build time and don't require checksums in your config.

## Output Requirements

- Scripts must be executable (`chmod +x`)
- Output to stdout only
- One protocol element per line
- Exit code 0 for success (non-zero shows error state)
- Keep execution time short (< refresh interval)
