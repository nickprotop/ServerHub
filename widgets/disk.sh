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

# Get root filesystem usage
root_info=$(df -h / | awk 'NR==2{print $3,$2,$5}')
read -r used total percent_str <<< "$root_info"
percent=${percent_str%\%}

# Determine status
if [ "$percent" -lt 70 ]; then
    status="ok"
elif [ "$percent" -lt 90 ]; then
    status="warn"
else
    status="error"
fi

echo "row: [status:$status] Root (/): ${used} / ${total} (${percent}%)"
echo "row: [progress:${percent}:inline]"

# Get home filesystem usage if different from root
home_mount=$(df /home 2>/dev/null | awk 'NR==2{print $6}')
if [ "$home_mount" != "/" ]; then
    home_info=$(df -h /home | awk 'NR==2{print $3,$2,$5}')
    read -r h_used h_total h_percent_str <<< "$home_info"
    h_percent=${h_percent_str%\%}

    if [ "$h_percent" -lt 70 ]; then
        h_status="ok"
    elif [ "$h_percent" -lt 90 ]; then
        h_status="warn"
    else
        h_status="error"
    fi

    echo "row: [status:$h_status] Home (/home): ${h_used} / ${h_total} (${h_percent}%)"
fi

# Get inode usage
inode_percent=$(df -i / 2>/dev/null | awk 'NR==2{print $5}' | sed 's/%//')
if [ -n "$inode_percent" ]; then
    echo "row: [grey70]Inodes: ${inode_percent}% used[/]"
fi

# Extended mode: detailed disk information
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [bold]All Filesystems:[/]"

    # List all mounted filesystems (excluding virtual filesystems)
    df -hT 2>/dev/null | grep -vE '^(Filesystem|tmpfs|devtmpfs|overlay|shm|udev|efivarfs)' | while read -r device fstype size used avail percent mount; do
        # Skip header row
        [ "$device" = "Filesystem" ] && continue

        # Parse percent (remove %)
        pct=${percent%\%}

        # Determine status color
        if [ "$pct" -lt 70 ]; then
            fs_status="ok"
        elif [ "$pct" -lt 90 ]; then
            fs_status="warn"
        else
            fs_status="error"
        fi

        # Truncate long mount points
        mount_short=$(echo "$mount" | cut -c1-25)
        device_short=$(echo "$device" | awk -F'/' '{print $NF}')

        echo "row: [status:$fs_status] ${mount_short}"
        echo "row: [grey70]  ${device_short} (${fstype}): ${used}/${size} (${percent})[/]"
    done

    # Inode usage details
    echo "row: "
    echo "row: [bold]Inode Usage:[/]"
    df -i 2>/dev/null | grep -vE '^(Filesystem|tmpfs|devtmpfs|overlay|shm|udev)' | while read -r device total used avail percent mount; do
        [ "$device" = "Filesystem" ] && continue
        [ "$percent" = "-" ] && continue

        pct=${percent%\%}
        mount_short=$(echo "$mount" | cut -c1-30)
        echo "row: [grey70]${mount_short}: ${percent} (${used} used)[/]"
    done

    # I/O stats (blocking OK in extended mode)
    if command -v iostat &> /dev/null; then
        echo "row: "
        echo "row: [bold]I/O Statistics:[/]"
        iostat -d -x 1 2 2>/dev/null | tail -n +7 | head -n 5 | while read -r device rrqm wrqm r w rkb wkb avgrq avgqu await svctm util; do
            [ -z "$device" ] && continue
            [ "$device" = "Device" ] && continue
            echo "row: [grey70]${device}: r/s=${r:-0} w/s=${w:-0} util=${util:-0}%[/]"
        done
    fi

    # Largest directories
    echo "row: "
    echo "row: [bold]Largest Directories (/):[/]"
    du -hx --max-depth=1 / 2>/dev/null | sort -hr | head -n 8 | while read -r size dir; do
        dir_short=$(echo "$dir" | cut -c1-25)
        echo "row: [grey70]${size}  ${dir_short}[/]"
    done

    # Disk health (if smartctl available)
    if command -v smartctl &> /dev/null; then
        echo "row: "
        echo "row: [bold]Disk Health:[/]"
        for disk in /dev/sd? /dev/nvme?n?; do
            [ -b "$disk" ] || continue
            disk_name=$(basename "$disk")
            health=$(sudo smartctl -H "$disk" 2>/dev/null | grep -i "health\|result" | head -n1)
            if echo "$health" | grep -qi "passed\|ok"; then
                echo "row: [status:ok] ${disk_name}: PASSED"
            elif [ -n "$health" ]; then
                echo "row: [status:error] ${disk_name}: CHECK REQUIRED"
            fi
        done
    fi
fi

# Actions (context-based on usage)
if [ "$percent" -gt 80 ]; then
    echo "action: [sudo] Clear apt cache:apt clean"
    echo "action: [sudo] Clear journal logs:journalctl --vacuum-time=3d"
    echo "action: Find large files:find /home -xdev -type f -size +100M -exec ls -lh {} \\; 2>/dev/null | sort -k5 -hr | head -20"
fi
echo "action: Analyze disk usage:du -hx --max-depth=1 / 2>/dev/null | sort -hr | head -20"
echo "action: Show all mounts:df -hT"
