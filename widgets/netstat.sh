#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

echo "title: Network Connections"
echo "refresh: 5"

# Check if ss is available
if ! command -v ss &> /dev/null; then
    echo "row: [status:error] ss command not available"
    exit 0
fi

# Connection states
established=$(ss -tan | grep ESTAB | wc -l)
time_wait=$(ss -tan | grep TIME-WAIT | wc -l)
close_wait=$(ss -tan | grep CLOSE-WAIT | wc -l)
listen=$(ss -tln | grep LISTEN | wc -l)

echo "row: [bold]Connection States:[/]"
echo "row: [status:ok] ESTABLISHED: [green]$established[/]"
echo "row: TIME-WAIT: [grey70]$time_wait[/]"

if [ $close_wait -gt 10 ]; then
    echo "row: [status:warn] CLOSE-WAIT: [yellow]$close_wait[/]"
elif [ $close_wait -gt 0 ]; then
    echo "row: CLOSE-WAIT: [grey70]$close_wait[/]"
fi

echo "row: LISTEN: [cyan1]$listen[/]"

echo "row: "

# Top listening ports
echo "row: [bold]Listening Ports:[/]"
ss -tlnp 2>/dev/null | grep LISTEN | awk '{print $4}' | awk -F':' '{print $NF}' | sort -n | uniq -c | sort -rn | head -n 5 | while read -r count port; do
    # Try to get service name
    service=$(grep -w "$port/tcp" /etc/services 2>/dev/null | awk '{print $1}' | head -n1)
    if [ -z "$service" ]; then
        service="unknown"
    fi

    echo "row: [cyan1]Port $port[/] [grey70]($service)[/]: $count socket(s)"
done

echo "row: "

# Top external connections by IP
echo "row: [bold]Top External IPs:[/]"
ss -tan | grep ESTAB | awk '{print $5}' | awk -F':' '{print $1}' | grep -v "^127\." | grep -v "^0.0.0.0" | sort | uniq -c | sort -rn | head -n 5 | while read -r count ip; do
    echo "row: [grey70]$ip: [cyan1]$count[/] connection(s)[/]"
done

# UDP sockets
udp_count=$(ss -uan | grep -v "State" | wc -l)
if [ $udp_count -gt 0 ]; then
    echo "row: "
    echo "row: UDP sockets: [cyan1]$udp_count[/]"
fi
