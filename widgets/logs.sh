#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

echo "title: Recent Logs"
echo "refresh: 10"

# Check if journalctl is available
if ! command -v journalctl &> /dev/null; then
    echo "row: [status:error] journalctl not available"
    exit 0
fi

# Error count in last hour
errors=$(journalctl --since "1 hour ago" -p err --no-pager -q | wc -l)
warnings=$(journalctl --since "1 hour ago" -p warning --no-pager -q | wc -l)

if [ $errors -gt 10 ]; then
    status="error"
elif [ $errors -gt 0 ]; then
    status="warn"
else
    status="ok"
fi

echo "row: [status:$status] Last hour: [red]$errors[/] errors, [yellow]$warnings[/] warnings"
echo "row: "

# Recent errors
if [ $errors -gt 0 ]; then
    echo "row: [bold red]Recent Errors:[/]"
    journalctl --since "1 hour ago" -p err --no-pager -q -n 5 --output=short-precise | while read -r line; do
        # Parse timestamp and message
        timestamp=$(echo "$line" | awk '{print $1, $2}')
        message=$(echo "$line" | cut -d' ' -f4- | cut -c1-60)

        echo "row: [grey70]${timestamp:11:8}[/] [red]${message}[/]"
    done
    echo "row: "
fi

# Recent warnings
if [ $warnings -gt 0 ] && [ $errors -eq 0 ]; then
    echo "row: [bold yellow]Recent Warnings:[/]"
    journalctl --since "30 minutes ago" -p warning --no-pager -q -n 5 --output=short-precise | while read -r line; do
        timestamp=$(echo "$line" | awk '{print $1, $2}')
        message=$(echo "$line" | cut -d' ' -f4- | cut -c1-60)

        echo "row: [grey70]${timestamp:11:8}[/] [yellow]${message}[/]"
    done
    echo "row: "
fi

# Recent system events
echo "row: [bold]Recent System Events:[/]"
journalctl --since "10 minutes ago" -p notice --no-pager -q -n 3 --output=short-precise | while read -r line; do
    timestamp=$(echo "$line" | awk '{print $1, $2}')
    message=$(echo "$line" | cut -d' ' -f4- | cut -c1-60)

    echo "row: [grey70]${timestamp:11:8}[/] $message"
done

# Kernel messages
kernel_msgs=$(journalctl -k --since "1 hour ago" --no-pager -q | wc -l)
if [ $kernel_msgs -gt 0 ]; then
    echo "row: "
    echo "row: Kernel messages: [cyan1]$kernel_msgs[/]"
fi

# Actions
echo "action: View live log:journalctl -f -n 100"
echo "action: [danger] Clear old logs:journalctl --vacuum-time=7d"
