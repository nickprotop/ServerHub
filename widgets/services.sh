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

# Key services to monitor in standard mode
KEY_SERVICES="sshd docker nginx apache2 postgresql mysql redis-server cron"

# Get all service states in one call
all_services=$(systemctl list-units --type=service --all --no-pager --no-legend 2>/dev/null)

# Count all services
total_active=$(echo "$all_services" | grep -c "running\|exited")
total_failed=$(echo "$all_services" | grep -c "failed")
total_inactive=$(echo "$all_services" | grep -c "inactive")

# Status indicator
status="ok"
if [ "$total_failed" -gt 0 ]; then
    status="error"
fi

# Dashboard mode: Compact tables
if [ "$EXTENDED" = false ]; then
    echo "row: [status:$status] Active: [green]$total_active[/] | Failed: [red]$total_failed[/]"
    echo "row: "

    # Key services table
    echo "row: [bold]Key Services:[/]"
    echo "[table:Service|Status]"

    for service in $KEY_SERVICES; do
        service_full="${service}.service"
        service_info=$(echo "$all_services" | grep -E "^\s*${service_full}\s+" | head -n1)

        if [ -n "$service_info" ]; then
            sub_state=$(echo "$service_info" | awk '{print $4}')

            case "$sub_state" in
                running)
                    echo "[tablerow:$service|[green]running[/]]"
                    ;;
                exited)
                    echo "[tablerow:$service|[cyan1]exited[/]]"
                    ;;
                failed)
                    echo "[tablerow:$service|[red]failed[/]]"
                    ;;
                dead)
                    echo "[tablerow:$service|[grey70]stopped[/]]"
                    ;;
                *)
                    echo "[tablerow:$service|[grey70]$sub_state[/]]"
                    ;;
            esac
        fi
    done

    # Failed services
    if [ "$total_failed" -gt 0 ]; then
        echo "row: "
        echo "row: [bold]Failed Services:[/]"
        echo "[table:Service|State]"
        echo "$all_services" | grep "failed" | head -n 8 | while read -r line; do
            # Handle both formats: with/without ● prefix
            service_name=$(echo "$line" | awk '{if ($1 == "●") print $2; else print $1}' | sed 's/.service//' | cut -c1-25)
            echo "[tablerow:$service_name|[red]failed[/]]"
        done
    fi
else
    # Extended mode: Detailed view with comprehensive tables
    echo "row: [status:$status] Total Services - Active: [green]$total_active[/] | Failed: [red]$total_failed[/] | Inactive: [grey70]$total_inactive[/]"
    echo "row: "

    # Key services table with descriptions
    echo "row: [bold]Key Services:[/]"
    echo "[table:Service|Status|State]"

    for service in $KEY_SERVICES; do
        service_full="${service}.service"
        service_info=$(echo "$all_services" | grep -E "^\s*${service_full}\s+" | head -n1)

        if [ -n "$service_info" ]; then
            state=$(echo "$service_info" | awk '{print $3}')
            sub_state=$(echo "$service_info" | awk '{print $4}')

            case "$sub_state" in
                running)
                    echo "[tablerow:$service|[green]running[/]|$state]"
                    ;;
                exited)
                    echo "[tablerow:$service|[cyan1]exited[/]|$state]"
                    ;;
                failed)
                    echo "[tablerow:$service|[red]failed[/]|$state]"
                    ;;
                dead)
                    echo "[tablerow:$service|[grey70]stopped[/]|$state]"
                    ;;
                *)
                    echo "[tablerow:$service|[grey70]$sub_state[/]|$state]"
                    ;;
            esac
        fi
    done

    # Failed services
    if [ "$total_failed" -gt 0 ]; then
        echo "row: "
        echo "row: [divider]"
        echo "row: "
        echo "row: [bold]Failed Services:[/]"
        echo "[table:Service|Status|Unit File]"
        echo "$all_services" | grep "failed" | head -n 15 | while read -r line; do
            # Handle both formats: with/without ● prefix
            if echo "$line" | grep -q "^●"; then
                service_name=$(echo "$line" | awk '{print $2}' | sed 's/.service//' | cut -c1-30)
                unit_state=$(echo "$line" | awk '{print $4}')
            else
                service_name=$(echo "$line" | awk '{print $1}' | sed 's/.service//' | cut -c1-30)
                unit_state=$(echo "$line" | awk '{print $3}')
            fi
            echo "[tablerow:$service_name|[red]failed[/]|$unit_state]"
        done
    fi

    # All active services
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Active Services (Top 30):[/]"
    echo "[table:Service|Status|Load State]"
    echo "$all_services" | grep "running\|exited" | head -n 30 | while read -r line; do
        service_name=$(echo "$line" | awk '{print $1}' | sed 's/.service//' | cut -c1-30)
        sub_state=$(echo "$line" | awk '{print $4}')
        load_state=$(echo "$line" | awk '{print $2}')

        if [ "$sub_state" = "running" ]; then
            echo "[tablerow:$service_name|[green]running[/]|$load_state]"
        else
            echo "[tablerow:$service_name|[cyan1]$sub_state[/]|$load_state]"
        fi
    done

    # Service memory usage
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Top Services by Memory:[/]"
    echo "[table:Service|Memory|Status]"

    # Get running services with memory info
    systemctl list-units --type=service --state=running --no-pager --no-legend 2>/dev/null | awk '{print $1}' | while read -r svc; do
        mem=$(systemctl show "$svc" --property=MemoryCurrent 2>/dev/null | cut -d= -f2)
        if [ -n "$mem" ] && [ "$mem" != "[not set]" ] && [ "$mem" -gt 0 ] 2>/dev/null; then
            mem_mb=$((mem / 1048576))
            svc_short=$(echo "$svc" | sed 's/.service//' | cut -c1-25)
            echo "$mem_mb|$svc_short"
        fi
    done | sort -rn -t'|' -k1 | head -n 15 | while IFS='|' read -r mem_mb svc_short; do
        # Calculate percentage for mini progress (assume max 500MB for scale)
        max_mem=500
        if [ "$mem_mb" -gt "$max_mem" ]; then
            pct=100
        else
            pct=$((mem_mb * 100 / max_mem))
        fi
        echo "[tablerow:$svc_short|${mem_mb}MB|[miniprogress:$pct:10]]"
    done

    # Service ports
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Service Listening Ports:[/]"
    echo "[table:Service|Port|Protocol]"
    ss -tlnp 2>/dev/null | grep -v "State" | head -n 20 | while read -r state recv send local peer process; do
        port=$(echo "$local" | awk -F':' '{print $NF}')
        proc_name=$(echo "$process" | grep -oP 'users:\(\("\K[^"]+' | head -n1)
        if [ -n "$proc_name" ]; then
            proc_short=$(echo "$proc_name" | cut -c1-25)
            echo "[tablerow:$proc_short|$port|TCP]"
        fi
    done

    # Service statistics
    echo "row: "
    echo "row: [divider:─:cyan1]"
    echo "row: "
    echo "row: [bold]Statistics:[/]"
    enabled_count=$(systemctl list-unit-files --type=service --state=enabled 2>/dev/null | grep -c "enabled")
    disabled_count=$(systemctl list-unit-files --type=service --state=disabled 2>/dev/null | grep -c "disabled")
    echo "row: [grey70]Enabled services: $enabled_count[/]"
    echo "row: [grey70]Disabled services: $disabled_count[/]"
    echo "row: [grey70]Total running: $(echo "$all_services" | grep -c "running")[/]"
fi

# Dynamic actions based on installed/running services
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
if [ "$total_failed" -gt 0 ]; then
    first_failed=$(echo "$all_services" | grep "failed" | head -n1 | awk '{print $1}')
    first_failed_short=$(echo "$first_failed" | sed 's/.service//')
    echo "action: [sudo,refresh] Restart $first_failed_short:systemctl restart $first_failed"
fi

# General actions
echo "action: List all services:systemctl list-units --type=service --all"
echo "action: [sudo,refresh] Reload systemd:systemctl daemon-reload"
echo "action: View failed:systemctl list-units --type=service --state=failed"
