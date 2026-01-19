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
home_mount=$(df /home | awk 'NR==2{print $6}')
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
inode_percent=$(df -i / | awk 'NR==2{print $5}' | sed 's/%//')
echo "row: [grey70]Inodes: ${inode_percent}% used[/]"

# Get disk I/O stats if available
if command -v iostat &> /dev/null; then
    io_stats=$(iostat -d -x 1 2 | tail -n 1 | awk '{printf "r/s: %.1f w/s: %.1f", $4, $5}')
    echo "row: [grey70]${io_stats}[/]"
fi

# Extended mode: show all mounted filesystems and detailed info
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [bold]All Filesystems:[/]"

    # List all mounted filesystems (excluding virtual filesystems)
    df -hT | grep -vE '^(Filesystem|tmpfs|devtmpfs|overlay|shm)' | while read -r device fstype size used avail percent mount; do
        # Skip header row
        [ "$device" = "Filesystem" ] && continue

        # Parse percent (remove %)
        pct=${percent%\%}

        # Determine status color
        if [ "$pct" -lt 70 ]; then
            status="ok"
        elif [ "$pct" -lt 90 ]; then
            status="warn"
        else
            status="error"
        fi

        # Truncate long mount points
        mount_short=$(echo "$mount" | cut -c1-25)
        device_short=$(echo "$device" | awk -F'/' '{print $NF}')

        echo "row: [status:$status] ${mount_short}"
        echo "row: [grey70]  ${device_short} (${fstype}): ${used}/${size} (${percent})[/]"
    done

    echo "row: "
    echo "row: [bold]Inode Usage:[/]"
    df -i | grep -vE '^(Filesystem|tmpfs|devtmpfs|overlay|shm)' | while read -r device total used avail percent mount; do
        [ "$device" = "Filesystem" ] && continue
        [ "$percent" = "-" ] && continue

        pct=${percent%\%}
        mount_short=$(echo "$mount" | cut -c1-30)
        echo "row: [grey70]${mount_short}: ${percent} (${used} used)[/]"
    done

    # Show largest directories in root (if du is available)
    if command -v du &> /dev/null; then
        echo "row: "
        echo "row: [bold]Largest Directories (/):[/]"
        du -hx --max-depth=1 / 2>/dev/null | sort -hr | head -n 8 | while read -r size dir; do
            dir_short=$(echo "$dir" | cut -c1-25)
            echo "row: [grey70]${size}  ${dir_short}[/]"
        done
    fi
fi
