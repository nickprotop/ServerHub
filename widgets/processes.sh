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

echo "row: Total processes: [cyan1]$total_procs[/]"
echo "row: "

# Top 5 processes by CPU (using awk for comparisons instead of bc)
echo "row: [bold]Top 5 by CPU:[/]"
echo "$all_procs" | awk 'NR>1 && NR<=6 {
    cmd = $11
    gsub(/.*\//, "", cmd)
    if (length(cmd) > 20) cmd = substr(cmd, 1, 20)
    cpu = $3
    pid = $2

    if (cpu > 50) status = "error"
    else if (cpu > 20) status = "warn"
    else status = "ok"

    printf "row: [status:%s] %s: [yellow]%.1f%%[/] [grey70](PID: %s)[/]\n", status, cmd, cpu, pid
}'

echo "row: "

# Top 5 processes by Memory (re-sort by memory)
echo "row: [bold]Top 5 by Memory:[/]"
echo "$all_procs" | sort -k4 -rn | awk 'NR>1 && NR<=6 {
    cmd = $11
    gsub(/.*\//, "", cmd)
    if (length(cmd) > 20) cmd = substr(cmd, 1, 20)
    mem = $4
    pid = $2

    if (mem > 10) status = "warn"
    else status = "ok"

    printf "row: [status:%s] %s: [cyan1]%.1f%%[/] [grey70](PID: %s)[/]\n", status, cmd, mem, pid
}'

# Zombie processes
zombies=$(echo "$all_procs" | awk '$8=="Z" || $8~/^Z/' | wc -l)
if [ "$zombies" -gt 0 ]; then
    echo "row: "
    echo "row: [status:warn] Zombie processes: [yellow]$zombies[/]"
fi

# Extended mode: more detailed process information
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [bold]Top 20 Processes by CPU:[/]"
    echo "$all_procs" | awk 'NR>1 && NR<=21 {
        cmd = $11
        gsub(/.*\//, "", cmd)
        if (length(cmd) > 25) cmd = substr(cmd, 1, 25)
        printf "row: [grey70]%s: CPU %.1f%% MEM %.1f%% (PID %s)[/]\n", cmd, $3, $4, $2
    }'

    echo "row: "
    echo "row: [bold]Top 20 Processes by Memory:[/]"
    echo "$all_procs" | sort -k4 -rn | awk 'NR>1 && NR<=21 {
        cmd = $11
        gsub(/.*\//, "", cmd)
        if (length(cmd) > 25) cmd = substr(cmd, 1, 25)
        printf "row: [grey70]%s: MEM %.1f%% CPU %.1f%% (PID %s)[/]\n", cmd, $4, $3, $2
    }'

    # Process state breakdown
    echo "row: "
    echo "row: [bold]Process States:[/]"
    running=$(echo "$all_procs" | awk '$8~/^R/' | wc -l)
    sleeping=$(echo "$all_procs" | awk '$8~/^S/' | wc -l)
    stopped=$(echo "$all_procs" | awk '$8~/^T/' | wc -l)
    echo "row: [grey70]Running: $running | Sleeping: $sleeping | Stopped: $stopped | Zombie: $zombies[/]"

    # Users with most processes
    echo "row: "
    echo "row: [bold]Processes by User:[/]"
    echo "$all_procs" | awk 'NR>1 {print $1}' | sort | uniq -c | sort -rn | head -n 5 | while read -r count user; do
        echo "row: [grey70]$user: $count processes[/]"
    done

    # Zombie process details
    if [ "$zombies" -gt 0 ]; then
        echo "row: "
        echo "row: [bold]Zombie Processes:[/]"
        echo "$all_procs" | awk '$8=="Z" || $8~/^Z/ {
            cmd = $11
            gsub(/.*\//, "", cmd)
            printf "row: [yellow]PID %s: %s (parent: %s)[/]\n", $2, cmd, $3
        }' | head -n 5
    fi

    # Process tree preview
    echo "row: "
    echo "row: [bold]Process Tree (root):[/]"
    ps auxf 2>/dev/null | head -n 20 | tail -n 15 | while read -r line; do
        cmd=$(echo "$line" | awk '{print $11}' | cut -c1-40)
        echo "row: [grey70]$cmd[/]"
    done
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
echo "action: View open files:lsof 2>/dev/null | head -50"
