#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

echo "title: Network Traffic"
echo "refresh: 2"

# Get primary network interface
iface=$(ip route | grep '^default' | awk '{print $5}' | head -n1)

if [ -z "$iface" ]; then
    echo "row: [status:error] No active network interface"
    exit 0
fi

# Get network stats
rx_bytes_before=$(cat /sys/class/net/$iface/statistics/rx_bytes 2>/dev/null || echo 0)
tx_bytes_before=$(cat /sys/class/net/$iface/statistics/tx_bytes 2>/dev/null || echo 0)

sleep 1

rx_bytes_after=$(cat /sys/class/net/$iface/statistics/rx_bytes 2>/dev/null || echo 0)
tx_bytes_after=$(cat /sys/class/net/$iface/statistics/tx_bytes 2>/dev/null || echo 0)

# Calculate speeds (bytes per second)
rx_speed=$((rx_bytes_after - rx_bytes_before))
tx_speed=$((tx_bytes_after - tx_bytes_before))

# Convert to human readable (KB/s or MB/s)
if [ $rx_speed -gt 1048576 ]; then
    rx_display=$(awk "BEGIN {printf \"%.1f MB/s\", $rx_speed/1048576}")
else
    rx_display=$(awk "BEGIN {printf \"%.1f KB/s\", $rx_speed/1024}")
fi

if [ $tx_speed -gt 1048576 ]; then
    tx_display=$(awk "BEGIN {printf \"%.1f MB/s\", $tx_speed/1048576}")
else
    tx_display=$(awk "BEGIN {printf \"%.1f KB/s\", $tx_speed/1024}")
fi

echo "row: [status:ok] Interface: [cyan1]$iface[/]"
echo "row: ↓ Download: [green]$rx_display[/]"
echo "row: ↑ Upload: [yellow]$tx_display[/]"

# Get total transferred (since boot)
rx_total=$(cat /sys/class/net/$iface/statistics/rx_bytes 2>/dev/null || echo 0)
tx_total=$(cat /sys/class/net/$iface/statistics/tx_bytes 2>/dev/null || echo 0)

rx_total_mb=$((rx_total / 1048576))
tx_total_mb=$((tx_total / 1048576))

echo "row: [grey70]Total RX: ${rx_total_mb} MB[/]"
echo "row: [grey70]Total TX: ${tx_total_mb} MB[/]"

# Active connections count
if command -v ss &> /dev/null; then
    active_conn=$(ss -tan | grep ESTAB | wc -l)
    echo "row: Active connections: [cyan1]$active_conn[/]"
fi
