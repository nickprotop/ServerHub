# Debug Logging Guide

ServerHub uses SharpConsoleUI's file-based logging system for debugging without interfering with the TUI display.

## Quick Start

Enable debug logging by setting environment variables before running ServerHub:

```bash
# Set log file path and level
export SHARPCONSOLEUI_DEBUG_LOG=/tmp/serverhub-debug.log
export SHARPCONSOLEUI_DEBUG_LEVEL=Debug

# Run ServerHub
dotnet run --config config-storage-test.yaml --widgets-path ./widgets

# In another terminal, watch the log file
tail -f /tmp/serverhub-debug.log
```

## Log Levels

Set `SHARPCONSOLEUI_DEBUG_LEVEL` to one of:

- **Trace** - Most verbose (all messages)
- **Debug** - Debug messages and above (recommended for development)
- **Information** - Info messages and above
- **Warning** - Warnings and errors only (default)
- **Error** - Errors and critical only
- **Critical** - Critical errors only

## Log Categories

ServerHub uses these categories for organized logging:

- **Storage** - Database operations, datastore directive persistence
- **WidgetRefresh** - Widget execution and parsing
- **WidgetProtocol** - Protocol parsing warnings
- **General** - Other application logs

## Example: Debugging Storage Issues

```bash
# Enable debug logging
export SHARPCONSOLEUI_DEBUG_LOG=/tmp/serverhub-debug.log
export SHARPCONSOLEUI_DEBUG_LEVEL=Debug

# Run ServerHub
dotnet run --config config-storage-test.yaml --widgets-path ./widgets

# Watch storage logs in real-time
tail -f /tmp/serverhub-debug.log | grep "\[Storage\]"
```

### Expected Storage Debug Output

```
[2026-02-08 00:35:34.123] [DEBUG] [Storage     ] Widget 'storage-test' has 4 datastore directives to persist
[2026-02-08 00:35:34.124] [DEBUG] [Storage     ] Processing directive: measurement='test_metric', tags=0, fields=1
[2026-02-08 00:35:34.125] [DEBUG] [Storage     ] Processing directive: measurement='test_metric', tags=1, fields=2
[2026-02-08 00:35:34.126] [DEBUG] [Storage     ] Processing directive: measurement='system_load', tags=1, fields=1
[2026-02-08 00:35:34.127] [DEBUG] [Storage     ] Processing directive: measurement='test_counter', tags=0, fields=2
```

## Log File Location

Recommended locations:

- **Development**: `/tmp/serverhub-debug.log` (easy cleanup)
- **Production**: `~/.config/serverhub/serverhub.log` (persistent)
- **Testing**: `./serverhub-test.log` (project-local)

## Log Format

```
[YYYY-MM-DD HH:mm:ss.fff] [LEVEL] [Category   ] Message
[2026-02-08 00:35:34.123] [DEBUG] [Storage    ] Widget 'cpu' has 3 datastore directives
```

## Disabling Logging

Simply unset the environment variable:

```bash
unset SHARPCONSOLEUI_DEBUG_LOG
```

## Tips

1. **Use grep for focused debugging:**
   ```bash
   tail -f /tmp/serverhub-debug.log | grep -E "(ERROR|Storage)"
   ```

2. **Rotate logs automatically:**
   ```bash
   # Add to your .bashrc or startup script
   export SHARPCONSOLEUI_DEBUG_LOG=~/logs/serverhub-$(date +%Y%m%d).log
   ```

3. **Filter by category:**
   ```bash
   tail -f /tmp/serverhub-debug.log | grep "\[Storage\]"
   tail -f /tmp/serverhub-debug.log | grep "\[WidgetRefresh\]"
   ```

4. **Capture full session:**
   ```bash
   export SHARPCONSOLEUI_DEBUG_LOG=./session-$(date +%Y%m%d-%H%M%S).log
   export SHARPCONSOLEUI_DEBUG_LEVEL=Debug
   dotnet run
   # Log file will contain complete debug trace of the session
   ```

## Performance Impact

- **Trace/Debug levels**: Minor impact (~1-2% overhead)
- **Warning/Error levels**: Negligible impact
- **File I/O**: Async writes, minimal blocking
- **Buffer size**: 1000 entries (configurable in LogService)

## Troubleshooting

**No log file created:**
- Check directory exists and is writable
- Verify environment variable is set: `echo $SHARPCONSOLEUI_DEBUG_LOG`
- Check for permission errors in console output

**Log file empty:**
- Verify log level is set correctly (Debug or lower)
- Check that logging is generating messages at your level
- Ensure ServerHub actually executed (check for startup messages)

**TUI display corrupted:**
- Never use `Console.WriteLine` in TUI code
- Always use `Logger.Debug/Info/Warning/Error` instead
- File logging won't corrupt TUI display
