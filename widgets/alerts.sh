#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

echo "title: System Alerts"
echo "refresh: 30"

# Initialize alert counters
critical_count=0
warning_count=0

# Collect health check results
disk_alert=""
disk_status="ok"
failed_services_count=0
zombie_count=0
memory_alert=""
memory_status="ok"
cpu_alert=""
cpu_status="ok"
docker_alert=""
docker_status="ok"
ssl_alert=""
ssl_status="ok"
updates_alert=""
updates_status="ok"

# 1. Check disk usage (>85% critical, >75% warning)
if command -v df &> /dev/null; then
    while IFS= read -r line; do
        usage=$(echo "$line" | awk '{print $5}' | sed 's/%//')
        mount=$(echo "$line" | awk '{print $6}')

        if [ "$usage" -ge 85 ]; then
            disk_alert="$mount at ${usage}%"
            disk_status="error"
            ((critical_count++))
            break
        elif [ "$usage" -ge 75 ] && [ "$disk_status" = "ok" ]; then
            disk_alert="$mount at ${usage}%"
            disk_status="warn"
            ((warning_count++))
        fi
    done < <(df -h | grep -E '^/dev/' | grep -v '/boot')
fi

# 2. Check failed systemd services
if command -v systemctl &> /dev/null; then
    failed_services_count=$(systemctl list-units --state=failed --no-pager --no-legend 2>/dev/null | wc -l)

    if [ "$failed_services_count" -gt 0 ]; then
        ((critical_count++))
    fi
fi

# 3. Check for zombie processes
zombie_count=$(ps aux 2>/dev/null | awk '{print $8}' | grep -c '^Z')
if [ "$zombie_count" -gt 5 ]; then
    ((warning_count++))
fi

# 4. Check memory usage (>90% critical, >80% warning)
if [ -f /proc/meminfo ]; then
    mem_total=$(grep MemTotal /proc/meminfo | awk '{print $2}')
    mem_available=$(grep MemAvailable /proc/meminfo | awk '{print $2}')
    mem_used=$((mem_total - mem_available))
    mem_usage=$((mem_used * 100 / mem_total))

    if [ "$mem_usage" -ge 90 ]; then
        memory_alert="${mem_usage}%"
        memory_status="error"
        ((critical_count++))
    elif [ "$mem_usage" -ge 80 ]; then
        memory_alert="${mem_usage}%"
        memory_status="warn"
        ((warning_count++))
    fi
fi

# 5. Check CPU load average vs available cores
if [ -f /proc/loadavg ]; then
    load_avg=$(awk '{print $1}' /proc/loadavg)
    cpu_cores=$(nproc 2>/dev/null || echo "1")
    load_percent=$(awk "BEGIN {printf \"%.0f\", ($load_avg / $cpu_cores) * 100}")

    if [ "$load_percent" -ge 90 ]; then
        cpu_alert="${load_percent}% (${load_avg}/${cpu_cores})"
        cpu_status="error"
        ((critical_count++))
    elif [ "$load_percent" -ge 70 ]; then
        cpu_alert="${load_percent}% (${load_avg}/${cpu_cores})"
        cpu_status="warn"
        ((warning_count++))
    fi
fi

# 6. Check Docker unhealthy containers (if Docker is available)
if command -v docker &> /dev/null && docker info &> /dev/null; then
    unhealthy_containers=$(docker ps --filter health=unhealthy --format '{{.Names}}' 2>/dev/null | wc -l)

    if [ "$unhealthy_containers" -gt 0 ]; then
        docker_alert="$unhealthy_containers unhealthy"
        docker_status="error"
        ((critical_count++))
    fi
fi

# 7. Check SSL certificate expiration
ssl_expiring=0
if [ -d /etc/letsencrypt/live ]; then
    for domain_dir in /etc/letsencrypt/live/*/; do
        if [ -d "$domain_dir" ]; then
            cert_file="${domain_dir}cert.pem"
            if [ -f "$cert_file" ]; then
                expiry=$(openssl x509 -enddate -noout -in "$cert_file" 2>/dev/null | cut -d= -f2)
                if [ -n "$expiry" ]; then
                    expiry_epoch=$(date -d "$expiry" +%s 2>/dev/null)
                    now_epoch=$(date +%s)
                    days_until_expiry=$(( (expiry_epoch - now_epoch) / 86400 ))

                    if [ "$days_until_expiry" -lt 7 ] && [ "$days_until_expiry" -ge 0 ]; then
                        ((ssl_expiring++))
                        ssl_status="error"
                    elif [ "$days_until_expiry" -lt 30 ] && [ "$days_until_expiry" -ge 0 ] && [ "$ssl_status" = "ok" ]; then
                        ((ssl_expiring++))
                        ssl_status="warn"
                    fi
                fi
            fi
        fi
    done

    if [ "$ssl_expiring" -gt 0 ]; then
        ssl_alert="$ssl_expiring cert(s) expiring"
        if [ "$ssl_status" = "error" ]; then
            ((critical_count++))
        else
            ((warning_count++))
        fi
    fi
fi

# 8. Check for available system updates (if apt is available)
if command -v apt &> /dev/null; then
    if [ -f /var/cache/apt/pkgcache.bin ]; then
        cache_age=$(( ($(date +%s) - $(stat -c %Y /var/cache/apt/pkgcache.bin)) / 3600 ))

        if [ "$cache_age" -lt 24 ]; then
            updates=$(apt list --upgradable 2>/dev/null | grep -c upgradable)
            security=$(apt list --upgradable 2>/dev/null | grep -ic security)

            if [ "$security" -gt 0 ]; then
                updates_alert="$security security"
                updates_status="error"
                ((critical_count++))
            elif [ "$updates" -gt 50 ]; then
                updates_alert="$updates available"
                updates_status="warn"
                ((warning_count++))
            elif [ "$updates" -gt 0 ]; then
                updates_alert="$updates available"
                updates_status="info"
            fi
        fi
    fi
fi

# Dashboard mode: compact table
if [ "$EXTENDED" = false ]; then
    if [ "$critical_count" -gt 0 ]; then
        echo "row: [status:error] Critical: [red]$critical_count[/] | Warnings: [yellow]$warning_count[/]"
    elif [ "$warning_count" -gt 0 ]; then
        echo "row: [status:warn] Critical: $critical_count | Warnings: [yellow]$warning_count[/]"
    else
        echo "row: [status:ok] System healthy - no alerts"
    fi
    echo "row: "

    # Health checks table
    echo "row: [bold]Health Checks:[/]"
    echo "[table:Check|Status|Details]"

    # Disk usage
    if [ "$disk_status" = "error" ]; then
        echo "[tablerow:Disk Usage|[red]Critical[/]|$disk_alert]"
    elif [ "$disk_status" = "warn" ]; then
        echo "[tablerow:Disk Usage|[yellow]Warning[/]|$disk_alert]"
    else
        echo "[tablerow:Disk Usage|[green]OK[/]|All < 75%]"
    fi

    # Failed services
    if [ "$failed_services_count" -gt 0 ]; then
        echo "[tablerow:Services|[red]Critical[/]|$failed_services_count failed]"
    else
        echo "[tablerow:Services|[green]OK[/]|All running]"
    fi

    # Zombie processes
    if [ "$zombie_count" -gt 5 ]; then
        echo "[tablerow:Processes|[yellow]Warning[/]|$zombie_count zombies]"
    else
        echo "[tablerow:Processes|[green]OK[/]|No zombies]"
    fi

    # Memory
    if [ "$memory_status" = "error" ]; then
        echo "[tablerow:Memory|[red]Critical[/]|$memory_alert]"
    elif [ "$memory_status" = "warn" ]; then
        echo "[tablerow:Memory|[yellow]Warning[/]|$memory_alert]"
    else
        echo "[tablerow:Memory|[green]OK[/]|< 80%]"
    fi

    # CPU load
    if [ "$cpu_status" = "error" ]; then
        echo "[tablerow:CPU Load|[red]Critical[/]|$cpu_alert]"
    elif [ "$cpu_status" = "warn" ]; then
        echo "[tablerow:CPU Load|[yellow]Warning[/]|$cpu_alert]"
    else
        echo "[tablerow:CPU Load|[green]OK[/]|Normal]"
    fi

    # Docker containers
    if command -v docker &> /dev/null && docker info &> /dev/null; then
        if [ "$docker_status" = "error" ]; then
            echo "[tablerow:Docker|[red]Critical[/]|$docker_alert]"
        else
            echo "[tablerow:Docker|[green]OK[/]|All healthy]"
        fi
    fi

    # SSL certificates
    if [ "$ssl_status" = "error" ]; then
        echo "[tablerow:SSL Certs|[red]Critical[/]|$ssl_alert]"
    elif [ "$ssl_status" = "warn" ]; then
        echo "[tablerow:SSL Certs|[yellow]Warning[/]|$ssl_alert]"
    elif [ -d /etc/letsencrypt/live ]; then
        cert_count=$(find /etc/letsencrypt/live -name "cert.pem" 2>/dev/null | wc -l)
        echo "[tablerow:SSL Certs|[green]OK[/]|$cert_count valid]"
    fi

    # System updates
    if [ "$updates_status" = "error" ]; then
        echo "[tablerow:Updates|[red]Critical[/]|$updates_alert]"
    elif [ "$updates_status" = "warn" ]; then
        echo "[tablerow:Updates|[yellow]Warning[/]|$updates_alert]"
    elif [ "$updates_status" = "info" ]; then
        echo "[tablerow:Updates|[cyan1]Info[/]|$updates_alert]"
    fi
else
    # Extended mode: detailed tables
    echo "row: [status:$([ "$critical_count" -gt 0 ] && echo "error" || ([ "$warning_count" -gt 0 ] && echo "warn" || echo "ok"))] Total Issues - Critical: [red]$critical_count[/] | Warnings: [yellow]$warning_count[/]"
    echo "row: "

    # All filesystems table
    echo "row: [bold]Filesystems:[/]"
    echo "[table:Mount|Size|Used|Avail|Use%|Status]"
    df -h | grep -E '^/dev/' | while read -r line; do
        device=$(echo "$line" | awk '{print $1}' | cut -d'/' -f3 | cut -c1-15)
        size=$(echo "$line" | awk '{print $2}')
        used=$(echo "$line" | awk '{print $3}')
        avail=$(echo "$line" | awk '{print $4}')
        usage=$(echo "$line" | awk '{print $5}' | sed 's/%//')
        mount=$(echo "$line" | awk '{print $6}' | cut -c1-20)

        if [ "$usage" -ge 85 ]; then
            echo "[tablerow:$mount|$size|$used|$avail|[red]${usage}%[/]|[red]Critical[/]]"
        elif [ "$usage" -ge 75 ]; then
            echo "[tablerow:$mount|$size|$used|$avail|[yellow]${usage}%[/]|[yellow]Warning[/]]"
        else
            echo "[tablerow:$mount|$size|$used|$avail|${usage}%|[green]OK[/]]"
        fi
    done

    # Failed services table
    if [ "$failed_services_count" -gt 0 ]; then
        echo "row: "
        echo "row: [divider]"
        echo "row: "
        echo "row: [bold]Failed Services:[/]"
        echo "[table:Service|State|Since]"
        systemctl list-units --state=failed --no-pager --no-legend 2>/dev/null | head -n 15 | while read -r line; do
            service_name=$(echo "$line" | awk '{print $2}' | sed 's/.service//' | cut -c1-30)
            state=$(echo "$line" | awk '{print $4}')
            echo "[tablerow:$service_name|[red]failed[/]|$state]"
        done
    fi

    # SSL certificates table
    if [ -d /etc/letsencrypt/live ]; then
        echo "row: "
        echo "row: [divider]"
        echo "row: "
        echo "row: [bold]SSL Certificates:[/]"
        echo "[table:Domain|Expires|Days Left|Status]"
        for domain_dir in /etc/letsencrypt/live/*/; do
            if [ -d "$domain_dir" ]; then
                cert_file="${domain_dir}cert.pem"
                if [ -f "$cert_file" ]; then
                    domain=$(basename "$domain_dir" | cut -c1-30)
                    expiry=$(openssl x509 -enddate -noout -in "$cert_file" 2>/dev/null | cut -d= -f2)
                    if [ -n "$expiry" ]; then
                        expiry_epoch=$(date -d "$expiry" +%s 2>/dev/null)
                        now_epoch=$(date +%s)
                        days_left=$(( (expiry_epoch - now_epoch) / 86400 ))
                        expiry_short=$(echo "$expiry" | cut -d' ' -f1-3)

                        if [ "$days_left" -lt 0 ]; then
                            echo "[tablerow:$domain|$expiry_short|[red]${days_left}d[/]|[red]Expired[/]]"
                        elif [ "$days_left" -lt 7 ]; then
                            echo "[tablerow:$domain|$expiry_short|[red]${days_left}d[/]|[red]Critical[/]]"
                        elif [ "$days_left" -lt 30 ]; then
                            echo "[tablerow:$domain|$expiry_short|[yellow]${days_left}d[/]|[yellow]Warning[/]]"
                        else
                            echo "[tablerow:$domain|$expiry_short|[green]${days_left}d[/]|[green]OK[/]]"
                        fi
                    fi
                fi
            fi
        done
    fi

    # Docker containers table
    if command -v docker &> /dev/null && docker info &> /dev/null; then
        echo "row: "
        echo "row: [divider]"
        echo "row: "
        echo "row: [bold]Docker Health:[/]"
        echo "[table:Container|Status|Health]"
        docker ps --format '{{.Names}}:{{.Status}}' 2>/dev/null | head -n 15 | while IFS=':' read -r name status; do
            name_short=$(echo "$name" | cut -c1-25)
            if echo "$status" | grep -qi "unhealthy"; then
                echo "[tablerow:$name_short|$status|[red]Unhealthy[/]]"
            elif echo "$status" | grep -qi "healthy"; then
                echo "[tablerow:$name_short|Up|[green]Healthy[/]]"
            else
                echo "[tablerow:$name_short|$status|[grey70]Unknown[/]]"
            fi
        done
    fi

    # Recent errors table
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Recent System Errors:[/]"
    echo "[table:Time|Service|Message]"
    journalctl --since "1 hour ago" -p err --no-pager -q -n 15 --output=short-precise 2>/dev/null | while read -r line; do
        timestamp=$(echo "$line" | awk '{print $1, $2, $3}')
        time_short=$(echo "$timestamp" | awk -F'.' '{print $1}' | awk -F'T' '{print $2}' | cut -c1-8)
        [ -z "$time_short" ] && time_short=$(echo "$timestamp" | awk '{print $3}' | cut -c1-8)
        unit=$(echo "$line" | awk '{print $4}' | tr -d ':' | cut -c1-15)
        message=$(echo "$line" | cut -d' ' -f5- | cut -c1-40)
        echo "[tablerow:[grey70]$time_short[/]|[cyan1]$unit[/]|[red]$message[/]]"
    done

    # System statistics
    echo "row: "
    echo "row: [divider:─:cyan1]"
    echo "row: "
    echo "row: [bold]Statistics:[/]"
    echo "row: [grey70]Last check: $(date '+%Y-%m-%d %H:%M:%S')[/]"
    echo "row: [grey70]Total health checks: 8[/]"
    echo "row: [grey70]Critical issues: $critical_count[/]"
    echo "row: [grey70]Warnings: $warning_count[/]"
fi

# Actions
echo "action: [refresh] Refresh alerts:true"

if [ "$failed_services_count" -gt 0 ]; then
    first_failed=$(systemctl list-units --state=failed --no-pager --no-legend 2>/dev/null | head -n1 | awk '{print $2}')
    first_failed_short=$(echo "$first_failed" | sed 's/.service//')
    echo "action: [sudo,refresh] Restart ${first_failed_short}:systemctl restart ${first_failed}"
fi

if [ "$critical_count" -gt 0 ] || [ "$warning_count" -gt 0 ]; then
    echo "action: View system logs:journalctl --since '1 hour ago' -p warning --no-pager | head -50"
fi

if [ "$ssl_expiring" -gt 0 ] && [ -d /etc/letsencrypt ]; then
    echo "action: [sudo,refresh,timeout=180] Renew SSL certs:certbot renew"
fi
