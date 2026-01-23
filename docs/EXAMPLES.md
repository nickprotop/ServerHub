# ServerHub Examples

Real-world custom widget examples showing how to monitor and control your specific setup.

## Origin Story

ServerHub was originally built to solve a specific problem: monitoring and managing a development droplet running Docker containers, APIs, and deployment scripts. Instead of SSH-ing in constantly to check status and run commands, the goal was to have everything visible and actionable from one dashboard.

This also serves as a practical showcase of [SharpConsoleUI](https://github.com/nickprotop/ConsoleEx) - demonstrating how to build responsive, interactive terminal applications with .NET.

---

## Language-Agnostic Widgets

Widgets can be written in **any language**. ServerHub simply executes them and reads stdout - no SDK or library required.

**Extended View Support:** Widgets can detect when they're opened in expanded mode (Press Enter on any widget) and show additional detail - full logs, response times, system info, etc. Dashboard shows summaries; expanded view shows everything.

Here are examples of the same "API Health Check" widget in different languages with extended view support:

### C# Script

Modern C# with top-level statements. One file, runs like a script with full .NET power.

**Supports expanded view** - Press Enter on the widget to see full details with response times and headers.

**File:** `~/.config/serverhub/widgets/api-health.csx`

```csharp
#!/usr/bin/env dotnet script

using System;
using System.Net.Http;
using System.Diagnostics;
using System.Threading.Tasks;

// Check if running in extended mode
var extended = Args.Contains("--extended");

await RunAsync();

async Task RunAsync()
{
    Console.WriteLine("title: API Health");

    var endpoints = new[] {
        ("https://api.myapp.com/health", "Main API"),
        ("https://api-staging.myapp.com/health", "Staging API"),
        ("http://localhost:3000/health", "Local API")
    };

    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

    foreach (var (url, name) in endpoints) {
        var sw = Stopwatch.StartNew();
        try {
            var response = await client.GetAsync(url);
            sw.Stop();

            if (response.IsSuccessStatusCode) {
                Console.WriteLine($"row: [status:ok] {name} - Healthy");

                // Extended mode: show response time and details
                if (extended) {
                    Console.WriteLine($"row:   [grey70]Response time: {sw.ElapsedMilliseconds}ms[/]");
                    Console.WriteLine($"row:   [grey70]Status: {(int)response.StatusCode} {response.StatusCode}[/]");
                    Console.WriteLine($"row:   [grey70]Content-Type: {response.Content.Headers.ContentType}[/]");
                }
            } else {
                Console.WriteLine($"row: [status:warning] {name} - HTTP {(int)response.StatusCode}");
                if (extended) {
                    Console.WriteLine($"row:   [grey70]Response time: {sw.ElapsedMilliseconds}ms[/]");
                }
            }
        } catch (Exception ex) {
            Console.WriteLine($"row: [status:error] {name} - Unreachable");
            if (extended) {
                Console.WriteLine($"row:   [grey70]Error: {ex.Message}[/]");
            }
        }
    }

    Console.WriteLine("row: ");
    Console.WriteLine($"row: [grey70]Last check: {DateTime.Now:HH:mm:ss}[/]");

    // Extended mode: show additional system info
    if (extended) {
        Console.WriteLine("row: ");
        Console.WriteLine("row: [bold]System Info:[/]");
        Console.WriteLine($"row: [grey70].NET Version: {Environment.Version}[/]");
        Console.WriteLine($"row: [grey70]OS: {Environment.OSVersion}[/]");
    }

    // Actions (shown as buttons in expanded view)
    Console.WriteLine("action: [danger,sudo,refresh] Restart Main:systemctl restart myapp-api");
    Console.WriteLine("action: View Logs:journalctl -u myapp-api -n 50 --no-pager");
}
```

**Dashboard view (compact):**
- Summary status for each endpoint
- Color-coded indicators

**Extended view (Press Enter):**
- Full response times
- HTTP headers
- Error details
- System information
- Action buttons

**Setup:**
```bash
# Install dotnet-script globally (once)
dotnet tool install -g dotnet-script

# Make executable
chmod +x api-health.csx
```

**Why C#:** Full .NET ecosystem (HttpClient, JSON, async/await, LINQ), type safety, modern syntax, NuGet packages.

---

### Python

Great for APIs, JSON parsing, and data processing.

**File:** `~/.config/serverhub/widgets/api-health.py`

```python
#!/usr/bin/env python3

import requests
from datetime import datetime

print("title: API Health")

endpoints = [
    ("https://api.myapp.com/health", "Main API"),
    ("https://api-staging.myapp.com/health", "Staging API"),
    ("http://localhost:3000/health", "Local API")
]

for url, name in endpoints:
    try:
        response = requests.get(url, timeout=3)
        if response.status_code == 200:
            print(f"row: [status:ok] {name} - Healthy")
        else:
            print(f"row: [status:warning] {name} - HTTP {response.status_code}")
    except:
        print(f"row: [status:error] {name} - Unreachable")

print("row: ")
print(f"row: [grey70]Last check: {datetime.now().strftime('%H:%M:%S')}[/]")

# Actions
print("action: [danger,sudo,refresh] Restart Main:systemctl restart myapp-api")
print("action: View Logs:journalctl -u myapp-api -n 50 --no-pager")
```

**Setup:**
```bash
pip install requests
chmod +x api-health.py
```

**Why Python:** Excellent library ecosystem, readable syntax, great for data processing.

---

### Node.js

Perfect for async operations and npm packages.

**File:** `~/.config/serverhub/widgets/api-health.js`

```javascript
#!/usr/bin/env node

const https = require('https');
const http = require('http');

console.log("title: API Health");

const endpoints = [
    { url: "https://api.myapp.com/health", name: "Main API" },
    { url: "https://api-staging.myapp.com/health", name: "Staging API" },
    { url: "http://localhost:3000/health", name: "Local API" }
];

async function checkEndpoint(url, name) {
    return new Promise((resolve) => {
        const client = url.startsWith('https') ? https : http;
        const timeout = setTimeout(() => {
            resolve(`row: [status:error] ${name} - Timeout`);
        }, 3000);

        client.get(url, (res) => {
            clearTimeout(timeout);
            if (res.statusCode === 200) {
                resolve(`row: [status:ok] ${name} - Healthy`);
            } else {
                resolve(`row: [status:warning] ${name} - HTTP ${res.statusCode}`);
            }
        }).on('error', () => {
            clearTimeout(timeout);
            resolve(`row: [status:error] ${name} - Unreachable`);
        });
    });
}

(async () => {
    for (const { url, name } of endpoints) {
        console.log(await checkEndpoint(url, name));
    }

    console.log("row: ");
    console.log(`row: [grey70]Last check: ${new Date().toTimeString().slice(0, 8)}[/]`);

    console.log("action: [danger,sudo,refresh] Restart Main:systemctl restart myapp-api");
    console.log("action: View Logs:journalctl -u myapp-api -n 50 --no-pager");
})();
```

**Setup:**
```bash
chmod +x api-health.js
```

**Why Node.js:** Native async/await, npm ecosystem, familiar to web developers.

---

### Bash

Lightweight, zero dependencies, perfect for system commands.

**File:** `~/.config/serverhub/widgets/api-health.sh`

```bash
#!/bin/bash

echo "title: API Health"

check_api() {
    local url="$1"
    local name="$2"

    response=$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 3 "$url" 2>/dev/null)

    if [ "$response" = "200" ]; then
        echo "row: [status:ok] $name - Healthy"
    elif [ -z "$response" ]; then
        echo "row: [status:error] $name - Unreachable"
    else
        echo "row: [status:warning] $name - HTTP $response"
    fi
}

check_api "https://api.myapp.com/health" "Main API"
check_api "https://api-staging.myapp.com/health" "Staging API"
check_api "http://localhost:3000/health" "Local API"

echo "row: "
echo "row: [grey70]Last check: $(date '+%H:%M:%S')[/]"

echo "action: [danger,sudo,refresh] Restart Main:systemctl restart myapp-api"
echo "action: View Logs:journalctl -u myapp-api -n 50 --no-pager"
```

**Setup:**
```bash
chmod +x api-health.sh
```

**Why Bash:** Minimal overhead, ubiquitous, great for simple system commands.

---

### Key Takeaway

**Pick the right tool for the job:**
- C# - Full .NET ecosystem, modern syntax, type safety
- Python - Data processing, scientific computing, rich libraries
- Node.js - Async operations, web APIs, npm packages
- Bash - System commands, lightweight, zero setup

All work equally well with ServerHub - just output text following the protocol.

---

## Example 1: Development Droplet

**Scenario:** You have a cloud droplet running your development/staging environment with Docker containers, Node.js APIs, and deployment scripts.

### API Health Monitor Widget

Monitor multiple API endpoints with status indicators and quick restart actions.

**File:** `~/.config/serverhub/widgets/api-health.sh`

```bash
#!/bin/bash
# API Health Monitor - Checks multiple API endpoints

echo "title: API Health"

# Configuration
API_ENDPOINTS=(
    "https://api.myapp.com/health|Main API"
    "https://api-staging.myapp.com/health|Staging API"
    "http://localhost:3000/health|Local API"
)

check_api() {
    local url="$1"
    local name="$2"

    # Curl with timeout
    response=$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 3 "$url" 2>/dev/null)

    if [ "$response" = "200" ]; then
        echo "row: [status:ok] $name - Healthy"
    elif [ -z "$response" ]; then
        echo "row: [status:error] $name - Unreachable"
    else
        echo "row: [status:warning] $name - HTTP $response"
    fi
}

# Check all endpoints
for endpoint in "${API_ENDPOINTS[@]}"; do
    url="${endpoint%%|*}"
    name="${endpoint##*|}"
    check_api "$url" "$name"
done

echo "row: "
echo "row: [grey70]Last check: $(date '+%H:%M:%S')[/]"

# Export actions
echo "action: [danger,sudo,refresh] Restart Main API:systemctl restart myapp-api"
echo "action: [danger,sudo,refresh] Restart Staging:systemctl restart myapp-staging"
echo "action: View Logs:journalctl -u myapp-api -n 50 --no-pager"
```

**Config:**
```yaml
widgets:
  api-health:
    path: api-health.sh
    sha256: <checksum>
    refresh: 10
```

---

### Docker Services Widget

Monitor containers with per-container actions based on current state.

**File:** `~/.config/serverhub/widgets/docker-services.sh`

```bash
#!/bin/bash
# Docker Services - Monitor containers with context-aware actions

echo "title: Docker Services"

if ! command -v docker &> /dev/null; then
    echo "row: [yellow]Docker not installed[/]"
    exit 0
fi

# Get container status
containers=$(docker ps -a --format "{{.Names}}|{{.State}}|{{.Status}}" 2>/dev/null)

if [ -z "$containers" ]; then
    echo "row: [grey70]No containers found[/]"
    exit 0
fi

running=0
stopped=0

echo "$containers" | while IFS='|' read -r name state status; do
    if [ "$state" = "running" ]; then
        echo "row: [status:ok] $name - Running"
        echo "action: [danger,refresh] Restart $name:docker restart $name"
        echo "action: [danger,refresh] Stop $name:docker stop $name"
        echo "action: Logs $name:docker logs --tail 50 $name"
        ((running++))
    else
        echo "row: [status:error] $name - Stopped"
        echo "action: [refresh] Start $name:docker start $name"
        ((stopped++))
    fi
done

echo "row: "
echo "row: [cyan1]Running:[/] $running [grey70]|[/] [red]Stopped:[/] $stopped"

# Global actions
echo "action: [danger,refresh] Restart All:docker restart \$(docker ps -q)"
echo "action: Docker Stats:docker stats --no-stream"
```

**Config:**
```yaml
widgets:
  docker-services:
    path: docker-services.sh
    sha256: <checksum>
    refresh: 5
```

---

### Git Deployment Widget

Show repository status with pull/deploy actions.

**File:** `~/.config/serverhub/widgets/git-deploy.sh`

```bash
#!/bin/bash
# Git Deployment Status

echo "title: Deployment"

REPO_PATH="/var/www/myapp"

if [ ! -d "$REPO_PATH/.git" ]; then
    echo "row: [yellow]Not a git repository[/]"
    exit 0
fi

cd "$REPO_PATH" || exit

# Get current branch and commit
branch=$(git rev-parse --abbrev-ref HEAD 2>/dev/null)
commit=$(git rev-parse --short HEAD 2>/dev/null)
commit_msg=$(git log -1 --pretty=%B 2>/dev/null | head -n 1)

echo "row: [cyan1]Branch:[/] $branch"
echo "row: [grey70]Commit:[/] $commit"
echo "row: [grey70]${commit_msg}[/]"

# Check for updates
git fetch origin "$branch" &>/dev/null
behind=$(git rev-list HEAD..origin/"$branch" --count 2>/dev/null)

if [ "$behind" -gt 0 ]; then
    echo "row: "
    echo "row: [yellow]⚠[/] $behind commit(s) behind origin"
    echo "action: [danger,refresh] Pull Changes:cd $REPO_PATH && git pull origin $branch"
    echo "action: [danger,refresh,timeout=120] Deploy:cd $REPO_PATH && git pull && npm install && pm2 restart myapp"
else
    echo "row: "
    echo "row: [green]✓[/] Up to date"
fi

# Last deployment time
last_pull=$(stat -c %Y "$REPO_PATH/.git/FETCH_HEAD" 2>/dev/null)
if [ -n "$last_pull" ]; then
    last_pull_time=$(date -d "@$last_pull" '+%Y-%m-%d %H:%M')
    echo "row: [grey70]Last pull: $last_pull_time[/]"
fi

# Always available actions
echo "action: View Git Log:cd $REPO_PATH && git log --oneline -10"
echo "action: Git Status:cd $REPO_PATH && git status"
```

**Config:**
```yaml
widgets:
  git-deploy:
    path: git-deploy.sh
    sha256: <checksum>
    refresh: 30
```

---

## Example 2: Homelab Server

**Scenario:** Home server running Proxmox with VMs, a NAS for storage, and automated backups.

### Proxmox VM Status Widget

**File:** `~/.config/serverhub/widgets/proxmox-vms.sh`

```bash
#!/bin/bash
# Proxmox VM Status Monitor

echo "title: Proxmox VMs"

# Requires: pvesh (Proxmox API shell)
if ! command -v pvesh &> /dev/null; then
    echo "row: [yellow]Proxmox tools not available[/]"
    exit 0
fi

# Get VM list (adjust node name)
vms=$(pvesh get /nodes/pve/qemu --output-format json 2>/dev/null)

if [ -z "$vms" ]; then
    echo "row: [grey70]No VMs found[/]"
    exit 0
fi

running=0
stopped=0

echo "$vms" | jq -r '.[] | "\(.vmid)|\(.name)|\(.status)"' | while IFS='|' read -r vmid name status; do
    if [ "$status" = "running" ]; then
        echo "row: [green]●[/] $name (ID: $vmid)"
        echo "action: [danger,sudo,refresh] Stop $name:qm stop $vmid"
        echo "action: [danger,sudo,refresh] Reboot $name:qm reboot $vmid"
        echo "action: [timeout=0] Console $name:qm terminal $vmid"
        ((running++))
    else
        echo "row: [grey70]○[/] $name (ID: $vmid) - Stopped"
        echo "action: [sudo,refresh] Start $name:qm start $vmid"
        ((stopped++))
    fi
done

echo "row: "
echo "row: [cyan1]Running:[/] $running [grey70]|[/] [grey70]Stopped:[/] $stopped"
```

---

### NAS Health Widget

**File:** `~/.config/serverhub/widgets/nas-health.sh`

```bash
#!/bin/bash
# NAS Health Monitor

echo "title: NAS Health"

NAS_MOUNT="/mnt/nas"
NAS_HOST="nas.local"

# Check if mounted
if mountpoint -q "$NAS_MOUNT"; then
    used=$(df -h "$NAS_MOUNT" | awk 'NR==2 {print $3}')
    total=$(df -h "$NAS_MOUNT" | awk 'NR==2 {print $2}')
    percent=$(df "$NAS_MOUNT" | awk 'NR==2 {print $5}' | tr -d '%')

    echo "row: [status:ok] NAS Mounted - Healthy"
    echo "row: [cyan1]Space:[/] $used / $total"
    echo "row: [progress:$percent:inline]"

    echo "action: [danger,sudo,refresh] Unmount NAS:umount $NAS_MOUNT"
    echo "action: View Files:ls -lh $NAS_MOUNT"
else
    echo "row: [red]○[/] NAS Not Mounted"
    echo "action: [sudo,refresh] Mount NAS:mount -t nfs $NAS_HOST:/volume1/backup $NAS_MOUNT"
fi

# Check if NAS host is reachable
if ping -c 1 -W 2 "$NAS_HOST" &>/dev/null; then
    echo "row: [green]✓[/] Host reachable"
else
    echo "row: [red]✗[/] Host unreachable"
fi
```

---

## Example 3: Production Monitor

**Scenario:** Production server where you need quick visibility into critical services and ability to take emergency actions.

### Service Health Widget

**File:** `~/.config/serverhub/widgets/critical-services.sh`

```bash
#!/bin/bash
# Critical Services Monitor

echo "title: Critical Services"

SERVICES=("nginx" "postgresql" "redis")

for service in "${SERVICES[@]}"; do
    if systemctl is-active --quiet "$service"; then
        uptime=$(systemctl show "$service" -p ActiveEnterTimestamp --value)
        echo "row: [status:ok] $service - Running"
        echo "action: [danger,sudo,refresh] Restart $service:systemctl restart $service"
        echo "action: [danger,sudo,refresh] Stop $service:systemctl stop $service"
        echo "action: Logs $service:journalctl -u $service -n 50 --no-pager"
    else
        echo "row: [status:error] $service - Stopped"
        echo "action: [sudo,refresh] Start $service:systemctl start $service"
        echo "action: Status $service:systemctl status $service"
    fi
done

echo "row: "
echo "row: [grey70]Last check: $(date '+%H:%M:%S')[/]"

# Emergency action
echo "action: [danger,sudo,refresh] Restart All Services:systemctl restart nginx postgresql redis"
```

---

### Error Log Monitor Widget

**File:** `~/.config/serverhub/widgets/error-logs.sh`

```bash
#!/bin/bash
# Recent Error Logs

echo "title: Error Logs"

LOG_FILE="/var/log/myapp/error.log"

if [ ! -f "$LOG_FILE" ]; then
    echo "row: [grey70]Log file not found[/]"
    exit 0
fi

# Count errors in last 5 minutes
recent_errors=$(find "$LOG_FILE" -mmin -5 -exec grep -c "ERROR" {} \; 2>/dev/null || echo "0")

if [ "$recent_errors" -gt 10 ]; then
    echo "row: [red]⚠[/] $recent_errors errors in last 5 minutes"
elif [ "$recent_errors" -gt 0 ]; then
    echo "row: [yellow]⚠[/] $recent_errors errors in last 5 minutes"
else
    echo "row: [green]✓[/] No recent errors"
fi

# Show last 3 errors
echo "row: "
echo "row: [grey70]Recent errors:[/]"
tail -n 100 "$LOG_FILE" | grep "ERROR" | tail -n 3 | while read -r line; do
    timestamp=$(echo "$line" | cut -d' ' -f1-2)
    message=$(echo "$line" | cut -d' ' -f4- | cut -c1-50)
    echo "row: [grey70]$timestamp[/] $message..."
done

# Actions
echo "action: View Full Log:tail -n 100 $LOG_FILE"
echo "action: [danger,sudo,refresh] Clear Old Logs:find /var/log/myapp -name '*.log' -mtime +7 -delete"
echo "action: [timeout=0] Watch Live:tail -f $LOG_FILE"
```

---

## Tips for Writing Custom Widgets

1. **Start simple** - Get basic output working, then add actions
2. **Handle errors** - Check if commands exist before using them
3. **Use timeouts** - Don't let network calls hang indefinitely
4. **Context-aware actions** - Export different actions based on current state
5. **Mark dangerous actions** - Use `[danger]` flag for destructive operations
6. **Use sudo flag** - Add `[sudo]` when action needs elevated privileges
7. **Use refresh flag** - Add `[refresh]` to auto-refresh widget after action completes
8. **Test thoroughly** - Run widget manually before adding to config

## Action Protocol Syntax

```bash
echo "action: Label:command"                         # Basic action
echo "action: [danger] Label:command"                # Destructive action (shows warning)
echo "action: [sudo] Label:command"                  # Requires sudo authentication
echo "action: [danger,sudo] Label:command"           # Both flags
echo "action: [refresh] Label:command"               # Refresh widget after completion
echo "action: [timeout=120] Label:command"           # Custom timeout (seconds)
echo "action: [timeout=0] Label:command"             # No timeout (infinite)
echo "action: [danger,sudo,refresh] Label:command"   # Multiple flags
```

**Flags:**
- `danger` - Shows warning, requires confirmation
- `sudo` - Prompts for password if needed
- `refresh` - Auto-refreshes widget after action completes
- `timeout=N` - Custom timeout in seconds (default: 60, use 0 for infinite)
```

## Getting Help

- **Widget Protocol:** See [WIDGET_PROTOCOL.md](WIDGET_PROTOCOL.md) for full protocol reference
- **Security:** Custom widgets require SHA256 validation - see README Security section
- **Discovery:** Use `serverhub --discover` to add widgets with checksums automatically
