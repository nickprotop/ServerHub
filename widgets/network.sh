#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

echo "title: Network Traffic"
echo "refresh: 2"

# Setup cache directory for historical data
CACHE_DIR="$HOME/.cache/serverhub"
mkdir -p "$CACHE_DIR"

# Get primary network interface
iface=$(ip route 2>/dev/null | grep '^default' | awk '{print $5}' | head -n1)

if [ -z "$iface" ]; then
    echo "row: [status:error] No active network interface"
    exit 0
fi

# History files (per interface)
RX_SPEED_HISTORY="$CACHE_DIR/network-${iface}-rx-speed.txt"
TX_SPEED_HISTORY="$CACHE_DIR/network-${iface}-tx-speed.txt"
LAST_SAMPLE_FILE="$CACHE_DIR/network-${iface}-last.txt"

# Get current bytes (instant read, no blocking)
rx_bytes=$(cat /sys/class/net/$iface/statistics/rx_bytes 2>/dev/null || echo 0)
tx_bytes=$(cat /sys/class/net/$iface/statistics/tx_bytes 2>/dev/null || echo 0)
current_time=$(date +%s)

# Calculate speed (KB/s) since last sample
rx_speed_kb=0
tx_speed_kb=0
should_store_history=true

if [ -f "$LAST_SAMPLE_FILE" ]; then
    read -r last_time last_rx last_tx < "$LAST_SAMPLE_FILE"
    time_diff=$((current_time - last_time))

    # Only calculate speed if time_diff is reasonable (not stale)
    # Max 5 seconds for a 2-second refresh widget
    if [ "$time_diff" -gt 0 ] && [ "$time_diff" -le 5 ]; then
        rx_diff=$((rx_bytes - last_rx))
        tx_diff=$((tx_bytes - last_tx))
        rx_speed_kb=$((rx_diff / 1024 / time_diff))
        tx_speed_kb=$((tx_diff / 1024 / time_diff))
    elif [ "$time_diff" -gt 5 ]; then
        # Sample is stale - don't store in history, just update baseline
        should_store_history=false
    fi
fi

# Store current sample for next run
echo "$current_time $rx_bytes $tx_bytes" > "$LAST_SAMPLE_FILE"

# Store speed history (keep last 10 for dashboard, 30 for extended)
store_history() {
    local file=$1
    local value=$2
    local max_samples=$3

    # Append new value
    echo "$value" >> "$file"

    # Keep only last N samples
    tail -n "$max_samples" "$file" > "${file}.tmp" 2>/dev/null
    mv "${file}.tmp" "$file" 2>/dev/null
}

# Read history as comma-separated values for sparkline/graph
read_history() {
    local file=$1
    if [ -f "$file" ] && [ -s "$file" ]; then
        paste -sd',' "$file"
    else
        echo "0"
    fi
}

# Determine sample count based on mode
if [ "$EXTENDED" = true ]; then
    MAX_SAMPLES=30
else
    MAX_SAMPLES=10
fi

# Only store speed in history if the sample is fresh (not stale)
if [ "$should_store_history" = true ]; then
    store_history "$RX_SPEED_HISTORY" "$rx_speed_kb" "$MAX_SAMPLES"
    store_history "$TX_SPEED_HISTORY" "$tx_speed_kb" "$MAX_SAMPLES"
fi

# Read history for visualization
rx_history=$(read_history "$RX_SPEED_HISTORY")
tx_history=$(read_history "$TX_SPEED_HISTORY")

# Convert bytes to human readable
format_bytes() {
    local bytes=$1
    if [ "$bytes" -ge 1073741824 ]; then
        awk "BEGIN {printf \"%.1f GB\", $bytes/1073741824}"
    elif [ "$bytes" -ge 1048576 ]; then
        awk "BEGIN {printf \"%.1f MB\", $bytes/1048576}"
    else
        awk "BEGIN {printf \"%.1f KB\", $bytes/1024}"
    fi
}

# Format speed
format_speed() {
    local speed_kb=$1
    if [ "$speed_kb" -ge 1024 ]; then
        awk "BEGIN {printf \"%.1f MB/s\", $speed_kb/1024}"
    else
        awk "BEGIN {printf \"%.0f KB/s\", $speed_kb}"
    fi
}

rx_display=$(format_bytes $rx_bytes)
tx_display=$(format_bytes $tx_bytes)
rx_speed_display=$(format_speed $rx_speed_kb)
tx_speed_display=$(format_speed $tx_speed_kb)

# Determine status based on speed
if [ "$rx_speed_kb" -gt 102400 ] || [ "$tx_speed_kb" -gt 102400 ]; then
    # Over 100 MB/s
    status="warn"
elif [ "$rx_speed_kb" -gt 10240 ] || [ "$tx_speed_kb" -gt 10240 ]; then
    # Over 10 MB/s
    status="info"
else
    status="ok"
fi

echo "row: [status:$status] Interface: [cyan1]$iface[/]"

# Get IP address
ip_addr=$(ip -4 addr show $iface 2>/dev/null | grep -oP 'inet \K[\d.]+' | head -n1)
if [ -n "$ip_addr" ]; then
    echo "row: [grey70]IP: $ip_addr[/]"
fi

# Show current speed with sparklines
echo "row: ↓ RX: [green]${rx_speed_display}[/] [sparkline:${rx_history}:green] [grey70](total: ${rx_display})[/]"
echo "row: ↑ TX: [yellow]${tx_speed_display}[/] [sparkline:${tx_history}:yellow] [grey70](total: ${tx_display})[/]"

# Active connections count
if command -v ss &> /dev/null; then
    active_conn=$(ss -tan 2>/dev/null | grep -c ESTAB || echo 0)
    echo "row: Connections: [cyan1]$active_conn[/] active"
fi

# Extended mode: detailed network information
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Traffic History (last 60s):[/]"
    echo "row: [graph:${rx_history}:green:Download (KB/s)]"
    echo "row: "
    echo "row: [graph:${tx_history}:yellow:Upload (KB/s)]"

    # All network interfaces table
    echo "row: "
    echo "row: [divider:─:cyan1]"
    echo "row: "
    echo "row: [bold]All Network Interfaces:[/]"
    echo "[table:Interface|Status|IP Address|RX Total|TX Total]"

    for iface_path in /sys/class/net/*/; do
        iface_name=$(basename "$iface_path")
        [ "$iface_name" = "lo" ] && continue

        state=$(cat "$iface_path/operstate" 2>/dev/null || echo "unknown")
        iface_rx=$(cat "$iface_path/statistics/rx_bytes" 2>/dev/null || echo 0)
        iface_tx=$(cat "$iface_path/statistics/tx_bytes" 2>/dev/null || echo 0)
        iface_rx_h=$(format_bytes $iface_rx)
        iface_tx_h=$(format_bytes $iface_tx)

        iface_ip=$(ip -4 addr show "$iface_name" 2>/dev/null | grep -oP 'inet \K[\d.]+' | head -n1)
        [ -z "$iface_ip" ] && iface_ip="-"

        if [ "$state" = "up" ]; then
            status_col="[green]${state}[/]"
        else
            status_col="[grey70]${state}[/]"
        fi

        echo "[tablerow:${iface_name}|${status_col}|${iface_ip}|${iface_rx_h}|${iface_tx_h}]"
    done

    # Interface errors and drops
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Interface Statistics ($iface):[/]"

    rx_errors=$(cat /sys/class/net/$iface/statistics/rx_errors 2>/dev/null || echo 0)
    tx_errors=$(cat /sys/class/net/$iface/statistics/tx_errors 2>/dev/null || echo 0)
    rx_dropped=$(cat /sys/class/net/$iface/statistics/rx_dropped 2>/dev/null || echo 0)
    tx_dropped=$(cat /sys/class/net/$iface/statistics/tx_dropped 2>/dev/null || echo 0)

    echo "[table:Metric|RX|TX]"

    if [ "$rx_errors" -gt 0 ] || [ "$tx_errors" -gt 0 ]; then
        echo "[tablerow:Errors|[yellow]${rx_errors}[/]|[yellow]${tx_errors}[/]]"
    else
        echo "[tablerow:Errors|${rx_errors}|${tx_errors}]"
    fi

    if [ "$rx_dropped" -gt 0 ] || [ "$tx_dropped" -gt 0 ]; then
        echo "[tablerow:Dropped|[yellow]${rx_dropped}[/]|[yellow]${tx_dropped}[/]]"
    else
        echo "[tablerow:Dropped|${rx_dropped}|${tx_dropped}]"
    fi

    rx_packets=$(cat /sys/class/net/$iface/statistics/rx_packets 2>/dev/null || echo 0)
    tx_packets=$(cat /sys/class/net/$iface/statistics/tx_packets 2>/dev/null || echo 0)
    echo "[tablerow:Packets|${rx_packets}|${tx_packets}]"

    # MAC address
    mac=$(cat /sys/class/net/$iface/address 2>/dev/null || echo "unknown")
    echo "row: "
    echo "row: [grey70]MAC Address: ${mac}[/]"

    # Routing table
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Routing Table:[/]"
    ip route 2>/dev/null | head -n 5 | while read -r route; do
        echo "row: [grey70]• $route[/]"
    done

    # DNS servers
    echo "row: "
    echo "row: [bold]DNS Servers:[/]"
    if [ -f /etc/resolv.conf ]; then
        grep "^nameserver" /etc/resolv.conf | awk '{print $2}' | while read -r dns; do
            echo "row: [grey70]• $dns[/]"
        done
    else
        echo "row: [grey70]No DNS servers configured[/]"
    fi

    # Gateway
    gateway=$(ip route 2>/dev/null | grep '^default' | awk '{print $3}' | head -n1)
    if [ -n "$gateway" ]; then
        echo "row: "
        echo "row: [grey70]Default Gateway: $gateway[/]"
    fi
fi

# Actions
echo "action: Show all connections:ss -tunapl"
echo "action: Show routing table:ip route"
echo "action: Ping gateway:ping -c 4 \$(ip route | grep '^default' | awk '{print \$3}')"
echo "action: [sudo] Restart networking:systemctl restart networking"
echo "action: Clear traffic history:rm -f $RX_SPEED_HISTORY $TX_SPEED_HISTORY $LAST_SAMPLE_FILE"
