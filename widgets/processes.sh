#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

echo "title: Top Processes"
echo "refresh: 3"

# Total process count
total_procs=$(ps aux | wc -l)
echo "row: Total processes: [cyan1]$total_procs[/]"
echo "row: "

# Top 5 processes by CPU
echo "row: [bold]Top 5 by CPU:[/]"
ps aux --sort=-%cpu | awk 'NR>1 {printf "%s|%.1f|%s\n", $11, $3, $2}' | head -n 5 | while IFS='|' read -r cmd cpu pid; do
    # Truncate long command names
    cmd_short=$(echo "$cmd" | awk -F'/' '{print $NF}' | cut -c1-20)

    if (( $(echo "$cpu > 50" | bc -l) )); then
        status="error"
    elif (( $(echo "$cpu > 20" | bc -l) )); then
        status="warn"
    else
        status="ok"
    fi

    echo "row: [status:$status] ${cmd_short}: [yellow]${cpu}%[/] [grey70](PID: $pid)[/]"
done

echo "row: "

# Top 5 processes by Memory
echo "row: [bold]Top 5 by Memory:[/]"
ps aux --sort=-%mem | awk 'NR>1 {printf "%s|%.1f|%s\n", $11, $4, $2}' | head -n 5 | while IFS='|' read -r cmd mem pid; do
    cmd_short=$(echo "$cmd" | awk -F'/' '{print $NF}' | cut -c1-20)

    if (( $(echo "$mem > 10" | bc -l) )); then
        status="warn"
    else
        status="ok"
    fi

    echo "row: [status:$status] ${cmd_short}: [cyan1]${mem}%[/] [grey70](PID: $pid)[/]"
done

# Zombie processes
zombies=$(ps aux | awk '$8=="Z"' | wc -l)
if [ $zombies -gt 0 ]; then
    echo "row: "
    echo "row: [status:warn] Zombie processes: [yellow]$zombies[/]"
fi
