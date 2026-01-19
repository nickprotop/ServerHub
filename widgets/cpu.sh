#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

echo "title: CPU Usage"
echo "refresh: 2"

# Get core count
cpu_cores=$(nproc 2>/dev/null || echo "1")

# Get load average (instant, no blocking)
read -r load1 load5 load15 _ _ < /proc/loadavg

# Calculate load percentage (load / cores * 100)
load_percent=$(awk "BEGIN {printf \"%.0f\", ($load1 / $cpu_cores) * 100}")

# Cap at 100% for display
[ "$load_percent" -gt 100 ] && load_percent=100

# Determine status based on load
if [ "$load_percent" -lt 70 ]; then
    status="ok"
elif [ "$load_percent" -lt 90 ]; then
    status="warn"
else
    status="error"
fi

echo "row: [status:$status] Load: ${load1} / ${cpu_cores} cores (${load_percent}%)"
echo "row: [progress:${load_percent}:inline]"

# Load averages
echo "row: Load Average: ${load1}, ${load5}, ${load15}"

# Quick CPU info from /proc/cpuinfo (no subprocess)
if [ -f /proc/cpuinfo ]; then
    cpu_model=$(grep -m1 "model name" /proc/cpuinfo | cut -d':' -f2 | xargs)
    if [ -n "$cpu_model" ]; then
        # Truncate long model names for standard mode
        cpu_model_short=$(echo "$cpu_model" | cut -c1-40)
        echo "row: [grey70]${cpu_model_short}[/]"
    fi
fi

# Extended mode: detailed CPU information
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [bold]CPU Details:[/]"

    # Full model name
    if [ -n "$cpu_model" ]; then
        echo "row: [grey70]Model: ${cpu_model}[/]"
    fi
    echo "row: [grey70]Cores: ${cpu_cores}[/]"

    # CPU frequency
    if [ -f /sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq ]; then
        cur_freq=$(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq 2>/dev/null)
        min_freq=$(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_min_freq 2>/dev/null)
        max_freq=$(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_max_freq 2>/dev/null)
        if [ -n "$cur_freq" ]; then
            cur_mhz=$((cur_freq / 1000))
            min_mhz=$((min_freq / 1000))
            max_mhz=$((max_freq / 1000))
            echo "row: [grey70]Frequency: ${cur_mhz} MHz (${min_mhz}-${max_mhz})[/]"
        fi
    fi

    # CPU time breakdown from /proc/stat
    if [ -f /proc/stat ]; then
        read -r _ user nice system idle iowait irq softirq _ < /proc/stat
        total=$((user + nice + system + idle + iowait + irq + softirq))
        if [ "$total" -gt 0 ]; then
            user_pct=$((user * 100 / total))
            sys_pct=$((system * 100 / total))
            idle_pct=$((idle * 100 / total))
            iowait_pct=$((iowait * 100 / total))
            echo "row: "
            echo "row: [bold]CPU Time:[/]"
            echo "row: [grey70]User: ${user_pct}% | System: ${sys_pct}% | Idle: ${idle_pct}% | I/O Wait: ${iowait_pct}%[/]"
        fi
    fi

    # Per-core load (from /proc/stat)
    echo "row: "
    echo "row: [bold]Per-Core Load:[/]"
    core_num=0
    grep "^cpu[0-9]" /proc/stat | head -n 8 | while read -r line; do
        read -r _ user nice system idle iowait _ <<< "$line"
        total=$((user + nice + system + idle + iowait))
        if [ "$total" -gt 0 ]; then
            busy=$((user + nice + system))
            usage=$((busy * 100 / total))
            if [ "$usage" -gt 80 ]; then
                core_status="error"
            elif [ "$usage" -gt 60 ]; then
                core_status="warn"
            else
                core_status="ok"
            fi
            echo "row: [status:$core_status] Core $core_num: ${usage}%"
        fi
        ((core_num++))
    done

    # Top CPU processes (can be slower in extended mode)
    echo "row: "
    echo "row: [bold]Top CPU Processes:[/]"
    ps aux --sort=-%cpu 2>/dev/null | awk 'NR>1 && NR<=11 {
        cmd = $11
        gsub(/.*\//, "", cmd)
        if (length(cmd) > 25) cmd = substr(cmd, 1, 25)
        printf "row: [grey70]%s: %.1f%% (PID %s)[/]\n", cmd, $3, $2
    }'

    # Context switches and interrupts
    if [ -f /proc/stat ]; then
        ctxt=$(grep "^ctxt" /proc/stat | awk '{print $2}')
        intr=$(grep "^intr" /proc/stat | awk '{print $2}')
        echo "row: "
        echo "row: [grey70]Context switches: ${ctxt}[/]"
        echo "row: [grey70]Interrupts: ${intr}[/]"
    fi
fi

# Actions
if [ "$load_percent" -gt 80 ]; then
    # Get top CPU process for kill action
    top_pid=$(ps aux --sort=-%cpu 2>/dev/null | awk 'NR==2 {print $2}')
    top_cmd=$(ps aux --sort=-%cpu 2>/dev/null | awk 'NR==2 {cmd=$11; gsub(/.*\//, "", cmd); print cmd}')
    if [ -n "$top_pid" ]; then
        echo "action: [sudo,danger,refresh] Kill ${top_cmd} (${top_pid}):kill -9 ${top_pid}"
    fi
fi
echo "action: View all processes:ps aux --sort=-%cpu | head -20"
