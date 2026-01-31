# Advanced Bash Widget Template

Advanced shell script template with sparklines, graphs, tables, and history tracking.

## Features

- Dashboard vs Extended mode support
- History tracking with configurable cache
- Sparklines for trend visualization
- Graphs in extended mode
- Statistical tables
- Dynamic actions
- Status indicators based on thresholds

## Usage

```bash
# Interactive mode
serverhub new-widget

# Direct creation
serverhub new-widget bash-advanced --name system-monitor --max-samples 50

# With custom cache location
serverhub new-widget bash-advanced --name cpu-tracker --cache-file /tmp/cpu-history.txt
```

## Template Variables

- **WIDGET_NAME** (required): Widget identifier
- **WIDGET_TITLE** (optional): Display title
- **REFRESH_INTERVAL** (optional): Refresh interval in seconds (default: 5)
- **AUTHOR** (optional): Widget author name
- **DESCRIPTION** (optional): Widget description
- **CACHE_FILE** (optional): Path to history cache file
- **MAX_SAMPLES** (optional): Maximum history samples to keep (default: 30)

## Dashboard vs Extended Mode

The widget automatically detects if it's running in extended mode (when the widget is expanded):

- **Dashboard mode**: Compact view with sparkline and key metrics
- **Extended mode**: Detailed view with graphs, tables, and statistics

## Customization

Replace the TODO section with your actual metric collection:

```bash
# Example: CPU usage
CURRENT_VALUE=$(top -bn1 | grep "Cpu(s)" | awk '{print $2}' | cut -d'%' -f1 | cut -d'.' -f1)

# Example: Memory usage percentage
CURRENT_VALUE=$(free | grep Mem | awk '{print int($3/$2 * 100)}')

# Example: Disk usage
CURRENT_VALUE=$(df / | tail -1 | awk '{print int($5)}')
```

## Visualization Elements

- `sparkline:value1 value2 value3` - Compact trend line
- `graph:value1 value2 value3` - Full ASCII graph (extended mode)
- `table:header1|header2` - Table headers
- `table:value1|value2` - Table rows

## Example Output

Dashboard mode:
```
Current: 45%
▁▂▃▄▅▆▇█▇▆▅▄▃▂▁
Average: 47%
```

Extended mode:
```
Current Status
✓ Value: 45%

History Graph
█████████▇▇▆▆▅▅▄▄▃▃▂▂▁▁

Statistics
Metric    | Value
Average   | 47%
Minimum   | 12%
Maximum   | 89%
Samples   | 30
```

## Protocol Reference

See the [Widget Protocol Documentation](https://github.com/nickprotop/ServerHub#widget-protocol) for more details.
