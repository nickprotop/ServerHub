#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

echo "title: Top Processes"
echo "refresh: 3"

# Get all process info in one call
all_procs=$(ps aux --sort=-%cpu 2>/dev/null)

# Total process count
total_procs=$(echo "$all_procs" | wc -l)
((total_procs--))  # Subtract header line

# Count zombies
zombies=$(echo "$all_procs" | awk '$8=="Z" || $8~/^Z/' | wc -l)

# Process state breakdown
running=$(echo "$all_procs" | awk '$8~/^R/' | wc -l)
sleeping=$(echo "$all_procs" | awk '$8~/^S/' | wc -l)
stopped=$(echo "$all_procs" | awk '$8~/^T/' | wc -l)

# Dashboard mode: Compact overview with tables
if [ "$EXTENDED" = false ]; then
    if [ "$zombies" -gt 0 ]; then
        echo "row: [status:warn] Total: [cyan1]${total_procs}[/] | Zombies: [yellow]${zombies}[/]"
    else
        echo "row: [status:ok] Total processes: [cyan1]${total_procs}[/]"
    fi
    echo "row: "

    # Top 5 by CPU
    echo "row: [bold]Top 5 by CPU:[/]"
    echo "[table:Process|CPU|Memory|PID]"
    echo "$all_procs" | awk 'NR>1 && NR<=6 {
        cmd = $11
        gsub(/.*\//, "", cmd)
        if (length(cmd) > 18) cmd = substr(cmd, 1, 18)
        cpu = int($3 + 0.5)
        mem = int($4 + 0.5)
        printf "[tablerow:%s|[miniprogress:%d:8]|[miniprogress:%d:8]|%s]\n", cmd, cpu, mem, $2
    }'

    echo "row: "

    # Top 5 by Memory
    echo "row: [bold]Top 5 by Memory:[/]"
    echo "[table:Process|Memory|CPU|PID]"
    echo "$all_procs" | sort -k4 -rn | awk 'NR>1 && NR<=6 {
        cmd = $11
        gsub(/.*\//, "", cmd)
        if (length(cmd) > 18) cmd = substr(cmd, 1, 18)
        cpu = int($3 + 0.5)
        mem = int($4 + 0.5)
        printf "[tablerow:%s|[miniprogress:%d:8]|[miniprogress:%d:8]|%s]\n", cmd, mem, cpu, $2
    }'

    # Process states summary
    echo "row: "
    echo "row: [grey70]Running: ${running} | Sleeping: ${sleeping} | Stopped: ${stopped}[/]"
else
    # Extended mode: Detailed view with comprehensive tables
    if [ "$zombies" -gt 0 ]; then
        echo "row: [status:warn] Total: [cyan1]${total_procs}[/] | Zombies: [yellow]${zombies}[/]"
    else
        echo "row: [status:ok] Total processes: [cyan1]${total_procs}[/]"
    fi
    echo "row: "

    # Combined top processes table (top 15)
    echo "row: [bold]Top Processes (by CPU):[/]"
    echo "[table:Process|CPU|Memory|User|PID]"
    echo "$all_procs" | awk 'NR>1 && NR<=16 {
        cmd = $11
        gsub(/.*\//, "", cmd)
        if (length(cmd) > 20) cmd = substr(cmd, 1, 20)
        user = $1
        if (length(user) > 12) user = substr(user, 1, 12)
        cpu = int($3 + 0.5)
        mem = int($4 + 0.5)
        printf "[tablerow:%s|[miniprogress:%d:8]|[miniprogress:%d:8]|%s|%s]\n", cmd, cpu, mem, user, $2
    }'

    # Process state breakdown
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Process States:[/]"
    echo "[table:State|Count]"
    echo "[tablerow:Running|${running}]"
    echo "[tablerow:Sleeping|${sleeping}]"
    echo "[tablerow:Stopped|${stopped}]"
    if [ "$zombies" -gt 0 ]; then
        echo "[tablerow:Zombie|[yellow]${zombies}[/]]"
    else
        echo "[tablerow:Zombie|0]"
    fi

    # Users with most processes
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Processes by User (Top 10):[/]"
    echo "[table:User|Count]"
    echo "$all_procs" | awk 'NR>1 {print $1}' | sort | uniq -c | sort -rn | head -n 10 | while read -r count user; do
        echo "[tablerow:${user}|${count}]"
    done

    # Top processes by memory
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Top Memory Consumers:[/]"
    echo "[table:Process|Memory|CPU|User|PID]"
    echo "$all_procs" | sort -k4 -rn | awk 'NR>1 && NR<=16 {
        cmd = $11
        gsub(/.*\//, "", cmd)
        if (length(cmd) > 20) cmd = substr(cmd, 1, 20)
        user = $1
        if (length(user) > 12) user = substr(user, 1, 12)
        cpu = int($3 + 0.5)
        mem = int($4 + 0.5)
        printf "[tablerow:%s|[miniprogress:%d:8]|[miniprogress:%d:8]|%s|%s]\n", cmd, mem, cpu, user, $2
    }'

    # Zombie process details
    if [ "$zombies" -gt 0 ]; then
        echo "row: "
        echo "row: [divider]"
        echo "row: "
        echo "row: [bold]Zombie Processes:[/]"
        echo "[table:PID|Process|Parent PID]"
        ps -eo pid,ppid,state,comm 2>/dev/null | awk '$3=="Z" || $3~/^Z/ {
            pid = $1
            ppid = $2
            cmd = $4
            # Escape brackets in process names to prevent markup conflicts
            gsub(/\[/, "⦗", cmd)
            gsub(/\]/, "⦘", cmd)
            if (length(cmd) > 25) cmd = substr(cmd, 1, 25)
            printf "[tablerow:%s|[yellow]%s[/]|%s]\n", pid, cmd, ppid
        }' | head -n 10
    fi

    # Process statistics
    echo "row: "
    echo "row: [divider:─:cyan1]"
    echo "row: "
    echo "row: [bold]Statistics:[/]"

    # Calculate average CPU and memory
    avg_cpu=$(echo "$all_procs" | awk 'NR>1 {sum+=$3; count++} END {if(count>0) printf "%.2f", sum/count; else print "0"}')
    avg_mem=$(echo "$all_procs" | awk 'NR>1 {sum+=$4; count++} END {if(count>0) printf "%.2f", sum/count; else print "0"}')

    # Count threads (with timeout to prevent hanging)
    total_threads=$(timeout 2 ps -eLf 2>/dev/null | wc -l)
    if [ -n "$total_threads" ] && [ "$total_threads" -gt 0 ]; then
        ((total_threads--))  # Subtract header
    else
        total_threads="N/A"
    fi

    echo "row: [grey70]Average CPU per process: ${avg_cpu}%[/]"
    echo "row: [grey70]Average Memory per process: ${avg_mem}%[/]"
    if [ "$total_threads" != "N/A" ]; then
        echo "row: [grey70]Total threads: ${total_threads}[/]"
    fi

    # Process tree preview (with timeout to prevent hanging)
    if command -v pstree &> /dev/null; then
        tree_output=$(timeout 2 pstree -p -a -A 2>/dev/null | head -n 15)
        if [ -n "$tree_output" ]; then
            echo "row: "
            echo "row: [divider]"
            echo "row: "
            echo "row: [bold]Process Tree (top levels):[/]"
            echo "$tree_output" | while read -r line; do
                # Truncate long lines
                line_short=$(echo "$line" | cut -c1-60)
                echo "row: [grey70]${line_short}[/]"
            done
        fi
    fi
fi

# Actions (context-based)
# Get top CPU process info for kill action
top_cpu_pid=$(echo "$all_procs" | awk 'NR==2 {print $2}')
top_cpu_cmd=$(echo "$all_procs" | awk 'NR==2 {cmd=$11; gsub(/.*\//, "", cmd); print cmd}')
top_cpu_pct=$(echo "$all_procs" | awk 'NR==2 {print $3}')

# Get top memory process info
top_mem_line=$(echo "$all_procs" | sort -k4 -rn | awk 'NR==2')
top_mem_pid=$(echo "$top_mem_line" | awk '{print $2}')
top_mem_cmd=$(echo "$top_mem_line" | awk '{cmd=$11; gsub(/.*\//, "", cmd); print cmd}')
top_mem_pct=$(echo "$top_mem_line" | awk '{print $4}')

# Show kill actions only if resource usage is high
if awk "BEGIN {exit !($top_cpu_pct > 50)}"; then
    echo "action: [sudo,danger,refresh] Kill $top_cpu_cmd (CPU ${top_cpu_pct}%):kill -9 $top_cpu_pid"
fi

if awk "BEGIN {exit !($top_mem_pct > 10)}"; then
    echo "action: [sudo,danger,refresh] Kill $top_mem_cmd (MEM ${top_mem_pct}%):kill -9 $top_mem_pid"
fi

# Zombie cleanup
if [ "$zombies" -gt 0 ]; then
    echo "action: [sudo,danger,refresh] Kill zombie parents:kill -9 \$(ps aux | awk '\$8==\"Z\" {print \$3}' | sort -u)"
fi

# General actions
echo "action: View process tree:ps auxf | head -50"
echo "action: View all processes:ps aux | head -50"
echo "action: Show threads:ps -eLf | head -50"
echo "action: View open files:lsof 2>/dev/null | head -50"
