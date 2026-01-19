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
info_count=0

echo "row: [bold]Health Monitoring:[/]"
echo "row: "

# 1. Check disk usage (>90% critical, >80% warning)
if command -v df &> /dev/null; then
    while IFS= read -r line; do
        usage=$(echo "$line" | awk '{print $5}' | sed 's/%//')
        mount=$(echo "$line" | awk '{print $6}')

        if [ "$usage" -ge 90 ]; then
            echo "row: [status:error] Disk $mount at ${usage}% (critical)"
            ((critical_count++))
        elif [ "$usage" -ge 80 ]; then
            echo "row: [status:warn] Disk $mount at ${usage}% (high)"
            ((warning_count++))
        fi
    done < <(df -h | grep -E '^/dev/' | grep -v '/boot')
fi

# 2. Check failed systemd services
if command -v systemctl &> /dev/null; then
    failed_services=$(systemctl list-units --state=failed --no-pager --no-legend 2>/dev/null | wc -l)

    if [ "$failed_services" -gt 0 ]; then
        echo "row: [status:error] $failed_services failed service(s)"
        ((critical_count++))

        # Show first 3 failed services
        systemctl list-units --state=failed --no-pager --no-legend 2>/dev/null | head -n 3 | while read -r line; do
            service_name=$(echo "$line" | awk '{print $2}')
            echo "row: [grey70]  → $service_name[/]"
        done
    fi
fi

# 3. Check for zombie processes
zombie_count=$(ps aux 2>/dev/null | awk '{print $8}' | grep -c '^Z')
if [ "$zombie_count" -gt 0 ]; then
    echo "row: [status:warn] $zombie_count zombie process(es)"
    ((warning_count++))
fi

# 4. Check memory usage (>90% critical, >80% warning)
if [ -f /proc/meminfo ]; then
    mem_total=$(grep MemTotal /proc/meminfo | awk '{print $2}')
    mem_available=$(grep MemAvailable /proc/meminfo | awk '{print $2}')
    mem_used=$((mem_total - mem_available))
    mem_usage=$((mem_used * 100 / mem_total))

    if [ "$mem_usage" -ge 90 ]; then
        echo "row: [status:error] Memory usage at ${mem_usage}% (critical)"
        ((critical_count++))
    elif [ "$mem_usage" -ge 80 ]; then
        echo "row: [status:warn] Memory usage at ${mem_usage}% (high)"
        ((warning_count++))
    fi
fi

# 5. Check CPU load average vs available cores
if [ -f /proc/loadavg ]; then
    load_avg=$(awk '{print $1}' /proc/loadavg)
    cpu_cores=$(nproc 2>/dev/null || echo "1")

    # Convert load to percentage (load/cores * 100)
    load_percent=$(awk "BEGIN {printf \"%.0f\", ($load_avg / $cpu_cores) * 100}")

    if [ "$load_percent" -ge 90 ]; then
        echo "row: [status:error] CPU load at ${load_percent}% (${load_avg} / ${cpu_cores} cores)"
        ((critical_count++))
    elif [ "$load_percent" -ge 70 ]; then
        echo "row: [status:warn] CPU load at ${load_percent}% (${load_avg} / ${cpu_cores} cores)"
        ((warning_count++))
    fi
fi

# 6. Check Docker unhealthy containers (if Docker is available)
if command -v docker &> /dev/null && docker info &> /dev/null; then
    unhealthy_containers=$(docker ps --filter health=unhealthy --format '{{.Names}}' 2>/dev/null | wc -l)

    if [ "$unhealthy_containers" -gt 0 ]; then
        echo "row: [status:error] $unhealthy_containers unhealthy container(s)"
        ((critical_count++))

        # Show unhealthy container names
        docker ps --filter health=unhealthy --format '{{.Names}}' 2>/dev/null | head -n 3 | while read -r container; do
            echo "row: [grey70]  → $container[/]"
        done
    fi
fi

# 7. Check SSL certificate expiration (limit to 3 in standard mode)
ssl_checked=0
if [ -d /etc/letsencrypt/live ]; then
    for domain_dir in /etc/letsencrypt/live/*/; do
        [ "$ssl_checked" -ge 3 ] && break
        if [ -d "$domain_dir" ]; then
            cert_file="${domain_dir}cert.pem"
            if [ -f "$cert_file" ]; then
                expiry=$(openssl x509 -enddate -noout -in "$cert_file" 2>/dev/null | cut -d= -f2)
                if [ -n "$expiry" ]; then
                    expiry_epoch=$(date -d "$expiry" +%s 2>/dev/null)
                    now_epoch=$(date +%s)
                    days_until_expiry=$(( (expiry_epoch - now_epoch) / 86400 ))

                    if [ "$days_until_expiry" -lt 7 ] && [ "$days_until_expiry" -ge 0 ]; then
                        domain=$(basename "$domain_dir")
                        echo "row: [status:error] SSL cert expires in ${days_until_expiry}d: $domain"
                        ((critical_count++))
                        ((ssl_checked++))
                    elif [ "$days_until_expiry" -lt 30 ] && [ "$days_until_expiry" -ge 0 ]; then
                        domain=$(basename "$domain_dir")
                        echo "row: [status:warn] SSL cert expires in ${days_until_expiry}d: $domain"
                        ((warning_count++))
                        ((ssl_checked++))
                    fi
                fi
            fi
        fi
    done
fi

# 8. Check for available system updates (if apt is available)
if command -v apt &> /dev/null; then
    # Only check if apt cache is recent (to avoid slow updates)
    if [ -f /var/cache/apt/pkgcache.bin ]; then
        cache_age=$(( ($(date +%s) - $(stat -c %Y /var/cache/apt/pkgcache.bin)) / 3600 ))

        if [ "$cache_age" -lt 24 ]; then
            updates=$(apt list --upgradable 2>/dev/null | grep -c upgradable)
            security=$(apt list --upgradable 2>/dev/null | grep -ic security)

            if [ "$security" -gt 0 ]; then
                echo "row: [status:error] $security security update(s) available"
                ((critical_count++))
            elif [ "$updates" -gt 50 ]; then
                echo "row: [status:warn] $updates package updates available"
                ((warning_count++))
            elif [ "$updates" -gt 0 ]; then
                echo "row: [status:info] $updates package updates available"
                ((info_count++))
            fi
        fi
    fi
fi

# Summary
echo "row: "

if [ "$critical_count" -eq 0 ] && [ "$warning_count" -eq 0 ]; then
    echo "row: [status:ok] [green]✓ All systems healthy[/]"
else
    total_issues=$((critical_count + warning_count))
    echo "row: [grey70]Issues: [red]$critical_count critical[/] | [yellow]$warning_count warning[/][/]"
fi

# Show last check time
echo "row: [grey50]Last check: $(date '+%H:%M:%S')[/]"

# Extended mode: detailed alert information
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [bold]Detailed Health Check:[/]"

    # Full disk check
    echo "row: "
    echo "row: [bold]All Filesystems:[/]"
    df -h | grep -E '^/dev/' | while read -r line; do
        usage=$(echo "$line" | awk '{print $5}' | sed 's/%//')
        mount=$(echo "$line" | awk '{print $6}')
        size=$(echo "$line" | awk '{print $2}')
        used=$(echo "$line" | awk '{print $3}')

        if [ "$usage" -ge 90 ]; then
            echo "row: [status:error] $mount: ${used}/${size} (${usage}%)"
        elif [ "$usage" -ge 80 ]; then
            echo "row: [status:warn] $mount: ${used}/${size} (${usage}%)"
        else
            echo "row: [status:ok] $mount: ${used}/${size} (${usage}%)"
        fi
    done

    # All failed services
    if [ "$failed_services" -gt 0 ]; then
        echo "row: "
        echo "row: [bold]All Failed Services:[/]"
        systemctl list-units --state=failed --no-pager --no-legend 2>/dev/null | while read -r line; do
            service_name=$(echo "$line" | awk '{print $2}')
            echo "row: [red]$service_name[/]"
        done
    fi

    # All SSL certs
    echo "row: "
    echo "row: [bold]All SSL Certificates:[/]"
    for domain_dir in /etc/letsencrypt/live/*/; do
        if [ -d "$domain_dir" ]; then
            cert_file="${domain_dir}cert.pem"
            if [ -f "$cert_file" ]; then
                domain=$(basename "$domain_dir")
                expiry=$(openssl x509 -enddate -noout -in "$cert_file" 2>/dev/null | cut -d= -f2)
                if [ -n "$expiry" ]; then
                    expiry_epoch=$(date -d "$expiry" +%s 2>/dev/null)
                    now_epoch=$(date +%s)
                    days_until_expiry=$(( (expiry_epoch - now_epoch) / 86400 ))

                    if [ "$days_until_expiry" -lt 7 ]; then
                        echo "row: [status:error] $domain: ${days_until_expiry} days"
                    elif [ "$days_until_expiry" -lt 30 ]; then
                        echo "row: [status:warn] $domain: ${days_until_expiry} days"
                    else
                        echo "row: [status:ok] $domain: ${days_until_expiry} days"
                    fi
                fi
            fi
        fi
    done

    # Recent system errors
    echo "row: "
    echo "row: [bold]Recent System Errors:[/]"
    journalctl --since "1 hour ago" -p err --no-pager -q -n 5 2>/dev/null | while read -r line; do
        message=$(echo "$line" | cut -d' ' -f5- | cut -c1-50)
        echo "row: [grey70]$message[/]"
    done
fi

# Actions
echo "action: [refresh] Refresh alerts:true"

if [ "$failed_services" -gt 0 ]; then
    first_failed=$(systemctl list-units --state=failed --no-pager --no-legend 2>/dev/null | head -n1 | awk '{print $2}')
    first_failed_short=$(echo "$first_failed" | sed 's/.service//')
    echo "action: [sudo,refresh] Restart ${first_failed_short}:systemctl restart ${first_failed}"
fi

if [ "$critical_count" -gt 0 ] || [ "$warning_count" -gt 0 ]; then
    echo "action: View system logs:journalctl --since '1 hour ago' -p warning --no-pager | head -50"
fi
