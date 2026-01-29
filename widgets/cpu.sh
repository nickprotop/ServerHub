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

# Setup cache directory for historical data
CACHE_DIR="$HOME/.cache/serverhub"
mkdir -p "$CACHE_DIR"

# Get core count
cpu_cores=$(nproc 2>/dev/null || echo "1")

# Get load average
read -r load1 load5 load15 _ _ < /proc/loadavg

# Calculate load percentage (load / cores * 100)
load_percent=$(awk "BEGIN {printf \"%.0f\", ($load1 / $cpu_cores) * 100}")

# Cap at 100% for display
[ "$load_percent" -gt 100 ] && load_percent=100

# Determine sample count based on mode
if [ "$EXTENDED" = true ]; then
    MAX_SAMPLES=30
else
    MAX_SAMPLES=10
fi

# Store load history
load_history_file="$CACHE_DIR/cpu-load.txt"
echo "$load_percent" >> "$load_history_file"
tail -n "$MAX_SAMPLES" "$load_history_file" > "${load_history_file}.tmp" 2>/dev/null
mv "${load_history_file}.tmp" "$load_history_file" 2>/dev/null

# Read history for sparkline
if [ -f "$load_history_file" ] && [ -s "$load_history_file" ]; then
    load_history=$(paste -sd',' "$load_history_file")
else
    load_history="$load_percent"
fi

# Determine status based on load
if [ "$load_percent" -lt 70 ]; then
    status="ok"
elif [ "$load_percent" -lt 90 ]; then
    status="warn"
else
    status="error"
fi

# Dashboard mode: Compact overview with sparklines
if [ "$EXTENDED" = false ]; then
    echo "row: [status:$status] Load: ${load1} / ${cpu_cores} cores [sparkline:${load_history}:green]"
    echo "row: [progress:${load_percent}]"
    echo "row: "
    echo "row: Load Average: ${load1}, ${load5}, ${load15}"

    # Quick CPU info
    if [ -f /proc/cpuinfo ]; then
        cpu_model=$(grep -m1 "model name" /proc/cpuinfo | cut -d':' -f2 | xargs | cut -c1-50)
        if [ -n "$cpu_model" ]; then
            echo "row: "
            echo "row: [grey70]${cpu_model}[/]"
        fi
    fi
else
    # Extended mode: Detailed view with graphs and tables
    echo "row: [status:$status] Load: ${load1} / ${cpu_cores} cores"
    echo "row: [progress:${load_percent}]"
    echo "row: "
    echo "row: Load Average: ${load1} (1m), ${load5} (5m), ${load15} (15m)"

    # Full CPU info
    if [ -f /proc/cpuinfo ]; then
        cpu_model=$(grep -m1 "model name" /proc/cpuinfo | cut -d':' -f2 | xargs)
        if [ -n "$cpu_model" ]; then
            echo "row: [grey70]Model: ${cpu_model}[/]"
        fi
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

    # Load history graph
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Load History (last 60s):[/]"
    echo "row: [graph:${load_history}:cyan1:Load %]"

    # Per-core detailed table
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Per-Core Usage:[/]"
    echo "[table:Core|Usage|User|System|Idle|I/O Wait]"

    core_num=0
    grep "^cpu[0-9]" /proc/stat | while read -r line; do
        read -r _ user nice system idle iowait irq softirq _ <<< "$line"
        total=$((user + nice + system + idle + iowait + irq + softirq))
        if [ "$total" -gt 0 ]; then
            busy=$((user + nice + system))
            usage=$((busy * 100 / total))
            user_pct=$(((user + nice) * 100 / total))
            sys_pct=$((system * 100 / total))
            idle_pct=$((idle * 100 / total))
            iowait_pct=$((iowait * 100 / total))

            echo "[tablerow:Core $core_num|[miniprogress:${usage}:10]|${user_pct}%|${sys_pct}%|${idle_pct}%|${iowait_pct}%]"
        fi
        ((core_num++))
    done

    # CPU time breakdown summary
    if [ -f /proc/stat ]; then
        read -r _ user nice system idle iowait irq softirq _ < /proc/stat
        total=$((user + nice + system + idle + iowait + irq + softirq))
        if [ "$total" -gt 0 ]; then
            user_pct=$((user * 100 / total))
            sys_pct=$((system * 100 / total))
            idle_pct=$((idle * 100 / total))
            iowait_pct=$((iowait * 100 / total))

            echo "row: "
            echo "row: [divider]"
            echo "row: "
            echo "row: [bold]CPU Time Breakdown:[/]"
            echo "[table:Type|Percentage]"
            echo "[tablerow:User|[miniprogress:${user_pct}:15]]"
            echo "[tablerow:System|[miniprogress:${sys_pct}:15]]"
            echo "[tablerow:Idle|[miniprogress:${idle_pct}:15]]"
            echo "[tablerow:I/O Wait|[miniprogress:${iowait_pct}:15]]"
        fi
    fi

    # Top CPU processes
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Top CPU Processes:[/]"
    echo "[table:Process|CPU %|Memory %|PID]"

    ps aux --sort=-%cpu 2>/dev/null | awk 'NR>1 && NR<=11 {
        cmd = $11
        gsub(/.*\//, "", cmd)
        if (length(cmd) > 20) cmd = substr(cmd, 1, 20)
        cpu_val = int($3 + 0.5)
        mem_val = int($4 + 0.5)
        printf "[tablerow:%s|%.1f%%|%.1f%%|%s]\n", cmd, $3, $4, $2
    }'

    # Context switches and interrupts
    if [ -f /proc/stat ]; then
        echo "row: "
        echo "row: [divider:─:cyan1]"
        echo "row: "
        echo "row: [bold]System Metrics:[/]"

        ctxt=$(grep "^ctxt" /proc/stat | awk '{print $2}')
        intr=$(grep "^intr" /proc/stat | awk '{print $2}')
        procs_running=$(grep "^procs_running" /proc/stat | awk '{print $2}')
        procs_blocked=$(grep "^procs_blocked" /proc/stat | awk '{print $2}')

        # Format large numbers with commas
        ctxt_fmt=$(echo "$ctxt" | sed ':a;s/\B[0-9]\{3\}\>/,&/;ta')
        intr_fmt=$(echo "$intr" | sed ':a;s/\B[0-9]\{3\}\>/,&/;ta')

        echo "row: [grey70]Context switches: ${ctxt_fmt}[/]"
        echo "row: [grey70]Interrupts: ${intr_fmt}[/]"
        if [ -n "$procs_running" ]; then
            echo "row: [grey70]Processes running: ${procs_running}[/]"
        fi
        if [ -n "$procs_blocked" ]; then
            echo "row: [grey70]Processes blocked: ${procs_blocked}[/]"
        fi
    fi
fi

# Actions (context-based on load)
if [ "$load_percent" -gt 80 ]; then
    # Get top CPU process for kill action
    top_pid=$(ps aux --sort=-%cpu 2>/dev/null | awk 'NR==2 {print $2}')
    top_cmd=$(ps aux --sort=-%cpu 2>/dev/null | awk 'NR==2 {cmd=$11; gsub(/.*\//, "", cmd); print cmd}')
    if [ -n "$top_pid" ]; then
        echo "action: [sudo,danger,refresh] Kill ${top_cmd} (${top_pid}):kill -9 ${top_pid}"
    fi
    echo "action: [sudo] Renice top process:renice +10 ${top_pid}"
fi

echo "action: View all processes:ps aux --sort=-%cpu | head -30"
echo "action: Top processes (interactive):top -bn1 | head -20"
echo "action: CPU info:lscpu 2>/dev/null || cat /proc/cpuinfo"
echo "action: Clear load history:rm -f $CACHE_DIR/cpu-load.txt"
