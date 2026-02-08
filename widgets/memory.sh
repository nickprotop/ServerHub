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

# Get swap info
swap_info=$(free -m | awk 'NR==3{if($2>0) printf "%.0f %.0f %.0f", $3,$2,$3*100/$2; else print "0 0 0"}')
read -r swap_used swap_total swap_percent <<< "$swap_info"

# Get available memory and cache
available=$(free -m | awk 'NR==2{print $7}')
buffers=$(free -m | awk 'NR==2{print $6}')
cached=$(grep "^Cached:" /proc/meminfo | awk '{print int($2/1024)}')

# Store data in storage system
echo "datastore: memory_usage value=$percent"
if [ "$swap_total" -gt 0 ]; then
    echo "datastore: swap_usage value=$swap_percent"
fi

# Determine status
if [ "$percent" -lt 70 ]; then
    status="ok"
elif [ "$percent" -lt 90 ]; then
    status="warn"
else
    status="error"
fi

# Dashboard mode: Compact overview
if [ "$EXTENDED" = false ]; then
    echo "row: [status:$status] Memory: ${used}MB / ${total}MB"
    echo "row: [progress:${percent}:warm]"
    echo "row: "

    # Memory breakdown table with gradients
    echo "row: [bold]Memory Breakdown:[/]"
    echo "[table:Type|Usage]"
    echo "[tablerow:RAM Used|[miniprogress:${percent}:12:warm]]"

    if [ "$swap_total" -gt 0 ]; then
        echo "[tablerow:Swap Used|[miniprogress:${swap_percent}:12:cool]]"
    else
        echo "[tablerow:Swap|[grey70]Not configured[/]]"
    fi

    # Calculate cache percentage
    cache_mb=$((buffers + cached))
    cache_percent=$((cache_mb * 100 / total))
    avail_percent=$((available * 100 / total))

    echo "[tablerow:Cache|${cache_mb}MB]"
    echo "[tablerow:Available|${available}MB]"

    # Quick memory info
    echo "row: "
    echo "row: [grey70]Available: ${available}MB (${avail_percent}%)[/]"
else
    # Extended mode: Detailed view with graphs and tables
    echo "row: [status:$status] Memory: ${used}MB / ${total}MB (${percent}%)"
    echo "row: [progress:${percent}:warm]"
    echo "row: "
    echo "row: Available: ${available}MB"

    # Memory history graph with gradient
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Memory Usage History (last 60s):[/]"
    # Use history_graph with last 30 samples, warm gradient, 0-100 scale, 40 char width
    echo "row: [history_graph:memory_usage.value:last_40:warm:Memory %:0-100:40]"

    # Swap graph if configured with gradient
    if [ "$swap_total" -gt 0 ]; then
        echo "row: "
        echo "row: [bold]Swap Usage History:[/]"
        # Use history_graph with last 30 samples, warm gradient, 0-100 scale, 40 char width
        echo "row: [history_graph:swap_usage.value:last_40:warm:Swap %:0-100:40]"
    fi

    # Enhanced memory trend visualization
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Memory Usage Trend (Last 1 Hour):[/]"
    echo "row: [history_line:memory_usage.value:1h:warm:Memory %:0-100:80:10:braille]"

    # Detailed breakdown table with gradients
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Memory Breakdown:[/]"
    echo "[table:Type|Size|Percentage]"

    # RAM breakdown
    echo "[tablerow:Total RAM|${total}MB|100%]"
    echo "[tablerow:Used|${used}MB|${percent}%]"
    echo "[tablerow:Available|${available}MB|$((available * 100 / total))%]"
    echo "[tablerow:Buffers|${buffers}MB|$((buffers * 100 / total))%]"
    echo "[tablerow:Cache|${cached}MB|$((cached * 100 / total))%]"

    # Swap breakdown
    if [ "$swap_total" -gt 0 ]; then
        swap_avail=$((swap_total - swap_used))
        echo "[tablerow:Swap Total|${swap_total}MB|100%]"
        echo "[tablerow:Swap Used|${swap_used}MB|${swap_percent}%]"
        echo "[tablerow:Swap Free|${swap_avail}MB|$((swap_avail * 100 / swap_total))%]"
    fi

    # Advanced memory details
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Advanced Details:[/]"
    echo "[table:Metric|Value]"

    active=$(grep "^Active:" /proc/meminfo | awk '{print int($2/1024)}')
    inactive=$(grep "^Inactive:" /proc/meminfo | awk '{print int($2/1024)}')
    dirty=$(grep "^Dirty:" /proc/meminfo | awk '{print int($2/1024)}')
    slab=$(grep "^Slab:" /proc/meminfo | awk '{print int($2/1024)}')
    shared=$(free -m | awk 'NR==2{print $5}')

    echo "[tablerow:Active|${active}MB]"
    echo "[tablerow:Inactive|${inactive}MB]"
    echo "[tablerow:Shared|${shared}MB]"
    echo "[tablerow:Dirty|${dirty}MB]"
    echo "[tablerow:Slab (kernel)|${slab}MB]"

    # Swap partitions
    if [ "$swap_total" -gt 0 ]; then
        echo "row: "
        echo "row: [divider]"
        echo "row: "
        echo "row: [bold]Swap Partitions:[/]"
        echo "[table:Device|Type|Size|Used|Priority]"

        cat /proc/swaps 2>/dev/null | tail -n +2 | while read -r filename type size used priority; do
            size_mb=$((size / 1024))
            used_mb=$((used / 1024))
            used_pct=$((used * 100 / size))
            dev_name=$(basename "$filename")
            echo "[tablerow:${dev_name}|${type}|${size_mb}MB|${used_mb}MB|${priority}]"
        done
    fi

    # Top memory processes
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Top Memory Processes:[/]"
    echo "[table:Process|Memory|Percent|PID]"

    ps aux --sort=-%mem 2>/dev/null | awk 'NR>1 && NR<=11 {
        cmd = $11
        gsub(/.*\//, "", cmd)
        if (length(cmd) > 20) cmd = substr(cmd, 1, 20)
        mem_mb = $6 / 1024
        printf "[tablerow:%s|%.0fMB|%.1f%%|%s]\n", cmd, mem_mb, $4, $2
    }'

    # Memory pressure (if available)
    if [ -f /proc/pressure/memory ]; then
        echo "row: "
        echo "row: [divider]"
        echo "row: "
        echo "row: [bold]Memory Pressure:[/]"

        # Parse pressure data
        some_avg10=$(grep "^some" /proc/pressure/memory | awk -F'avg10=' '{print $2}' | awk '{printf "%.2f", $1}')
        some_avg60=$(grep "^some" /proc/pressure/memory | awk -F'avg60=' '{print $2}' | awk '{printf "%.2f", $1}')
        full_avg10=$(grep "^full" /proc/pressure/memory | awk -F'avg10=' '{print $2}' | awk '{printf "%.2f", $1}')
        full_avg60=$(grep "^full" /proc/pressure/memory | awk -F'avg60=' '{print $2}' | awk '{printf "%.2f", $1}')

        echo "[table:Type|10s avg|60s avg]"
        echo "[tablerow:Some stall|${some_avg10}%|${some_avg60}%]"
        echo "[tablerow:Full stall|${full_avg10}%|${full_avg60}%]"
    fi

    # Huge pages (if configured)
    huge_total=$(grep "^HugePages_Total:" /proc/meminfo | awk '{print $2}')
    if [ "$huge_total" -gt 0 ]; then
        huge_free=$(grep "^HugePages_Free:" /proc/meminfo | awk '{print $2}')
        huge_rsvd=$(grep "^HugePages_Rsvd:" /proc/meminfo | awk '{print $2}')
        huge_size=$(grep "^Hugepagesize:" /proc/meminfo | awk '{print $2}')

        echo "row: "
        echo "row: [divider:─:cyan1]"
        echo "row: "
        echo "row: [bold]Huge Pages:[/]"
        echo "[table:Metric|Value]"
        echo "[tablerow:Total|$huge_total]"
        echo "[tablerow:Free|$huge_free]"
        echo "[tablerow:Reserved|$huge_rsvd]"
        echo "[tablerow:Page Size|${huge_size}KB]"
    fi
fi

# Actions (context-based)
if [ "$percent" -gt 90 ]; then
    # Get top memory process for kill action
    top_pid=$(ps aux --sort=-%mem 2>/dev/null | awk 'NR==2 {print $2}')
    top_cmd=$(ps aux --sort=-%mem 2>/dev/null | awk 'NR==2 {cmd=$11; gsub(/.*\//, "", cmd); print cmd}')
    if [ -n "$top_pid" ]; then
        echo "action: [sudo,danger,refresh] Kill ${top_cmd} (${top_pid}):kill -9 ${top_pid}"
    fi
fi

if [ "$swap_total" -gt 0 ] && [ "$swap_percent" -gt 50 ]; then
    echo "action: [sudo,danger,refresh] Clear swap:swapoff -a && swapon -a"
fi

echo "action: [sudo,refresh] Drop caches:sh -c 'sync && echo 3 > /proc/sys/vm/drop_caches' && echo 'Caches dropped'"
echo "action: View memory map:cat /proc/meminfo"
echo "action: Show OOM killer history:dmesg | grep -i 'killed process' | tail -10"
