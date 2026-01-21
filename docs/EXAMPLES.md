# ServerHub Examples

Real-world custom widget examples showing how to monitor and control your specific setup.

## Origin Story

ServerHub was originally built to solve a specific problem: monitoring and managing a development droplet running Docker containers, APIs, and deployment scripts. Instead of SSH-ing in constantly to check status and run commands, the goal was to have everything visible and actionable from one dashboard.

This also serves as a practical showcase of [SharpConsoleUI](https://github.com/nickprotop/ConsoleEx) - demonstrating how to build responsive, interactive terminal applications with .NET.

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
        echo "row: [green]●[/] $name - [status:ok] Healthy"
    elif [ -z "$response" ]; then
        echo "row: [red]●[/] $name - [status:error] Unreachable"
    else
        echo "row: [yellow]●[/] $name - [status:warning] HTTP $response"
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
echo "action: Restart Main API|systemctl restart myapp-api"
echo "action: Restart Staging|systemctl restart myapp-staging"
echo "action: View Logs|journalctl -u myapp-api -n 50 --no-pager"
```

**Config:**
```yaml
widgets:
  api-health:
    path: api-health.sh
    sha256: <checksum>
    refresh: 10
    priority: 1
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
        echo "row: [green]●[/] $name - [status:ok] Running"
        echo "action: Restart $name|docker restart $name"
        echo "action: Stop $name|docker stop $name"
        echo "action: Logs: $name|docker logs --tail 50 $name"
        ((running++))
    else
        echo "row: [red]●[/] $name - [status:error] Stopped"
        echo "action: Start $name|docker start $name"
        ((stopped++))
    fi
done

echo "row: "
echo "row: [cyan1]Running:[/] $running [grey70]|[/] [red]Stopped:[/] $stopped"

# Global actions
echo "action: Restart All|docker restart \$(docker ps -q)"
echo "action: Docker Stats|docker stats --no-stream"
```

**Config:**
```yaml
widgets:
  docker-services:
    path: docker-services.sh
    sha256: <checksum>
    refresh: 5
    priority: 1
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
    echo "action: Pull Changes|cd $REPO_PATH && git pull origin $branch|danger"
    echo "action: Deploy|cd $REPO_PATH && git pull && npm install && pm2 restart myapp|danger"
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
echo "action: View Git Log|cd $REPO_PATH && git log --oneline -10"
echo "action: Git Status|cd $REPO_PATH && git status"
```

**Config:**
```yaml
widgets:
  git-deploy:
    path: git-deploy.sh
    sha256: <checksum>
    refresh: 30
    priority: 1
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
        echo "action: Stop $name|qm stop $vmid|danger"
        echo "action: Reboot $name|qm reboot $vmid|danger"
        echo "action: Console $name|qm terminal $vmid"
        ((running++))
    else
        echo "row: [grey70]○[/] $name (ID: $vmid) - Stopped"
        echo "action: Start $name|qm start $vmid"
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

    echo "row: [green]●[/] NAS Mounted - [status:ok] Healthy"
    echo "row: [cyan1]Space:[/] $used / $total"
    echo "row: [progress:$percent:inline]"

    echo "action: Unmount NAS|umount $NAS_MOUNT"
    echo "action: View Files|ls -lh $NAS_MOUNT"
else
    echo "row: [red]○[/] NAS Not Mounted"
    echo "action: Mount NAS|mount -t nfs $NAS_HOST:/volume1/backup $NAS_MOUNT"
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
        echo "row: [green]●[/] $service - [status:ok] Running"
        echo "action: Restart $service|systemctl restart $service|danger|sudo"
        echo "action: Stop $service|systemctl stop $service|danger|sudo"
        echo "action: Logs: $service|journalctl -u $service -n 50 --no-pager"
    else
        echo "row: [red]●[/] $service - [status:error] Stopped"
        echo "action: Start $service|systemctl start $service|sudo"
        echo "action: Status: $service|systemctl status $service"
    fi
done

echo "row: "
echo "row: [grey70]Last check: $(date '+%H:%M:%S')[/]"

# Emergency action
echo "action: Restart All Services|systemctl restart nginx postgresql redis|danger|sudo"
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
echo "action: View Full Log|tail -n 100 $LOG_FILE"
echo "action: Clear Old Logs|find /var/log/myapp -name '*.log' -mtime +7 -delete|danger|sudo"
echo "action: Watch Live|tail -f $LOG_FILE"
```

---

## Tips for Writing Custom Widgets

1. **Start simple** - Get basic output working, then add actions
2. **Handle errors** - Check if commands exist before using them
3. **Use timeouts** - Don't let network calls hang indefinitely
4. **Context-aware actions** - Export different actions based on current state
5. **Mark dangerous actions** - Use `|danger` flag for destructive operations
6. **Use sudo flag** - Add `|sudo` when action needs elevated privileges
7. **Test thoroughly** - Run widget manually before adding to config

## Action Protocol Syntax

```bash
echo "action: Label|command"                    # Basic action
echo "action: Label|command|danger"             # Destructive action (shows warning)
echo "action: Label|command|sudo"               # Requires sudo authentication
echo "action: Label|command|danger|sudo"        # Both flags
echo "action: Label|command|timeout:120"        # Custom timeout (seconds)
echo "action: Label|command|timeout:0"          # No timeout (infinite)
```

## Getting Help

- **Widget Protocol:** See [WIDGET_PROTOCOL.md](WIDGET_PROTOCOL.md) for full protocol reference
- **Security:** Custom widgets require SHA256 validation - see README Security section
- **Discovery:** Use `serverhub --discover` to add widgets with checksums automatically
