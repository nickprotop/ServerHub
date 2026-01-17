#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

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
