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

# Get primary network interface
iface=$(ip route 2>/dev/null | grep '^default' | awk '{print $5}' | head -n1)

if [ -z "$iface" ]; then
    echo "row: [status:error] No active network interface"
    exit 0
fi

# Get current bytes (instant read, no blocking)
rx_bytes=$(cat /sys/class/net/$iface/statistics/rx_bytes 2>/dev/null || echo 0)
tx_bytes=$(cat /sys/class/net/$iface/statistics/tx_bytes 2>/dev/null || echo 0)

# Convert to human readable
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

rx_display=$(format_bytes $rx_bytes)
tx_display=$(format_bytes $tx_bytes)

echo "row: [status:ok] Interface: [cyan1]$iface[/]"
echo "row: ↓ Total RX: [green]$rx_display[/]"
echo "row: ↑ Total TX: [yellow]$tx_display[/]"

# Active connections count (quick)
if command -v ss &> /dev/null; then
    active_conn=$(ss -tan 2>/dev/null | grep -c ESTAB || echo 0)
    echo "row: Active connections: [cyan1]$active_conn[/]"
fi

# Get IP address
ip_addr=$(ip -4 addr show $iface 2>/dev/null | grep -oP 'inet \K[\d.]+' | head -n1)
if [ -n "$ip_addr" ]; then
    echo "row: [grey70]IP: $ip_addr[/]"
fi

# Extended mode: detailed network information
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [bold]Live Speed (1s sample):[/]"

    # Now we can do the blocking speed measurement (OK in extended mode)
    rx_before=$rx_bytes
    tx_before=$tx_bytes
    sleep 1
    rx_after=$(cat /sys/class/net/$iface/statistics/rx_bytes 2>/dev/null || echo 0)
    tx_after=$(cat /sys/class/net/$iface/statistics/tx_bytes 2>/dev/null || echo 0)

    rx_speed=$((rx_after - rx_before))
    tx_speed=$((tx_after - tx_before))

    if [ $rx_speed -gt 1048576 ]; then
        rx_speed_display=$(awk "BEGIN {printf \"%.1f MB/s\", $rx_speed/1048576}")
    else
        rx_speed_display=$(awk "BEGIN {printf \"%.1f KB/s\", $rx_speed/1024}")
    fi

    if [ $tx_speed -gt 1048576 ]; then
        tx_speed_display=$(awk "BEGIN {printf \"%.1f MB/s\", $tx_speed/1048576}")
    else
        tx_speed_display=$(awk "BEGIN {printf \"%.1f KB/s\", $tx_speed/1024}")
    fi

    echo "row: [green]↓ Download: $rx_speed_display[/]"
    echo "row: [yellow]↑ Upload: $tx_speed_display[/]"

    # All network interfaces
    echo "row: "
    echo "row: [bold]All Interfaces:[/]"
    for iface_path in /sys/class/net/*/; do
        iface_name=$(basename "$iface_path")
        [ "$iface_name" = "lo" ] && continue

        state=$(cat "$iface_path/operstate" 2>/dev/null || echo "unknown")
        iface_rx=$(cat "$iface_path/statistics/rx_bytes" 2>/dev/null || echo 0)
        iface_tx=$(cat "$iface_path/statistics/tx_bytes" 2>/dev/null || echo 0)
        iface_rx_h=$(format_bytes $iface_rx)
        iface_tx_h=$(format_bytes $iface_tx)

        iface_ip=$(ip -4 addr show "$iface_name" 2>/dev/null | grep -oP 'inet \K[\d.]+' | head -n1)
        mac=$(cat "$iface_path/address" 2>/dev/null || echo "unknown")

        if [ "$state" = "up" ]; then
            echo "row: [status:ok] [cyan1]$iface_name[/] ($state)"
        else
            echo "row: [status:warn] [grey70]$iface_name[/] ($state)"
        fi
        [ -n "$iface_ip" ] && echo "row: [grey70]  IP: $iface_ip[/]"
        echo "row: [grey70]  MAC: $mac[/]"
        echo "row: [grey70]  RX: $iface_rx_h | TX: $iface_tx_h[/]"
    done

    # Interface errors and drops
    echo "row: "
    echo "row: [bold]Interface Stats ($iface):[/]"
    rx_errors=$(cat /sys/class/net/$iface/statistics/rx_errors 2>/dev/null || echo 0)
    tx_errors=$(cat /sys/class/net/$iface/statistics/tx_errors 2>/dev/null || echo 0)
    rx_dropped=$(cat /sys/class/net/$iface/statistics/rx_dropped 2>/dev/null || echo 0)
    tx_dropped=$(cat /sys/class/net/$iface/statistics/tx_dropped 2>/dev/null || echo 0)

    if [ "$rx_errors" -gt 0 ] || [ "$tx_errors" -gt 0 ]; then
        echo "row: [status:warn] Errors: RX $rx_errors / TX $tx_errors"
    else
        echo "row: [grey70]Errors: RX $rx_errors / TX $tx_errors[/]"
    fi

    if [ "$rx_dropped" -gt 0 ] || [ "$tx_dropped" -gt 0 ]; then
        echo "row: [status:warn] Dropped: RX $rx_dropped / TX $tx_dropped"
    else
        echo "row: [grey70]Dropped: RX $rx_dropped / TX $tx_dropped[/]"
    fi

    # Routing table
    echo "row: "
    echo "row: [bold]Routing Table:[/]"
    ip route 2>/dev/null | head -n 5 | while read -r route; do
        echo "row: [grey70]$route[/]"
    done

    # DNS servers
    echo "row: "
    echo "row: [bold]DNS Servers:[/]"
    if [ -f /etc/resolv.conf ]; then
        grep "^nameserver" /etc/resolv.conf | awk '{print $2}' | while read -r dns; do
            echo "row: [grey70]$dns[/]"
        done
    fi

    # Gateway
    gateway=$(ip route 2>/dev/null | grep '^default' | awk '{print $3}')
    if [ -n "$gateway" ]; then
        echo "row: "
        echo "row: [grey70]Gateway: $gateway[/]"
    fi
fi

# Actions
echo "action: Show all connections:ss -tunapl"
echo "action: Show routing table:ip route"
echo "action: [sudo,danger] Restart networking:systemctl restart networking"
