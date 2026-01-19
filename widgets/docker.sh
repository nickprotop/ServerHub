#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

echo "title: Docker Containers"
echo "refresh: 5"

# Check if docker is installed and accessible
if ! command -v docker &> /dev/null; then
    echo "row: [status:error] Docker not installed"
    exit 0
fi

if ! docker ps &> /dev/null; then
    echo "row: [status:error] Cannot connect to Docker daemon"
    echo "row: [grey70]Check permissions or if daemon is running[/]"
    exit 0
fi

# Get container counts
running=$(docker ps -q | wc -l)
stopped=$(docker ps -aq --filter "status=exited" | wc -l)
total=$(docker ps -aq | wc -l)

if [ $running -gt 0 ]; then
    status="ok"
else
    status="warn"
fi

echo "row: [status:$status] Running: [green]$running[/] / Total: [cyan1]$total[/]"

if [ $stopped -gt 0 ]; then
    echo "row: Stopped: [yellow]$stopped[/]"
fi

echo "row: "

# List running containers
if [ $running -gt 0 ]; then
    echo "row: [bold]Running Containers:[/]"
    docker ps --format "table {{.Names}}|{{.Status}}|{{.Image}}" | tail -n +2 | while IFS='|' read -r name status image; do
        # Truncate long names/images
        name_short=$(echo "$name" | cut -c1-20)
        image_short=$(echo "$image" | awk -F':' '{print $1}' | awk -F'/' '{print $NF}' | cut -c1-15)

        # Parse uptime
        if [[ $status =~ Up.*\(health ]]; then
            health_status="ok"
        elif [[ $status =~ Up ]]; then
            health_status="ok"
        else
            health_status="warn"
        fi

        echo "row: [status:$health_status] [cyan1]${name_short}[/] [grey70]($image_short)[/]"
    done
else
    echo "row: [grey70]No containers running[/]"
fi

echo "row: "

# Docker stats summary (CPU/Memory) - sample for 1 second
if [ $running -gt 0 ]; then
    # In extended mode, show all containers; otherwise limit to 5
    if [ "$EXTENDED" = true ]; then
        docker stats --no-stream --format "table {{.Name}}|{{.CPUPerc}}|{{.MemPerc}}|{{.MemUsage}}|{{.NetIO}}" 2>/dev/null | tail -n +2 | while IFS='|' read -r name cpu mem mem_usage net_io; do
            name_short=$(echo "$name" | cut -c1-25)
            echo "row: [grey70]${name_short}: CPU ${cpu} MEM ${mem} (${mem_usage})[/]"
            echo "row: [grey50]  Network: ${net_io}[/]"
        done
    else
        docker stats --no-stream --format "table {{.Name}}|{{.CPUPerc}}|{{.MemPerc}}" 2>/dev/null | tail -n +2 | head -n 5 | while IFS='|' read -r name cpu mem; do
            name_short=$(echo "$name" | cut -c1-20)
            echo "row: [grey70]${name_short}: CPU ${cpu} MEM ${mem}[/]"
        done
    fi
fi

# Extended mode: show additional Docker information
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [bold]Docker System Info:[/]"

    # Docker version
    docker_version=$(docker version --format '{{.Server.Version}}' 2>/dev/null)
    echo "row: [grey70]Docker version: ${docker_version}[/]"

    # Disk usage summary
    docker_disk=$(docker system df --format "table {{.Type}}|{{.Size}}|{{.Reclaimable}}" 2>/dev/null | tail -n +2)
    if [ -n "$docker_disk" ]; then
        echo "row: "
        echo "row: [bold]Disk Usage:[/]"
        echo "$docker_disk" | while IFS='|' read -r type size reclaimable; do
            echo "row: [grey70]${type}: ${size} (reclaimable: ${reclaimable})[/]"
        done
    fi

    # List all images
    echo "row: "
    echo "row: [bold]Images:[/]"
    docker images --format "table {{.Repository}}|{{.Tag}}|{{.Size}}" 2>/dev/null | tail -n +2 | head -n 10 | while IFS='|' read -r repo tag size; do
        repo_short=$(echo "$repo" | awk -F'/' '{print $NF}' | cut -c1-25)
        echo "row: [grey70]${repo_short}:${tag} (${size})[/]"
    done

    # Networks
    network_count=$(docker network ls -q | wc -l)
    echo "row: "
    echo "row: [grey70]Networks: ${network_count}[/]"

    # Volumes
    volume_count=$(docker volume ls -q | wc -l)
    echo "row: [grey70]Volumes: ${volume_count}[/]"
fi

# Docker actions (dynamic based on state)
echo "action: Prune unused images:docker system prune -f"

# Show different actions based on container state
if [ $running -gt 0 ]; then
    echo "action: [danger,refresh] Restart all containers:docker restart \$(docker ps -q)"
    echo "action: [danger,refresh] Stop all containers:docker stop \$(docker ps -q)"
else
    echo "action: [refresh] Start all containers:docker start \$(docker ps -aq)"
fi

echo "action: [refresh] Pull latest images:docker-compose pull 2>/dev/null || echo 'docker-compose not available'"
