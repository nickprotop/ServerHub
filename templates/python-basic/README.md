# Basic Python Widget Template

Simple Python script template for creating ServerHub widgets.

## Features

- Basic widget protocol implementation in Python
- Status indicators
- Configurable refresh interval
- Action buttons
- Easy to extend with Python libraries

## Usage

```bash
# Interactive mode
serverhub new-widget

# Direct creation
serverhub new-widget python-basic --name my-widget --refresh 10

# With output path
serverhub new-widget python-basic --name my-widget --output ~/widgets/my-widget.py
```

## Template Variables

- **WIDGET_NAME** (required): Widget identifier (lowercase, hyphens, no spaces)
- **WIDGET_TITLE** (optional): Display title shown in dashboard
- **REFRESH_INTERVAL** (optional): Refresh interval in seconds (default: 5)
- **AUTHOR** (optional): Widget author name
- **DESCRIPTION** (optional): Widget description

## Requirements

Python 3.6+ (no additional packages required for basic template)

## Next Steps

After creating your widget:

1. Make it executable: `chmod +x your-widget.py`
2. Test it: `serverhub test-widget your-widget.py`
3. Add to config: `serverhub --discover`
4. Customize the monitoring logic in the TODO sections

## Example Customization

Replace the TODO sections with your actual monitoring code:

```python
import psutil

def main():
    print(f"title: System Resources")
    print(f"refresh: 5")

    # CPU usage
    cpu_percent = psutil.cpu_percent(interval=1)
    if cpu_percent > 80:
        print(f"row: [status:error] CPU: {cpu_percent}%")
    else:
        print(f"row: [status:ok] CPU: {cpu_percent}%")

    # Memory usage
    mem = psutil.virtual_memory()
    print(f"row: Memory: {mem.percent}%")

    # Actions
    print("action: Refresh:python3 /path/to/widget.py")

if __name__ == "__main__":
    main()
```

## Using External Libraries

If you need external libraries, create a `requirements.txt` file:

```
psutil>=5.9.0
requests>=2.28.0
```

Install dependencies:
```bash
pip install -r requirements.txt
```

## Protocol Reference

See the [Widget Protocol Documentation](https://github.com/nickprotop/ServerHub#widget-protocol) for more details.
