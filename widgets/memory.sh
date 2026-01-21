#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

echo "title: Memory Usage"
echo "refresh: 2"

# Get memory usage
mem_info=$(free -m | awk 'NR==2{printf "%.0f %.0f %.0f", $3,$2,$3*100/$2}')
read -r used total percent <<< "$mem_info"

# Determine status
if [ "$percent" -lt 70 ]; then
    status="ok"
elif [ "$percent" -lt 90 ]; then
    status="warn"
else
    status="error"
fi

echo "row: [status:$status] Memory: ${used}MB / ${total}MB (${percent}%)"
echo "row: [progress:${percent}:inline]"

# Get swap info
swap_info=$(free -m | awk 'NR==3{if($2>0) printf "%.0f %.0f %.0f", $3,$2,$3*100/$2; else print "0 0 0"}')
read -r swap_used swap_total swap_percent <<< "$swap_info"

if [ "$swap_total" -gt 0 ]; then
    if [ "$swap_percent" -gt 50 ]; then
        echo "row: [status:warn] Swap: ${swap_used}MB / ${swap_total}MB (${swap_percent}%)"
    else
        echo "row: Swap: ${swap_used}MB / ${swap_total}MB (${swap_percent}%)"
    fi
else
    echo "row: Swap: [grey70]Not configured[/]"
fi

# Get available memory
available=$(free -m | awk 'NR==2{print $7}')
echo "row: Available: ${available}MB"

# Extended mode: detailed memory information
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [bold]Memory Details:[/]"

    # Buffer/cache breakdown
    buffers=$(free -m | awk 'NR==2{print $6}')
    cached=$(grep "^Cached:" /proc/meminfo | awk '{print int($2/1024)}')
    echo "row: [grey70]Buffers: ${buffers}MB | Cached: ${cached}MB[/]"

    # Shared memory
    shared=$(free -m | awk 'NR==2{print $5}')
    echo "row: [grey70]Shared: ${shared}MB[/]"

    # Detailed from /proc/meminfo
    echo "row: "
    echo "row: [bold]Detailed Breakdown:[/]"
    active=$(grep "^Active:" /proc/meminfo | awk '{print int($2/1024)}')
    inactive=$(grep "^Inactive:" /proc/meminfo | awk '{print int($2/1024)}')
    dirty=$(grep "^Dirty:" /proc/meminfo | awk '{print int($2/1024)}')
    slab=$(grep "^Slab:" /proc/meminfo | awk '{print int($2/1024)}')
    echo "row: [grey70]Active: ${active}MB | Inactive: ${inactive}MB[/]"
    echo "row: [grey70]Dirty: ${dirty}MB | Slab: ${slab}MB[/]"

    # Swap partitions
    if [ "$swap_total" -gt 0 ]; then
        echo "row: "
        echo "row: [bold]Swap Partitions:[/]"
        cat /proc/swaps 2>/dev/null | tail -n +2 | while read -r filename type size used priority; do
            size_mb=$((size / 1024))
            used_mb=$((used / 1024))
            echo "row: [grey70]$filename: ${used_mb}MB / ${size_mb}MB (priority: $priority)[/]"
        done
    fi

    # Top memory processes
    echo "row: "
    echo "row: [bold]Top 10 Memory Processes:[/]"
    ps aux --sort=-%mem 2>/dev/null | awk 'NR>1 && NR<=11 {
        cmd = $11
        gsub(/.*\//, "", cmd)
        if (length(cmd) > 25) cmd = substr(cmd, 1, 25)
        mem_mb = $6 / 1024
        printf "row: [grey70]%s: %.1fMB (%.1f%%)[/]\n", cmd, mem_mb, $4
    }'

    # Memory pressure (if available)
    if [ -f /proc/pressure/memory ]; then
        echo "row: "
        echo "row: [bold]Memory Pressure:[/]"
        pressure=$(cat /proc/pressure/memory | head -n1)
        echo "row: [grey70]$pressure[/]"
    fi

    # Huge pages (if configured)
    huge_total=$(grep "^HugePages_Total:" /proc/meminfo | awk '{print $2}')
    if [ "$huge_total" -gt 0 ]; then
        huge_free=$(grep "^HugePages_Free:" /proc/meminfo | awk '{print $2}')
        huge_size=$(grep "^Hugepagesize:" /proc/meminfo | awk '{print $2}')
        echo "row: "
        echo "row: [bold]Huge Pages:[/]"
        echo "row: [grey70]Total: $huge_total | Free: $huge_free | Size: ${huge_size}KB[/]"
    fi
fi

# Actions (context-based)
echo "action: [sudo,refresh] Drop caches:sh -c 'sync && echo 3 > /proc/sys/vm/drop_caches' && echo 'Caches dropped'"

if [ "$percent" -gt 90 ]; then
    # Get top memory process for kill action
    top_pid=$(ps aux --sort=-%mem 2>/dev/null | awk 'NR==2 {print $2}')
    top_cmd=$(ps aux --sort=-%mem 2>/dev/null | awk 'NR==2 {cmd=$11; gsub(/.*\//, "", cmd); print cmd}')
    if [ -n "$top_pid" ]; then
        echo "action: [sudo,danger,refresh] Kill ${top_cmd}:kill -9 ${top_pid}"
    fi
fi

if [ "$swap_percent" -gt 50 ]; then
    echo "action: [sudo,danger,refresh] Clear swap:swapoff -a && swapon -a"
fi

echo "action: View memory map:cat /proc/meminfo"
