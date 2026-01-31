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

# Store load history (clear if stale)
load_history_file="$CACHE_DIR/cpu-load.txt"
last_run_file="$CACHE_DIR/cpu-last-run.txt"

# Check for stale data based on last run timestamp
current_time=$(date +%s)
if [ -f "$last_run_file" ]; then
    read -r last_time < "$last_run_file"
    time_diff=$((current_time - last_time))
    # If gap > 6 seconds (3x refresh interval), clear history
    if [ "$time_diff" -gt 6 ]; then
        rm -f "$load_history_file"
    fi
fi
echo "$current_time" > "$last_run_file"

echo "$load_percent" >> "$load_history_file"
tail -n "$MAX_SAMPLES" "$load_history_file" > "${load_history_file}.tmp" 2>/dev/null
mv "${load_history_file}.tmp" "$load_history_file" 2>/dev/null

# Read history for sparkline/graph
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

# Dashboard mode: Compact overview
if [ "$EXTENDED" = false ]; then
    # Use gradient progress bar for btop-style appearance
    echo "row: [status:$status] Load: ${load1} / ${cpu_cores} cores"
    echo "row: [progress:${load_percent}:warm]"
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
    echo "row: [progress:${load_percent}:warm]"
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

    # Load history graph with gradient
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Load History (last 60s):[/]"
    # Use 0-100 fixed scale for percentage graph
    echo "row: [graph:${load_history}:cool:Load %:0-100]"

    # Per-core detailed table (need delta calculation)
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Per-Core Usage:[/]"
    echo "[table:Core|Usage|User|System|Idle|I/O Wait]"

    # First sample (both aggregate and per-core)
    cpu_agg1=$(grep "^cpu " /proc/stat)
    stat1=$(grep "^cpu[0-9]" /proc/stat)

    # Wait for delta (1 second for accurate measurement)
    sleep 1

    # Second sample (both aggregate and per-core)
    cpu_agg2=$(grep "^cpu " /proc/stat)
    stat2=$(grep "^cpu[0-9]" /proc/stat)

    # Process each core
    core_num=0
    while IFS= read -r line1 && IFS= read -r line2 <&3; do
        read -r _ user1 nice1 system1 idle1 iowait1 irq1 softirq1 _ <<< "$line1"
        read -r _ user2 nice2 system2 idle2 iowait2 irq2 softirq2 _ <<< "$line2"

        user_delta=$((user2 - user1))
        nice_delta=$((nice2 - nice1))
        system_delta=$((system2 - system1))
        idle_delta=$((idle2 - idle1))
        iowait_delta=$((iowait2 - iowait1))
        irq_delta=$((irq2 - irq1))
        softirq_delta=$((softirq2 - softirq1))

        total_delta=$((user_delta + nice_delta + system_delta + idle_delta + iowait_delta + irq_delta + softirq_delta))

        if [ "$total_delta" -gt 0 ]; then
            busy_delta=$((user_delta + nice_delta + system_delta))
            usage=$((busy_delta * 100 / total_delta))
            user_pct=$(((user_delta + nice_delta) * 100 / total_delta))
            sys_pct=$((system_delta * 100 / total_delta))
            idle_pct=$((idle_delta * 100 / total_delta))
            iowait_pct=$((iowait_delta * 100 / total_delta))

            echo "[tablerow:Core $core_num|[miniprogress:${usage}:10:warm]|${user_pct}%|${sys_pct}%|${idle_pct}%|${iowait_pct}%]"
        fi
        ((core_num++))
    done <<< "$stat1" 3<<< "$stat2"

    # CPU time breakdown summary (uses same samples as per-core to avoid extra delay)
    if [ -n "$cpu_agg1" ] && [ -n "$cpu_agg2" ]; then
        # Parse aggregate CPU line from the samples we already took
        read -r _ user1 nice1 system1 idle1 iowait1 irq1 softirq1 _ <<< "$cpu_agg1"
        read -r _ user2 nice2 system2 idle2 iowait2 irq2 softirq2 _ <<< "$cpu_agg2"

        # Calculate deltas
        user_delta=$((user2 - user1))
        nice_delta=$((nice2 - nice1))
        system_delta=$((system2 - system1))
        idle_delta=$((idle2 - idle1))
        iowait_delta=$((iowait2 - iowait1))
        irq_delta=$((irq2 - irq1))
        softirq_delta=$((softirq2 - softirq1))

        total_delta=$((user_delta + nice_delta + system_delta + idle_delta + iowait_delta + irq_delta + softirq_delta))

        if [ "$total_delta" -gt 0 ]; then
            user_pct=$(((user_delta + nice_delta) * 100 / total_delta))
            sys_pct=$((system_delta * 100 / total_delta))
            idle_pct=$((idle_delta * 100 / total_delta))
            iowait_pct=$((iowait_delta * 100 / total_delta))

            echo "row: "
            echo "row: [divider]"
            echo "row: "
            echo "row: [bold]CPU Time Breakdown:[/]"
            echo "[table:Type|Percentage]"
            echo "[tablerow:User|[miniprogress:${user_pct}:15:spectrum]]"
            echo "[tablerow:System|[miniprogress:${sys_pct}:15:spectrum]]"
            echo "[tablerow:Idle|[miniprogress:${idle_pct}:15:cool]]"
            echo "[tablerow:I/O Wait|[miniprogress:${iowait_pct}:15:warm]]"
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
