#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

echo "title: Disk Usage"
echo "refresh: 10"

# Setup cache directory for historical data
CACHE_DIR="$HOME/.cache/serverhub"
mkdir -p "$CACHE_DIR"

# Check for stale data based on last run timestamp
last_run_file="$CACHE_DIR/disk-last-run.txt"
current_time=$(date +%s)
if [ -f "$last_run_file" ]; then
    read -r last_time < "$last_run_file"
    time_diff=$((current_time - last_time))
    # If gap > 30 seconds (3x refresh interval), clear all disk history
    if [ "$time_diff" -gt 30 ]; then
        rm -f "$CACHE_DIR"/disk*-usage.txt
    fi
fi
echo "$current_time" > "$last_run_file"

# Dashboard mode: Quick overview of main filesystems
if [ "$EXTENDED" = false ]; then
    echo "row: [bold]Main Filesystems:[/]"
    echo "[table:Mount|Device|Size|Used|Available|Usage]"

    # Get main filesystems (excluding virtual/temporary)
    df -h 2>/dev/null | grep -vE '^(Filesystem|tmpfs|devtmpfs|overlay|shm|udev|efivarfs|none)' | head -n 3 | while read -r device size used avail percent mount; do
        # Parse percent (remove %)
        pct=${percent%\%}

        # Clamp to 0-100
        [ "$pct" -lt 0 ] && pct=0
        [ "$pct" -gt 100 ] && pct=100

        # Store history for trending
        mount_safe=$(echo "$mount" | tr '/' '_')
        history_file="$CACHE_DIR/disk${mount_safe}-usage.txt"

        echo "$pct" >> "$history_file"
        tail -n 10 "$history_file" > "${history_file}.tmp" 2>/dev/null
        mv "${history_file}.tmp" "$history_file" 2>/dev/null

        # Determine status color
        if [ "$pct" -lt 70 ]; then
            status_col="[green]ok[/]"
        elif [ "$pct" -lt 90 ]; then
            status_col="[yellow]warn[/]"
        else
            status_col="[red]critical[/]"
        fi

        # Truncate long paths
        mount_short=$(echo "$mount" | cut -c1-12)
        device_short=$(echo "$device" | awk -F'/' '{print $NF}' | cut -c1-12)

        echo "[tablerow:${mount_short}|${device_short}|${size}|${used}|${avail}|[miniprogress:${pct}:10:warm]]"
    done

    # Inode usage summary
    inode_percent=$(df -i / 2>/dev/null | awk 'NR==2{print $5}' | sed 's/%//')
    if [ -n "$inode_percent" ]; then
        echo "row: "
        echo "row: [grey70]Inodes (root): ${inode_percent}% used[/]"
    fi
else
    # Extended mode: Detailed view with historical trends
    echo "row: [bold]All Filesystems:[/]"
    echo "[table:Mount|Device|Type|Size|Used|Avail|Usage]"

    # List all mounted filesystems
    df -hT 2>/dev/null | grep -vE '^(Filesystem|tmpfs|devtmpfs|overlay|shm|udev|efivarfs|none)' | while read -r device fstype size used avail percent mount; do
        pct=${percent%\%}

        # Clamp to 0-100
        [ "$pct" -lt 0 ] && pct=0
        [ "$pct" -gt 100 ] && pct=100

        # Store history
        mount_safe=$(echo "$mount" | tr '/' '_')
        history_file="$CACHE_DIR/disk${mount_safe}-usage.txt"

        echo "$pct" >> "$history_file"
        tail -n 30 "$history_file" > "${history_file}.tmp" 2>/dev/null
        mv "${history_file}.tmp" "$history_file" 2>/dev/null

        # Status color
        if [ "$pct" -lt 70 ]; then
            status_col="[green]${percent}[/]"
        elif [ "$pct" -lt 90 ]; then
            status_col="[yellow]${percent}[/]"
        else
            status_col="[red]${percent}[/]"
        fi

        # Truncate names
        mount_short=$(echo "$mount" | cut -c1-15)
        device_short=$(echo "$device" | awk -F'/' '{print $NF}' | cut -c1-12)
        fstype_short=$(echo "$fstype" | cut -c1-6)

        echo "[tablerow:${mount_short}|${device_short}|${fstype_short}|${size}|${used}|${avail}|${status_col}]"
    done

    # Disk usage trends for critical filesystems
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Usage Trends (last 5 minutes):[/]"

    # Show graphs for filesystems >70% full
    df -h 2>/dev/null | grep -vE '^(Filesystem|tmpfs|devtmpfs|overlay|shm|udev|efivarfs|none)' | while read -r device size used avail percent mount; do
        pct=${percent%\%}

        if [ "$pct" -ge 70 ]; then
            mount_safe=$(echo "$mount" | tr '/' '_')
            history_file="$CACHE_DIR/disk${mount_safe}-usage.txt"

            if [ -f "$history_file" ] && [ -s "$history_file" ]; then
                history=$(paste -sd',' "$history_file")
                mount_short=$(echo "$mount" | cut -c1-25)

                echo "row: "
                echo "row: [cyan1]${mount_short}[/] [grey70](${percent} full)[/]"
                # Use 0-100 fixed scale for percentage graph
                echo "row: [graph:${history}:warm:Usage %:0-100]"
            fi
        fi
    done

    # Inode usage details
    echo "row: "
    echo "row: [divider:─:cyan1]"
    echo "row: "
    echo "row: [bold]Inode Usage:[/]"
    echo "[table:Mount|Total|Used|Available|Usage]"

    df -i 2>/dev/null | grep -vE '^(Filesystem|tmpfs|devtmpfs|overlay|shm|udev|efivarfs)' | while read -r device total used avail percent mount; do
        [ "$percent" = "-" ] && continue
        [ -z "$percent" ] && continue

        pct=${percent%\%}
        mount_short=$(echo "$mount" | cut -c1-15)

        # Format numbers (add K suffix for thousands)
        if [ "$used" -gt 1000000 ]; then
            used_fmt="$((used / 1000000))M"
        elif [ "$used" -gt 1000 ]; then
            used_fmt="$((used / 1000))K"
        else
            used_fmt="$used"
        fi

        if [ "$pct" -gt 80 ]; then
            percent_col="[yellow]${percent}[/]"
        else
            percent_col="$percent"
        fi

        echo "[tablerow:${mount_short}|${total}|${used_fmt}|${avail}|${percent_col}]"
    done

    # I/O Statistics
    if command -v iostat &> /dev/null; then
        echo "row: "
        echo "row: [divider]"
        echo "row: "
        echo "row: [bold]I/O Statistics:[/]"
        echo "[table:Device|Read/s|Write/s|Utilization]"

        iostat -d -x 1 2 2>/dev/null | tail -n +7 | head -n 8 | while read -r device rrqm wrqm r w rkb wkb avgrq avgqu await svctm util; do
            [ -z "$device" ] && continue
            [ "$device" = "Device" ] && continue

            # Format values
            r_fmt=$(echo "$r" | awk '{printf "%.1f", $1}')
            w_fmt=$(echo "$w" | awk '{printf "%.1f", $1}')
            util_fmt=$(echo "$util" | awk '{printf "%.0f", $1}')

            # Util color
            if [ "${util_fmt%.*}" -gt 80 ]; then
                util_col="[yellow]${util_fmt}%[/]"
            else
                util_col="${util_fmt}%"
            fi

            echo "[tablerow:${device}|${r_fmt}|${w_fmt}|${util_col}]"
        done
    fi

    # Top 10 largest directories
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Largest Directories (/):[/]"
    echo "[table:Size|Directory]"

    du -hx --max-depth=1 / 2>/dev/null | sort -hr | head -n 10 | while read -r size dir; do
        [ "$dir" = "/" ] && continue
        dir_short=$(echo "$dir" | cut -c1-40)
        echo "[tablerow:${size}|${dir_short}]"
    done

    # Disk health (SMART)
    if command -v smartctl &> /dev/null; then
        echo "row: "
        echo "row: [divider]"
        echo "row: "
        echo "row: [bold]Disk Health (SMART):[/]"
        echo "[table:Device|Health|Temperature|Power-On Hours]"

        for disk in /dev/sd? /dev/nvme?n?; do
            [ -b "$disk" ] || continue
            disk_name=$(basename "$disk")

            # Get SMART data (suppress errors)
            smart_output=$(sudo smartctl -a "$disk" 2>/dev/null)

            # Health status
            if echo "$smart_output" | grep -qi "health.*passed\|overall.*passed"; then
                health="[green]PASSED[/]"
            elif echo "$smart_output" | grep -qi "health.*failed"; then
                health="[red]FAILED[/]"
            else
                health="[grey70]N/A[/]"
            fi

            # Temperature
            temp=$(echo "$smart_output" | grep -i "temperature" | head -n1 | grep -oP '\d+(?= Celsius)' || echo "-")
            if [ "$temp" != "-" ]; then
                temp="${temp}°C"
            fi

            # Power-on hours
            hours=$(echo "$smart_output" | grep -i "power_on_hours\|power on hours" | head -n1 | awk '{for(i=1;i<=NF;i++) if($i ~ /^[0-9]+$/) {print $i; exit}}' || echo "-")
            if [ "$hours" != "-" ] && [ "$hours" -gt 8760 ]; then
                # Convert to years if > 1 year
                years=$(awk "BEGIN {printf \"%.1f\", $hours/8760}")
                hours="${years}y"
            elif [ "$hours" != "-" ]; then
                hours="${hours}h"
            fi

            echo "[tablerow:${disk_name}|${health}|${temp}|${hours}]"
        done
    fi
fi

# Actions (context-based on usage)
# Check if any filesystem is over 80%
critical_fs=$(df -h 2>/dev/null | grep -vE '^(Filesystem|tmpfs|devtmpfs)' | awk '{gsub(/%/,""); if($5 > 80) print $6}' | head -n1)

if [ -n "$critical_fs" ]; then
    echo "action: [sudo] Clean apt cache:apt clean && apt autoremove -y"
    echo "action: [sudo] Clean journal logs:journalctl --vacuum-time=7d"
    echo "action: Find large files (${critical_fs}):find ${critical_fs} -xdev -type f -size +100M -exec ls -lh {} \\; 2>/dev/null | sort -k5 -hr | head -20"
    echo "action: Analyze usage (${critical_fs}):du -hx --max-depth=2 ${critical_fs} 2>/dev/null | sort -hr | head -20"
fi

echo "action: Show all mounts:df -hT"
echo "action: Show inode usage:df -i"
echo "action: Clear usage history:rm -f $CACHE_DIR/disk_*-usage.txt"
