#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

# Helper function to strip ANSI codes
# Note: Bracket escaping is handled by ContentSanitizer in the C# code
strip_ansi() {
    sed 's/\x1b\[[0-9;]*m//g'
}

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

# Dashboard mode: compact tables
if [ "$EXTENDED" = false ]; then
    echo "row: [status:$status] Last hour: [red]$errors[/] errors, [yellow]$warnings[/] warnings"
    echo "row: "

    # Summary table
    echo "row: [bold]Log Summary:[/]"
    echo "[table:Level|Count|Period]"
    echo "[tablerow:[red]Errors[/]|$errors|Last hour]"
    echo "[tablerow:[yellow]Warnings[/]|$warnings|Last hour]"

    # Recent errors (limited to 5 in standard mode)
    if [ "$errors" -gt 0 ]; then
        echo "row: "
        echo "row: [bold]Recent Errors:[/]"
        echo "[table:Time|Message]"
        journalctl --since "1 hour ago" -p err --no-pager -q -n 5 --output=short-precise 2>/dev/null | while read -r line; do
            timestamp=$(echo "$line" | awk '{print $1, $2, $3}')
            message=$(echo "$line" | cut -d' ' -f5- | cut -c1-45 | strip_ansi)
            time_short=$(echo "$timestamp" | awk -F'.' '{print $1}' | awk -F'T' '{print $2}' | cut -c1-8)
            [ -z "$time_short" ] && time_short=$(echo "$timestamp" | awk '{print $3}' | cut -c1-8)

            echo "[tablerow:[grey70]$time_short[/]|[red]$message[/]]"
        done
    fi

    # Recent warnings (only if no errors, limited to 5)
    if [ "$warnings" -gt 0 ] && [ "$errors" -eq 0 ]; then
        echo "row: "
        echo "row: [bold]Recent Warnings:[/]"
        echo "[table:Time|Message]"
        journalctl --since "30 minutes ago" -p warning --no-pager -q -n 5 --output=short-precise 2>/dev/null | while read -r line; do
            timestamp=$(echo "$line" | awk '{print $1, $2, $3}')
            message=$(echo "$line" | cut -d' ' -f5- | cut -c1-45 | strip_ansi)
            time_short=$(echo "$timestamp" | awk -F'.' '{print $1}' | awk -F'T' '{print $2}' | cut -c1-8)
            [ -z "$time_short" ] && time_short=$(echo "$timestamp" | awk '{print $3}' | cut -c1-8)

            echo "[tablerow:[grey70]$time_short[/]|[yellow]$message[/]]"
        done
    fi

    # Recent system events
    echo "row: "
    echo "row: [bold]Recent Events:[/]"
    echo "[table:Time|Event]"
    journalctl --since "10 minutes ago" -p notice --no-pager -q -n 5 --output=short-precise 2>/dev/null | while read -r line; do
        timestamp=$(echo "$line" | awk '{print $1, $2, $3}')
        message=$(echo "$line" | cut -d' ' -f5- | cut -c1-45 | strip_ansi)
        time_short=$(echo "$timestamp" | awk -F'.' '{print $1}' | awk -F'T' '{print $2}' | cut -c1-8)
        [ -z "$time_short" ] && time_short=$(echo "$timestamp" | awk '{print $3}' | cut -c1-8)

        echo "[tablerow:[grey70]$time_short[/]|$message]"
    done
else
    # Extended mode: detailed log information with comprehensive tables
    echo "row: [status:$status] Total - Errors: [red]$errors[/] | Warnings: [yellow]$warnings[/] (last hour)"
    echo "row: "

    # All errors table
    echo "row: [bold]All Errors (last hour):[/]"
    echo "[table:Time|Service|Message]"
    journalctl --since "1 hour ago" -p err --no-pager -q -n 20 --output=short-precise 2>/dev/null | while read -r line; do
        timestamp=$(echo "$line" | awk '{print $1, $2, $3}')
        unit=$(echo "$line" | awk '{print $4}' | tr -d ':' | cut -c1-15)
        message=$(echo "$line" | cut -d' ' -f5- | cut -c1-40 | strip_ansi)
        time_short=$(echo "$timestamp" | awk -F'.' '{print $1}' | awk -F'T' '{print $2}' | cut -c1-8)
        [ -z "$time_short" ] && time_short=$(echo "$timestamp" | awk '{print $3}' | cut -c1-8)

        echo "[tablerow:[grey70]$time_short[/]|[cyan1]$unit[/]|[red]$message[/]]"
    done

    # All warnings table
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]All Warnings (last hour):[/]"
    echo "[table:Time|Service|Message]"
    journalctl --since "1 hour ago" -p warning --no-pager -q -n 20 --output=short-precise 2>/dev/null | while read -r line; do
        timestamp=$(echo "$line" | awk '{print $1, $2, $3}')
        unit=$(echo "$line" | awk '{print $4}' | tr -d ':' | cut -c1-15)
        message=$(echo "$line" | cut -d' ' -f5- | cut -c1-40 | strip_ansi)
        time_short=$(echo "$timestamp" | awk -F'.' '{print $1}' | awk -F'T' '{print $2}' | cut -c1-8)
        [ -z "$time_short" ] && time_short=$(echo "$timestamp" | awk '{print $3}' | cut -c1-8)

        echo "[tablerow:[grey70]$time_short[/]|[cyan1]$unit[/]|[yellow]$message[/]]"
    done

    # Errors by service table
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Errors by Service:[/]"
    echo "[table:Service|Error Count]"
    journalctl --since "1 hour ago" -p err --no-pager -q -n 500 2>/dev/null | awk '{print $4}' | tr -d ':' | sort | uniq -c | sort -rn | head -n 10 | while read -r count unit; do
        unit_short=$(echo "$unit" | cut -c1-25)
        echo "[tablerow:[cyan1]$unit_short[/]|[red]$count[/]]"
    done

    # Kernel messages
    kernel_msgs=$(journalctl -k --since "1 hour ago" --no-pager -q -n 500 2>/dev/null | wc -l)
    if [ "$kernel_msgs" -gt 0 ]; then
        echo "row: "
        echo "row: [divider]"
        echo "row: "
        echo "row: [bold]Kernel Messages:[/]"
        echo "[table:Time|Message]"
        journalctl -k --since "1 hour ago" --no-pager -q -n 10 --output=short-precise 2>/dev/null | while read -r line; do
            timestamp=$(echo "$line" | awk '{print $1, $2, $3}')
            time_short=$(echo "$timestamp" | awk -F'.' '{print $1}' | awk -F'T' '{print $2}' | cut -c1-8)
            [ -z "$time_short" ] && time_short=$(echo "$timestamp" | awk '{print $3}' | cut -c1-8)
            message=$(echo "$line" | cut -d' ' -f5- | cut -c1-45 | strip_ansi)
            echo "[tablerow:[grey70]$time_short[/]|$message]"
        done
    fi

    # Boot log summary
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Current Boot:[/]"
    echo "[table:Type|Count]"
    boot_errors=$(journalctl -b -p err --no-pager -q 2>/dev/null | wc -l)
    boot_warnings=$(journalctl -b -p warning --no-pager -q 2>/dev/null | wc -l)
    echo "[tablerow:[red]Errors[/]|$boot_errors]"
    echo "[tablerow:[yellow]Warnings[/]|$boot_warnings]"

    # Auth events table
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Auth Events (last hour):[/]"
    echo "[table:Time|Event]"
    journalctl --since "1 hour ago" -t sshd --no-pager -q -n 10 2>/dev/null | while read -r line; do
        timestamp=$(echo "$line" | awk '{print $1, $2, $3}')
        time_short=$(echo "$timestamp" | awk -F'.' '{print $1}' | awk -F'T' '{print $2}' | cut -c1-8)
        [ -z "$time_short" ] && time_short=$(echo "$timestamp" | awk '{print $3}' | cut -c1-8)
        message=$(echo "$line" | cut -d' ' -f5- | cut -c1-40 | strip_ansi)
        echo "[tablerow:[grey70]$time_short[/]|$message]"
    done

    # Statistics
    echo "row: "
    echo "row: [divider:─:cyan1]"
    echo "row: "
    echo "row: [bold]Statistics:[/]"
    echo "row: [grey70]Kernel messages (last hour): $kernel_msgs[/]"
    echo "row: [grey70]Boot errors: $boot_errors[/]"
    echo "row: [grey70]Boot warnings: $boot_warnings[/]"
fi

# Actions
echo "action: [timeout=0] View live log:journalctl -f -n 100"
echo "action: [sudo,danger,refresh] Clear old logs:journalctl --vacuum-time=7d"

if [ "$errors" -gt 0 ]; then
    echo "action: View all errors:journalctl --since '1 hour ago' -p err --no-pager"
fi

echo "action: View kernel log:dmesg -T | tail -50"
echo "action: [sudo] View auth log:journalctl -u sshd -n 50 --no-pager"
