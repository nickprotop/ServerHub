# Basic Bash Widget Template

Simple shell script template for creating ServerHub widgets with status indicators and actions.

## Features

- Basic widget protocol implementation
- Status indicators
- Configurable refresh interval
- Action buttons
- Easy to customize

## Usage

```bash
# Interactive mode
serverhub new-widget

# Direct creation
serverhub new-widget bash-basic --name my-widget --refresh 10

# With output path
serverhub new-widget bash-basic --name my-widget --output ~/widgets/my-widget.sh
```

## Template Variables

- **WIDGET_NAME** (required): Widget identifier (lowercase, hyphens, no spaces)
- **WIDGET_TITLE** (optional): Display title shown in dashboard
- **REFRESH_INTERVAL** (optional): Refresh interval in seconds (default: 5)
- **AUTHOR** (optional): Widget author name
- **DESCRIPTION** (optional): Widget description

## Next Steps

After creating your widget:

1. Make it executable: `chmod +x your-widget.sh`
2. Test it: `serverhub test-widget your-widget.sh`
3. Add to config: `serverhub --discover`
4. Customize the monitoring logic in the TODO sections

## Example Customization

Replace the TODO sections with your actual monitoring code:

```bash
# Example: CPU usage monitoring
CPU_USAGE=$(top -bn1 | grep "Cpu(s)" | awk '{print $2}' | cut -d'%' -f1)

if (( $(echo "$CPU_USAGE > 80" | bc -l) )); then
    echo "row: [status:error] CPU: ${CPU_USAGE}%"
else
    echo "row: [status:ok] CPU: ${CPU_USAGE}%"
fi
```

## Protocol Reference

See the [Widget Protocol Documentation](https://github.com/nickprotop/ServerHub#widget-protocol) for more details.
