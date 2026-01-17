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
serverhub --compute-checksums  # Show checksums for configured widgets
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

Custom widgets require SHA256 checksum validation for security:

```bash
# Discover new widgets and get their checksums
serverhub --discover

# Or compute checksums for existing config
serverhub --compute-checksums
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

## Built With

- [SharpConsoleUI](https://github.com/nickprotop/ConsoleEx) - A .NET library for building terminal user interfaces with responsive layouts and window management.

## Author

**Nikolaos Protopapas**

- GitHub: [@nickprotop](https://github.com/nickprotop)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
