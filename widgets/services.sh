#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

echo "title: System Services"
echo "refresh: 5"

# Check if systemctl is available
if ! command -v systemctl &> /dev/null; then
    echo "row: [status:error] systemd not available"
    exit 0
fi

# Key services to monitor in standard mode (reduced list)
KEY_SERVICES="sshd docker nginx apache2 postgresql mysql cron"

# Get all service states in one call
all_services=$(systemctl list-units --type=service --all --no-pager --no-legend 2>/dev/null)

# Track counts and found services
active_count=0
failed_count=0
inactive_count=0

echo "row: [bold]Key Services:[/]"
echo "row: "

# Check key services
for service in $KEY_SERVICES; do
    service_full="${service}.service"
    service_info=$(echo "$all_services" | grep -E "^\s*${service_full}\s+" | head -n1)

    if [ -n "$service_info" ]; then
        # Parse state from the line
        state=$(echo "$service_info" | awk '{print $3}')  # active/inactive
        sub_state=$(echo "$service_info" | awk '{print $4}')  # running/dead/failed

        case "$sub_state" in
            running)
                status_indicator="ok"
                status_text="[green]running[/]"
                ((active_count++))
                ;;
            exited)
                status_indicator="ok"
                status_text="[grey70]exited[/]"
                ((active_count++))
                ;;
            failed)
                status_indicator="error"
                status_text="[red]failed[/]"
                ((failed_count++))
                ;;
            dead)
                status_indicator="warn"
                status_text="[grey70]stopped[/]"
                ((inactive_count++))
                ;;
            *)
                status_indicator="warn"
                status_text="[grey70]$sub_state[/]"
                ((inactive_count++))
                ;;
        esac

        echo "row: [status:$status_indicator] [cyan1]${service}[/]: $status_text"
    fi
done

echo "row: "

# Failed services (from any service, not just key services)
all_failed=$(echo "$all_services" | grep -c "failed" || echo 0)
if [ "$all_failed" -gt 0 ]; then
    echo "row: [status:error] Failed services: [red]$all_failed[/]"

    # Show failed service names
    echo "$all_services" | grep "failed" | head -n 5 | while read -r line; do
        service_name=$(echo "$line" | awk '{print $2}' | sed 's/.service//')
        echo "row: [grey70]  → $service_name[/]"
    done
    echo "row: "
fi

# Summary
echo "row: [grey70]Active: $active_count | Stopped: $inactive_count | Failed: $failed_count[/]"

# Extended mode: show all active services
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [bold]All Active Services:[/]"

    echo "$all_services" | grep "running\|exited" | head -n 30 | while read -r line; do
        service_name=$(echo "$line" | awk '{print $1}' | sed 's/.service//')
        sub_state=$(echo "$line" | awk '{print $4}')

        if [ "$sub_state" = "running" ]; then
            echo "row: [status:ok] [grey70]$service_name[/]"
        else
            echo "row: [grey70]$service_name ($sub_state)[/]"
        fi
    done

    # Service resource usage (if available)
    echo "row: "
    echo "row: [bold]Top Services by Memory:[/]"
    systemctl list-units --type=service --state=running --no-pager --no-legend 2>/dev/null | awk '{print $1}' | head -n 10 | while read -r svc; do
        mem=$(systemctl show "$svc" --property=MemoryCurrent 2>/dev/null | cut -d= -f2)
        if [ -n "$mem" ] && [ "$mem" != "[not set]" ] && [ "$mem" -gt 0 ] 2>/dev/null; then
            mem_mb=$((mem / 1048576))
            svc_short=$(echo "$svc" | sed 's/.service//')
            echo "row: [grey70]$svc_short: ${mem_mb}MB[/]"
        fi
    done

    # Listening ports per service
    echo "row: "
    echo "row: [bold]Service Ports:[/]"
    ss -tlnp 2>/dev/null | grep -v "State" | head -n 10 | while read -r state recv send local peer process; do
        port=$(echo "$local" | awk -F':' '{print $NF}')
        proc_name=$(echo "$process" | grep -oP 'users:\(\("\K[^"]+' | head -n1)
        [ -n "$proc_name" ] && echo "row: [grey70]Port $port: $proc_name[/]"
    done
fi

# Dynamic actions based on installed/running services
# Check which services exist and their state

# Nginx actions
if echo "$all_services" | grep -q "nginx.service"; then
    nginx_state=$(echo "$all_services" | grep "nginx.service" | awk '{print $4}')
    if [ "$nginx_state" = "running" ]; then
        echo "action: [sudo,danger,refresh] Restart nginx:systemctl restart nginx"
        echo "action: [sudo,danger,refresh] Stop nginx:systemctl stop nginx"
    else
        echo "action: [sudo,refresh] Start nginx:systemctl start nginx"
    fi
    echo "action: View nginx logs:journalctl -u nginx -n 50 --no-pager"
fi

# Docker actions
if echo "$all_services" | grep -q "docker.service"; then
    docker_state=$(echo "$all_services" | grep "docker.service" | awk '{print $4}')
    if [ "$docker_state" = "running" ]; then
        echo "action: [sudo,danger,refresh] Restart docker:systemctl restart docker"
    else
        echo "action: [sudo,refresh] Start docker:systemctl start docker"
    fi
fi

# PostgreSQL actions
if echo "$all_services" | grep -q "postgresql.service"; then
    pg_state=$(echo "$all_services" | grep "postgresql.service" | awk '{print $4}')
    if [ "$pg_state" = "running" ]; then
        echo "action: [sudo,danger,refresh] Restart PostgreSQL:systemctl restart postgresql"
    else
        echo "action: [sudo,refresh] Start PostgreSQL:systemctl start postgresql"
    fi
fi

# MySQL actions
if echo "$all_services" | grep -q "mysql.service"; then
    mysql_state=$(echo "$all_services" | grep "mysql.service" | awk '{print $4}')
    if [ "$mysql_state" = "running" ]; then
        echo "action: [sudo,danger,refresh] Restart MySQL:systemctl restart mysql"
    else
        echo "action: [sudo,refresh] Start MySQL:systemctl start mysql"
    fi
fi

# Failed service restart actions
if [ "$all_failed" -gt 0 ]; then
    # Get first failed service for quick restart action (use $2 because $1 is the bullet ●)
    first_failed=$(echo "$all_services" | grep "failed" | head -n1 | awk '{print $2}')
    first_failed_short=$(echo "$first_failed" | sed 's/.service//')
    echo "action: [sudo,refresh] Restart ${first_failed_short}:systemctl restart ${first_failed}"
fi

# General actions
echo "action: List all services:systemctl list-units --type=service --all"
echo "action: [sudo] Reload systemd:systemctl daemon-reload"
