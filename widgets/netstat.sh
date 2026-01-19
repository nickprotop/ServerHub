#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

echo "title: Network Connections"
echo "refresh: 5"

# Check if ss is available
if ! command -v ss &> /dev/null; then
    echo "row: [status:error] ss command not available"
    exit 0
fi

# Get all TCP connections in one call
all_tcp=$(ss -tan 2>/dev/null)

# Parse connection states from the single call
established=$(echo "$all_tcp" | grep -c "ESTAB" || echo 0)
time_wait=$(echo "$all_tcp" | grep -c "TIME-WAIT" || echo 0)
close_wait=$(echo "$all_tcp" | grep -c "CLOSE-WAIT" || echo 0)
listen=$(echo "$all_tcp" | grep -c "LISTEN" || echo 0)
syn_recv=$(echo "$all_tcp" | grep -c "SYN-RECV" || echo 0)
fin_wait=$(echo "$all_tcp" | grep -c "FIN-WAIT" || echo 0)

echo "row: [bold]Connection States:[/]"
echo "row: [status:ok] ESTABLISHED: [green]$established[/]"
echo "row: LISTEN: [cyan1]$listen[/]"
echo "row: TIME-WAIT: [grey70]$time_wait[/]"

if [ "$close_wait" -gt 10 ]; then
    echo "row: [status:warn] CLOSE-WAIT: [yellow]$close_wait[/]"
elif [ "$close_wait" -gt 0 ]; then
    echo "row: CLOSE-WAIT: [grey70]$close_wait[/]"
fi

echo "row: "

# Top listening ports (limited to 3 in standard mode)
echo "row: [bold]Listening Ports:[/]"
ss -tlnp 2>/dev/null | grep LISTEN | awk '{print $4}' | awk -F':' '{print $NF}' | sort -n | uniq -c | sort -rn | head -n 3 | while read -r count port; do
    # Try to get service name
    service=$(grep -w "$port/tcp" /etc/services 2>/dev/null | awk '{print $1}' | head -n1)
    [ -z "$service" ] && service="unknown"

    echo "row: [cyan1]Port $port[/] [grey70]($service)[/]: $count"
done

echo "row: "

# Top external connections by IP (limited to 3 in standard mode)
echo "row: [bold]Top External IPs:[/]"
echo "$all_tcp" | grep ESTAB | awk '{print $5}' | awk -F':' '{print $1}' | grep -vE "^(127\.|::1|0\.0\.0\.0|\*)" | sort | uniq -c | sort -rn | head -n 3 | while read -r count ip; do
    echo "row: [grey70]$ip: [cyan1]$count[/] conn[/]"
done

# UDP sockets
udp_count=$(ss -uan 2>/dev/null | grep -v "State" | wc -l)
if [ "$udp_count" -gt 0 ]; then
    echo "row: "
    echo "row: UDP sockets: [cyan1]$udp_count[/]"
fi

# Extended mode: full connection details
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [bold]All Connection States:[/]"
    echo "row: [grey70]ESTABLISHED: $established[/]"
    echo "row: [grey70]LISTEN: $listen[/]"
    echo "row: [grey70]TIME-WAIT: $time_wait[/]"
    echo "row: [grey70]CLOSE-WAIT: $close_wait[/]"
    echo "row: [grey70]FIN-WAIT: $fin_wait[/]"
    echo "row: [grey70]SYN-RECV: $syn_recv[/]"

    # All listening ports
    echo "row: "
    echo "row: [bold]All Listening Ports:[/]"
    ss -tlnp 2>/dev/null | grep LISTEN | awk '{print $4}' | awk -F':' '{print $NF}' | sort -n | uniq -c | sort -rn | head -n 15 | while read -r count port; do
        service=$(grep -w "$port/tcp" /etc/services 2>/dev/null | awk '{print $1}' | head -n1)
        [ -z "$service" ] && service="unknown"
        echo "row: [grey70]Port $port ($service): $count socket(s)[/]"
    done

    # All external IPs
    echo "row: "
    echo "row: [bold]All External IPs:[/]"
    echo "$all_tcp" | grep ESTAB | awk '{print $5}' | awk -F':' '{print $1}' | grep -vE "^(127\.|::1|0\.0\.0\.0|\*)" | sort | uniq -c | sort -rn | head -n 15 | while read -r count ip; do
        echo "row: [grey70]$ip: $count connection(s)[/]"
    done

    # Connection details with process names
    echo "row: "
    echo "row: [bold]Active Connections (with processes):[/]"
    ss -tunap 2>/dev/null | grep ESTAB | head -n 15 | while read -r proto recv send local remote state process; do
        proc_name=$(echo "$process" | grep -oP 'users:\(\("\K[^"]+' | head -n1)
        [ -z "$proc_name" ] && proc_name="?"
        local_port=$(echo "$local" | awk -F':' '{print $NF}')
        remote_short=$(echo "$remote" | cut -c1-25)
        echo "row: [grey70]$proc_name :$local_port → $remote_short[/]"
    done

    # Suspicious connections (many from single IP)
    echo "row: "
    echo "row: [bold]Connection Analysis:[/]"
    high_conn_ips=$(echo "$all_tcp" | grep ESTAB | awk '{print $5}' | awk -F':' '{print $1}' | grep -vE "^(127\.|::1|0\.0\.0\.0|\*)" | sort | uniq -c | sort -rn | awk '$1 > 20 {print $2 " (" $1 " connections)"}')
    if [ -n "$high_conn_ips" ]; then
        echo "row: [status:warn] High connection counts detected:"
        echo "$high_conn_ips" | head -n 3 | while read -r line; do
            echo "row: [yellow]  $line[/]"
        done
    else
        echo "row: [status:ok] No suspicious connection patterns"
    fi
fi

# Actions
echo "action: Show all connections:ss -tunapl"
echo "action: Export connections:ss -tunapl > /tmp/connections_$(date +%Y%m%d_%H%M%S).txt && echo 'Exported to /tmp/'"

# Check for suspicious IPs (more than 50 connections)
suspicious_ip=$(echo "$all_tcp" | grep ESTAB | awk '{print $5}' | awk -F':' '{print $1}' | grep -vE "^(127\.|::1|0\.0\.0\.0|\*)" | sort | uniq -c | sort -rn | awk '$1 > 50 {print $2; exit}')
if [ -n "$suspicious_ip" ]; then
    echo "action: [sudo,danger] Block $suspicious_ip:iptables -A INPUT -s $suspicious_ip -j DROP"
fi
