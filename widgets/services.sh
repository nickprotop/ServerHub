#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

echo "title: System Services"
echo "refresh: 5"

# Check if systemctl is available
if ! command -v systemctl &> /dev/null; then
    echo "row: [status:error] systemd not available"
    exit 0
fi

# Common critical services to monitor
SERVICES=(
    "sshd.service"
    "docker.service"
    "nginx.service"
    "apache2.service"
    "postgresql.service"
    "mysql.service"
    "redis.service"
    "cron.service"
    "systemd-resolved.service"
    "networkd.service"
)

# Count services
active_count=0
inactive_count=0
failed_count=0

echo "row: [bold]Service Status:[/]"
echo "row: "

for service in "${SERVICES[@]}"; do
    # Check if service exists
    if systemctl list-unit-files "$service" &>/dev/null; then
        status=$(systemctl is-active "$service" 2>/dev/null)
        enabled=$(systemctl is-enabled "$service" 2>/dev/null)

        service_name=$(echo "$service" | sed 's/.service//')

        case "$status" in
            active)
                status_indicator="ok"
                status_text="[green]active[/]"
                ((active_count++))
                ;;
            inactive)
                status_indicator="warn"
                status_text="[grey70]inactive[/]"
                ((inactive_count++))
                ;;
            failed)
                status_indicator="error"
                status_text="[red]failed[/]"
                ((failed_count++))
                ;;
            *)
                continue
                ;;
        esac

        # Only show if service is installed
        echo "row: [status:$status_indicator] [cyan1]${service_name}[/]: $status_text"
    fi
done

echo "row: "

# Failed services summary
all_failed=$(systemctl list-units --state=failed --no-pager --no-legend | wc -l)
if [ $all_failed -gt 0 ]; then
    echo "row: [status:error] Failed services: [red]$all_failed[/]"

    # Show failed service names
    systemctl list-units --state=failed --no-pager --no-legend | head -n 5 | while read -r line; do
        service_name=$(echo "$line" | awk '{print $2}')
        echo "row: [grey70]  - $service_name[/]"
    done
fi

# Summary
echo "row: "
echo "row: [grey70]Active: $active_count | Inactive: $inactive_count | Failed: $failed_count[/]"

# Actions
echo "action: Restart nginx:systemctl restart nginx"
echo "action: Restart docker:systemctl restart docker"
