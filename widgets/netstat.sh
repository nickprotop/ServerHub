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
established=$(echo "$all_tcp" | grep -c "ESTAB")
time_wait=$(echo "$all_tcp" | grep -c "TIME-WAIT")
close_wait=$(echo "$all_tcp" | grep -c "CLOSE-WAIT")
listen=$(echo "$all_tcp" | grep -c "LISTEN")
syn_recv=$(echo "$all_tcp" | grep -c "SYN-RECV")
fin_wait=$(echo "$all_tcp" | grep -c "FIN-WAIT")
total_tcp=$((established + time_wait + close_wait + listen + syn_recv + fin_wait))

# UDP sockets
udp_count=$(ss -uan 2>/dev/null | grep -v "State" | wc -l)

# Status indicator
status="ok"
if [ "$close_wait" -gt 10 ]; then
    status="warn"
fi

# Dashboard mode: Compact tables
if [ "$EXTENDED" = false ]; then
    echo "row: [status:$status] TCP: [cyan1]$total_tcp[/] | UDP: [cyan1]$udp_count[/]"
    echo "row: "

    # Connection states summary
    echo "row: [bold]Connection States:[/]"
    echo "[table:State|Count]"
    echo "[tablerow:ESTABLISHED|[green]$established[/]]"
    echo "[tablerow:LISTEN|[cyan1]$listen[/]]"
    echo "[tablerow:TIME-WAIT|$time_wait]"
    if [ "$close_wait" -gt 0 ]; then
        if [ "$close_wait" -gt 10 ]; then
            echo "[tablerow:CLOSE-WAIT|[yellow]$close_wait[/]]"
        else
            echo "[tablerow:CLOSE-WAIT|$close_wait]"
        fi
    fi

    # Top listening ports
    echo "row: "
    echo "row: [bold]Top Listening Ports:[/]"
    echo "[table:Port|Service|Sockets]"
    ss -tlnp 2>/dev/null | grep LISTEN | awk '{print $4}' | awk -F':' '{print $NF}' | sort -n | uniq -c | sort -rn | head -n 5 | while read -r count port; do
        service=$(grep -w "$port/tcp" /etc/services 2>/dev/null | awk '{print $1}' | head -n1)
        [ -z "$service" ] && service="unknown"
        service_short=$(echo "$service" | cut -c1-15)
        echo "[tablerow:$port|$service_short|$count]"
    done

    # Top external IPs
    echo "row: "
    echo "row: [bold]Top External Connections:[/]"
    echo "[table:IP Address|Connections]"
    echo "$all_tcp" | grep ESTAB | awk '{print $5}' | awk -F':' '{print $1}' | grep -vE "^(127\.|::1|0\.0\.0\.0|\*)" | sort | uniq -c | sort -rn | head -n 5 | while read -r count ip; do
        echo "[tablerow:$ip|$count]"
    done
else
    # Extended mode: Detailed view with comprehensive tables
    echo "row: [status:$status] Total TCP: [cyan1]$total_tcp[/] | UDP: [cyan1]$udp_count[/]"
    echo "row: "

    # Full connection states table
    echo "row: [bold]Connection States:[/]"
    echo "[table:State|Count|Description]"
    echo "[tablerow:ESTABLISHED|[green]$established[/]|Active connections]"
    echo "[tablerow:LISTEN|[cyan1]$listen[/]|Waiting for connections]"
    echo "[tablerow:TIME-WAIT|$time_wait|Connection closing]"
    if [ "$close_wait" -gt 10 ]; then
        echo "[tablerow:CLOSE-WAIT|[yellow]$close_wait[/]|Remote closed]"
    else
        echo "[tablerow:CLOSE-WAIT|$close_wait|Remote closed]"
    fi
    echo "[tablerow:FIN-WAIT|$fin_wait|Closing connection]"
    echo "[tablerow:SYN-RECV|$syn_recv|Connection request received]"

    # All listening ports
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Listening Ports:[/]"
    echo "[table:Port|Service|Sockets]"
    ss -tlnp 2>/dev/null | grep LISTEN | awk '{print $4}' | awk -F':' '{print $NF}' | sort -n | uniq -c | sort -rn | head -n 20 | while read -r count port; do
        service=$(grep -w "$port/tcp" /etc/services 2>/dev/null | awk '{print $1}' | head -n1)
        [ -z "$service" ] && service="unknown"
        service_short=$(echo "$service" | cut -c1-18)
        echo "[tablerow:$port|$service_short|$count]"
    done

    # External IPs with connection counts
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]External Connections:[/]"
    echo "[table:IP Address|Connections]"
    echo "$all_tcp" | grep ESTAB | awk '{print $5}' | awk -F':' '{print $1}' | grep -vE "^(127\.|::1|0\.0\.0\.0|\*)" | sort | uniq -c | sort -rn | head -n 20 | while read -r count ip; do
        ip_short=$(echo "$ip" | cut -c1-40)
        echo "[tablerow:$ip_short|$count]"
    done

    # Active connections with processes
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Active Connections (Top 20):[/]"
    echo "[table:Process|Local Port|Remote Address|Proto]"
    ss -tunap 2>/dev/null | grep ESTAB | head -n 20 | while IFS= read -r line; do
        proto=$(echo "$line" | awk '{print $1}')
        local=$(echo "$line" | awk '{print $4}')
        remote=$(echo "$line" | awk '{print $5}')
        process=$(echo "$line" | awk '{for(i=6;i<=NF;i++) printf "%s ", $i}')

        proc_name=$(echo "$process" | grep -oP 'users:\(\("\K[^"]+' | head -n1)
        [ -z "$proc_name" ] && proc_name="?"
        proc_name_short=$(echo "$proc_name" | cut -c1-15)

        local_port=$(echo "$local" | awk -F':' '{print $NF}')
        remote_short=$(echo "$remote" | cut -c1-30)

        echo "[tablerow:$proc_name_short|$local_port|$remote_short|$proto]"
    done

    # Connection analysis
    echo "row: "
    echo "row: [divider:─:cyan1]"
    echo "row: "
    echo "row: [bold]Connection Analysis:[/]"

    # Check for high connection counts
    high_conn=$(echo "$all_tcp" | grep ESTAB | awk '{print $5}' | awk -F':' '{print $1}' | grep -vE "^(127\.|::1|0\.0\.0\.0|\*)" | sort | uniq -c | sort -rn | awk '$1 > 20')

    if [ -n "$high_conn" ]; then
        echo "row: [status:warn] High connection counts detected"
        echo "row: "
        echo "[table:IP Address|Connections|Status]"
        echo "$high_conn" | head -n 10 | while read -r count ip; do
            if [ "$count" -gt 50 ]; then
                echo "[tablerow:$ip|[red]$count[/]|[red]Critical[/]]"
            else
                echo "[tablerow:$ip|[yellow]$count[/]|[yellow]Warning[/]]"
            fi
        done
    else
        echo "row: [status:ok] No suspicious connection patterns detected"
    fi

    # Port statistics
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Port Statistics:[/]"
    echo "row: [grey70]Total listening ports: $(ss -tln 2>/dev/null | grep LISTEN | wc -l)[/]"
    echo "row: [grey70]UDP sockets: $udp_count[/]"
    echo "row: [grey70]Unique external IPs: $(echo "$all_tcp" | grep ESTAB | awk '{print $5}' | awk -F':' '{print $1}' | grep -vE "^(127\.|::1|0\.0\.0\.0|\*)" | sort -u | wc -l)[/]"
fi

# Actions
echo "action: Show all connections:ss -tunapl"
echo "action: Export connections:ss -tunapl > /tmp/connections_$(date +%Y%m%d_%H%M%S).txt && echo 'Exported to /tmp/'"
echo "action: View established:ss -tunap | grep ESTAB | head -30"
echo "action: View listening:ss -tlnp | grep LISTEN | head -30"

# Check for suspicious IPs (more than 50 connections)
suspicious_ip=$(echo "$all_tcp" | grep ESTAB | awk '{print $5}' | awk -F':' '{print $1}' | grep -vE "^(127\.|::1|0\.0\.0\.0|\*)" | sort | uniq -c | sort -rn | awk '$1 > 50 {print $2; exit}')
if [ -n "$suspicious_ip" ]; then
    suspicious_count=$(echo "$all_tcp" | grep ESTAB | awk '{print $5}' | awk -F':' '{print $1}' | grep "^$suspicious_ip$" | wc -l)
    echo "action: [sudo,danger] Block $suspicious_ip ($suspicious_count conn):iptables -A INPUT -s $suspicious_ip -j DROP"
fi
