#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

echo "title: Recent Logs"
echo "refresh: 10"

# Check if journalctl is available
if ! command -v journalctl &> /dev/null; then
    echo "row: [status:error] journalctl not available"
    exit 0
fi

# Error and warning counts (with limits for speed)
errors=$(journalctl --since "1 hour ago" -p err --no-pager -q -n 1000 2>/dev/null | wc -l)
warnings=$(journalctl --since "1 hour ago" -p warning --no-pager -q -n 1000 2>/dev/null | wc -l)

if [ "$errors" -gt 10 ]; then
    status="error"
elif [ "$errors" -gt 0 ]; then
    status="warn"
else
    status="ok"
fi

echo "row: [status:$status] Last hour: [red]$errors[/] errors, [yellow]$warnings[/] warnings"
echo "row: "

# Recent errors (limited to 3 in standard mode)
if [ "$errors" -gt 0 ]; then
    echo "row: [bold red]Recent Errors:[/]"
    journalctl --since "1 hour ago" -p err --no-pager -q -n 3 --output=short-precise 2>/dev/null | while read -r line; do
        # Parse timestamp and message
        timestamp=$(echo "$line" | awk '{print $1, $2, $3}')
        message=$(echo "$line" | cut -d' ' -f5- | cut -c1-50)
        time_short=$(echo "$timestamp" | awk -F'.' '{print $1}' | awk -F'T' '{print $2}')
        [ -z "$time_short" ] && time_short=$(echo "$timestamp" | awk '{print $3}')

        echo "row: [grey70]${time_short}[/] [red]${message}[/]"
    done
    echo "row: "
fi

# Recent warnings (only if no errors, limited to 3)
if [ "$warnings" -gt 0 ] && [ "$errors" -eq 0 ]; then
    echo "row: [bold yellow]Recent Warnings:[/]"
    journalctl --since "30 minutes ago" -p warning --no-pager -q -n 3 --output=short-precise 2>/dev/null | while read -r line; do
        timestamp=$(echo "$line" | awk '{print $1, $2, $3}')
        message=$(echo "$line" | cut -d' ' -f5- | cut -c1-50)
        time_short=$(echo "$timestamp" | awk -F'.' '{print $1}' | awk -F'T' '{print $2}')
        [ -z "$time_short" ] && time_short=$(echo "$timestamp" | awk '{print $3}')

        echo "row: [grey70]${time_short}[/] [yellow]${message}[/]"
    done
    echo "row: "
fi

# Recent system events
echo "row: [bold]Recent Events:[/]"
journalctl --since "10 minutes ago" -p notice --no-pager -q -n 3 --output=short-precise 2>/dev/null | while read -r line; do
    timestamp=$(echo "$line" | awk '{print $1, $2, $3}')
    message=$(echo "$line" | cut -d' ' -f5- | cut -c1-50)
    time_short=$(echo "$timestamp" | awk -F'.' '{print $1}' | awk -F'T' '{print $2}')
    [ -z "$time_short" ] && time_short=$(echo "$timestamp" | awk '{print $3}')

    echo "row: [grey70]${time_short}[/] $message"
done

# Extended mode: detailed log information
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [bold]All Errors (last hour):[/]"
    journalctl --since "1 hour ago" -p err --no-pager -q -n 20 --output=short-precise 2>/dev/null | while read -r line; do
        timestamp=$(echo "$line" | awk '{print $1, $2, $3}')
        unit=$(echo "$line" | awk '{print $4}' | tr -d ':')
        message=$(echo "$line" | cut -d' ' -f5- | cut -c1-60)
        time_short=$(echo "$timestamp" | awk -F'.' '{print $1}' | awk -F'T' '{print $2}')
        [ -z "$time_short" ] && time_short=$(echo "$timestamp" | awk '{print $3}')

        echo "row: [grey70]${time_short}[/] [cyan1]${unit}[/] [red]${message}[/]"
    done

    echo "row: "
    echo "row: [bold]All Warnings (last hour):[/]"
    journalctl --since "1 hour ago" -p warning --no-pager -q -n 20 --output=short-precise 2>/dev/null | while read -r line; do
        timestamp=$(echo "$line" | awk '{print $1, $2, $3}')
        unit=$(echo "$line" | awk '{print $4}' | tr -d ':')
        message=$(echo "$line" | cut -d' ' -f5- | cut -c1-60)
        time_short=$(echo "$timestamp" | awk -F'.' '{print $1}' | awk -F'T' '{print $2}')
        [ -z "$time_short" ] && time_short=$(echo "$timestamp" | awk '{print $3}')

        echo "row: [grey70]${time_short}[/] [cyan1]${unit}[/] [yellow]${message}[/]"
    done

    # Logs by service/unit
    echo "row: "
    echo "row: [bold]Errors by Service:[/]"
    journalctl --since "1 hour ago" -p err --no-pager -q -n 500 2>/dev/null | awk '{print $4}' | tr -d ':' | sort | uniq -c | sort -rn | head -n 5 | while read -r count unit; do
        echo "row: [grey70]$unit: $count errors[/]"
    done

    # Kernel messages
    kernel_msgs=$(journalctl -k --since "1 hour ago" --no-pager -q -n 500 2>/dev/null | wc -l)
    echo "row: "
    echo "row: [bold]Kernel Messages:[/]"
    echo "row: [grey70]$kernel_msgs messages in last hour[/]"
    journalctl -k --since "1 hour ago" --no-pager -q -n 5 --output=short-precise 2>/dev/null | while read -r line; do
        message=$(echo "$line" | cut -d' ' -f5- | cut -c1-60)
        echo "row: [grey70]$message[/]"
    done

    # Boot log summary
    echo "row: "
    echo "row: [bold]Current Boot:[/]"
    boot_errors=$(journalctl -b -p err --no-pager -q 2>/dev/null | wc -l)
    boot_warnings=$(journalctl -b -p warning --no-pager -q 2>/dev/null | wc -l)
    echo "row: [grey70]Errors: $boot_errors | Warnings: $boot_warnings[/]"

    # Auth log highlights
    echo "row: "
    echo "row: [bold]Auth Events (last hour):[/]"
    journalctl --since "1 hour ago" -t sshd --no-pager -q -n 5 2>/dev/null | while read -r line; do
        message=$(echo "$line" | cut -d' ' -f5- | cut -c1-50)
        echo "row: [grey70]$message[/]"
    done
fi

# Actions
echo "action: [timeout=0] View live log:journalctl -f -n 100"
echo "action: [sudo,danger,refresh] Clear old logs:journalctl --vacuum-time=7d"

if [ "$errors" -gt 0 ]; then
    echo "action: View all errors:journalctl --since '1 hour ago' -p err --no-pager"
fi

echo "action: View kernel log:dmesg -T | tail -50"
echo "action: [sudo] View auth log:journalctl -u sshd -n 50 --no-pager"
