# ServerHub

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Linux-orange.svg)]()

A terminal-based server monitoring dashboard with responsive multi-column layout.

![ServerHub Screenshot](.github/Screenshot.png)

## Features

- Responsive 1-4 column layout that adapts to terminal width
- 14 bundled widgets (CPU, memory, disk, Docker, services, and more)
- Custom widget support with SHA256 security validation
- YAML-based configuration
- Keyboard navigation between widgets (Tab/Shift+Tab)
- Pause/resume widget refresh
- Visual status indicators and progress bars

## Requirements

- .NET 9.0 Runtime
- Linux
- Bash (for widget scripts)

## Installation

```bash
git clone https://github.com/nickprotop/ServerHub.git
cd ServerHub
./install.sh
```

Add `~/.local/bin` to your PATH if not already present:

```bash
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.bashrc
source ~/.bashrc
```

## Usage

```bash
serverhub                      # Run with default config
serverhub myconfig.yaml        # Use custom config file
serverhub --discover           # Find and add custom widgets
serverhub --verify-checksums   # Verify all widget checksums
serverhub --dev-mode           # Development mode (see Security section)
serverhub --help               # Show all options
```

### Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `Tab` / `Shift+Tab` | Navigate between widgets |
| `Arrow keys` | Scroll within focused widget |
| `F5` | Refresh all widgets |
| `Space` | Pause/resume refresh |
| `?` or `F1` | Show help |
| `Ctrl+Q` | Quit |

## Configuration

Configuration file location: `~/.config/serverhub/config.yaml`

```yaml
default_refresh: 5

widgets:
  cpu:
    path: cpu.sh
    refresh: 2
    priority: 1

  memory:
    path: memory.sh
    refresh: 2
    priority: 1

layout:
  order:
    - cpu
    - memory

breakpoints:
  single: 0      # 1 column
  double: 100    # 2 columns at 100+ chars
  triple: 160    # 3 columns at 160+ chars
  quad: 220      # 4 columns at 220+ chars
```

See [config.example.yaml](config.example.yaml) for full configuration options.

## Bundled Widgets

| Widget | Description |
|--------|-------------|
| `cpu` | CPU usage and load average |
| `memory` | Memory and swap usage |
| `disk` | Disk space usage |
| `network` | Network interface statistics |
| `processes` | Top processes by CPU/memory |
| `sysinfo` | System information (hostname, uptime, kernel) |
| `docker` | Docker container status |
| `services` | Systemd service status |
| `updates` | Available package updates |
| `alerts` | System health alerts |
| `sensors` | Hardware temperature sensors |
| `netstat` | Network connections |
| `logs` | Recent system log entries |
| `ssl-certs` | SSL certificate expiry status |

## Custom Widgets

Place custom widget scripts in `~/.config/serverhub/widgets/`.

**All custom widgets require SHA256 checksum validation** (see Security section below).

```bash
# Discover new widgets interactively (recommended)
serverhub --discover

# Verify checksums for all configured widgets
serverhub --verify-checksums
```

### Widget Protocol (Brief)

Widgets output structured text to stdout:

```bash
echo "title: My Widget"
echo "row: [status:ok] Everything is fine"
echo "row: [progress:75:inline]"
echo "row: [grey70]Last updated: $(date)[/]"
```

See [docs/WIDGET_PROTOCOL.md](docs/WIDGET_PROTOCOL.md) for the full protocol reference.

## Security

### Why This Matters

Widgets are **executable scripts that run with your user privileges**. A malicious widget could read your files, make network requests, or do anything else you can do. We'd rather be annoying about checksums than watch your server have a very bad day.

### Trust Hierarchy

| Source | Trust Level | Checksum Source |
|--------|-------------|-----------------|
| **Bundled widgets** | Highest | Hardcoded at build time (maintainer-reviewed) |
| **Custom widgets** | User-verified | You add `sha256` to config after reviewing code |

### How It Works

1. **Bundled widgets** (`~/.local/share/serverhub/widgets/`) are pre-validated with checksums baked into the application at build time. They just work.

2. **Custom widgets** (`~/.config/serverhub/widgets/`) **require** a `sha256` checksum in your config:

```yaml
widgets:
  my-widget:
    path: my-widget.sh
    sha256: a1b2c3d4e5f6...  # Required!
    refresh: 10
```

Without a checksum, custom widgets will not run. This is intentional.

### Adding Custom Widgets Safely

**Option 1: Discovery (Recommended)**
```bash
serverhub --discover
```
This shows you a code preview of each widget before adding it. When you approve, the checksum is captured at that moment - the "trusted moment" when you've actually seen what the code does.

**Option 2: Manual**
```bash
# 1. Read the script yourself
cat ~/.config/serverhub/widgets/my-widget.sh

# 2. Calculate the checksum
sha256sum ~/.config/serverhub/widgets/my-widget.sh

# 3. Add to config with the checksum
```

### Why We Don't Auto-Generate Checksums

When a widget fails validation, ServerHub does **not** helpfully show you "just add this checksum." That would defeat the entire security model:

1. Attacker modifies a widget file
2. You run ServerHub, it fails with "missing checksum"
3. If it showed the checksum, you'd copy-paste it without thinking
4. Congratulations, you've just blessed malicious code

Instead, you must go through a "trusted moment" - either `--discover` (which shows the code) or manually running `sha256sum` (which requires conscious action).

### Development Mode

For **development only**, you can skip custom widget checksum validation:

```bash
serverhub --dev-mode --widgets-path ./my-dev-widgets
```

Dev mode shows prominent warnings (status bar, orange border, startup dialog) because:
- Bundled widgets are **still validated** even in dev mode
- This is for development, not for "I don't want to deal with checksums"
- **Never use `--dev-mode` in production**

### Verification

```bash
# Check all your widgets
serverhub --verify-checksums

# Output shows VALID / MISMATCH / NO CHECKSUM for each
```

## Built With

- [SharpConsoleUI](https://github.com/nickprotop/ConsoleEx) - A .NET library for building terminal user interfaces with responsive layouts and window management.

## Author

**Nikolaos Protopapas**

- GitHub: [@nickprotop](https://github.com/nickprotop)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
