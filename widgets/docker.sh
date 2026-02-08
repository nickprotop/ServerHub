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


# List running containers with stats
if [ $running -gt 0 ]; then
    echo "row: "

    # Dashboard: Table with inline progress indicators
    if [ "$EXTENDED" = false ]; then
        echo "row: [bold]Running Containers:[/]"

        # Get stats for all running containers (cache in temp file)
        stats_tmp="/tmp/docker-stats-dashboard-$$"
        docker stats --no-stream --format "{{.Name}}|{{.CPUPerc}}|{{.MemPerc}}" 2>/dev/null > "$stats_tmp"

        # Count actual rows received
        actual_count=$(wc -l < "$stats_tmp")

        if [ "$actual_count" -gt 0 ]; then
            echo "[table:Name|Status|Image|CPU|Memory]"

            # Display first 5 containers (table rows only)
            head -n 5 "$stats_tmp" | while IFS='|' read -r name cpu_raw mem_raw; do
                # Parse percentages (remove % sign)
                cpu_pct=$(echo "$cpu_raw" | sed 's/%//' | awk '{printf "%.0f", $1}')
                mem_pct=$(echo "$mem_raw" | sed 's/%//' | awk '{printf "%.0f", $1}')

                # Clamp values to 0-100
                [ "$cpu_pct" -gt 100 ] && cpu_pct=100
                [ "$cpu_pct" -lt 0 ] && cpu_pct=0
                [ "$mem_pct" -gt 100 ] && mem_pct=100
                [ "$mem_pct" -lt 0 ] && mem_pct=0

                # Get image name
                image=$(docker inspect --format='{{.Config.Image}}' "$name" 2>/dev/null | awk -F':' '{print $1}' | awk -F'/' '{print $NF}' | cut -c1-15)

                # Get container state
                state=$(docker inspect --format='{{.State.Status}}' "$name" 2>/dev/null)
                if [ "$state" = "running" ]; then
                    status_col="[green]up[/]"
                else
                    status_col="[yellow]${state}[/]"
                fi

                # Truncate name
                name_short=$(echo "$name" | cut -c1-15)

                # Format CPU and Memory with mini progress bars
                echo "[tablerow:${name_short}|${status_col}|${image}|[miniprogress:${cpu_pct}:8:spectrum]|[miniprogress:${mem_pct}:8:warm]]"
            done

            # End table with "more" message if needed
            if [ "$actual_count" -gt 5 ]; then
                remaining=$((actual_count - 5))
                echo "row: [grey70]... and $remaining more containers[/]"
            fi

            # Now output all datastore directives (OUTSIDE the table)
            head -n 5 "$stats_tmp" | while IFS='|' read -r name cpu_raw mem_raw; do
                cpu_pct=$(echo "$cpu_raw" | sed 's/%//' | awk '{printf "%.0f", $1}')
                mem_pct=$(echo "$mem_raw" | sed 's/%//' | awk '{printf "%.0f", $1}')
                [ "$cpu_pct" -gt 100 ] && cpu_pct=100
                [ "$cpu_pct" -lt 0 ] && cpu_pct=0
                [ "$mem_pct" -gt 100 ] && mem_pct=100
                [ "$mem_pct" -lt 0 ] && mem_pct=0
                echo "datastore: docker_stats,container=$name cpu_pct=$cpu_pct,mem_pct=$mem_pct"
            done
        else
            # Fallback: docker stats failed, show simple list
            echo "row: [grey70]Unable to get container stats[/]"
        fi

        rm -f "$stats_tmp"
    else
        # Extended: Detailed view with graphs
        echo "row: [bold]Container Resource Usage:[/]"
        echo "[table:Name|Status|Image|CPU|Memory|Network I/O]"

        # Cache stats in temp file for reuse
        stats_tmp="/tmp/docker-stats-$$"
        docker stats --no-stream --format "{{.Name}}|{{.CPUPerc}}|{{.MemPerc}}|{{.MemUsage}}|{{.NetIO}}" 2>/dev/null > "$stats_tmp"

        # First pass: Output all table rows
        while IFS='|' read -r name cpu_raw mem_raw mem_usage net_io; do
            # Get image name (quick, from ps output)
            image=$(docker ps --filter "name=^${name}$" --format "{{.Image}}" 2>/dev/null | awk -F':' '{print $1}' | awk -F'/' '{print $NF}' | cut -c1-12)
            status_col="[green]up[/]"

            # Truncate name
            name_short=$(echo "$name" | cut -c1-15)

            # Format network I/O
            net_short=$(echo "$net_io" | awk '{print $1 "/" $3}')

            echo "[tablerow:${name_short}|${status_col}|${image}|${cpu_raw}|${mem_usage}|${net_short}]"
        done < "$stats_tmp"

        # Second pass: Output all datastore directives (OUTSIDE table)
        while IFS='|' read -r name cpu_raw mem_raw mem_usage net_io; do
            # Parse percentages
            cpu_pct=$(echo "$cpu_raw" | sed 's/%//' | awk '{printf "%.0f", $1}')
            mem_pct=$(echo "$mem_raw" | sed 's/%//' | awk '{printf "%.0f", $1}')

            # Clamp values
            [ "$cpu_pct" -gt 100 ] && cpu_pct=100
            [ "$cpu_pct" -lt 0 ] && cpu_pct=0
            [ "$mem_pct" -gt 100 ] && mem_pct=100
            [ "$mem_pct" -lt 0 ] && mem_pct=0

            echo "datastore: docker_stats,container=$name cpu_pct=$cpu_pct,mem_pct=$mem_pct"
        done < "$stats_tmp"

        # Show resource trend graphs for top 3 containers by CPU (from storage system)
        echo "row: "
        echo "row: [divider]"
        echo "row: "
        echo "row: [bold]Resource Trends (last 60s):[/]"

        sort -t'|' -k2 -rn "$stats_tmp" | head -n 3 | while IFS='|' read -r name cpu_raw _; do
            name_short=$(echo "$name" | cut -c1-20)

            echo "row: "
            echo "row: [cyan1]${name_short}[/]"
            # Use history_graph with container tag filter
            echo "row: [history_graph:docker_stats.cpu_pct,container=$name:last_40:spectrum:CPU %:0-100:40]"
            echo "row: [history_graph:docker_stats.mem_pct,container=$name:last_40:warm:Memory %:0-100:40]"
        done

        # Cleanup temp file
        rm -f "$stats_tmp"
    fi
else
    echo "row: "
    echo "row: [grey70]No containers running[/]"
fi

# Extended mode: additional information
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [divider:═:cyan1]"
    echo "row: "
    echo "row: [bold]Docker System Info:[/]"

    # Docker version
    docker_version=$(docker version --format '{{.Server.Version}}' 2>/dev/null)
    echo "row: [grey70]Docker version: ${docker_version}[/]"

    # Disk usage breakdown
    echo "row: "
    echo "row: [bold]Disk Usage:[/]"
    echo "[table:Type|Total|Active|Reclaimable]"

    docker system df --format "{{.Type}}|{{.TotalCount}}|{{.Active}}|{{.Size}}|{{.Reclaimable}}" 2>/dev/null | while IFS='|' read -r type total active size reclaimable; do
        # Highlight reclaimable space
        if [[ "$reclaimable" == *"%"* ]]; then
            reclaimable_pct=$(echo "$reclaimable" | grep -oP '\d+(?=%)')
            if [ "$reclaimable_pct" -gt 50 ]; then
                reclaimable_col="[yellow]${reclaimable}[/]"
            else
                reclaimable_col="$reclaimable"
            fi
        else
            reclaimable_col="$reclaimable"
        fi

        echo "[tablerow:${type}|${size}|${active}/${total}|${reclaimable_col}]"
    done

    # Images
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Top Images:[/]"
    echo "[table:Repository|Tag|Size|Created]"

    docker images --format "{{.Repository}}|{{.Tag}}|{{.Size}}|{{.CreatedSince}}" 2>/dev/null | head -n 8 | while IFS='|' read -r repo tag size created; do
        repo_short=$(echo "$repo" | awk -F'/' '{print $NF}' | cut -c1-20)
        tag_short=$(echo "$tag" | cut -c1-10)
        echo "[tablerow:${repo_short}|${tag_short}|${size}|${created}]"
    done

    # Networks and volumes
    echo "row: "
    echo "row: [divider]"
    echo "row: "

    network_count=$(docker network ls -q | wc -l)
    volume_count=$(docker volume ls -q | wc -l)

    echo "row: [grey70]Networks: ${network_count} | Volumes: ${volume_count}[/]"
fi

# Actions (dynamic based on state)
if [ $running -gt 0 ]; then
    # Get first running container for quick actions
    first_container=$(docker ps --format "{{.Names}}" | head -n1)
    if [ -n "$first_container" ]; then
        echo "action: View logs (${first_container}):docker logs --tail 100 ${first_container}"
        echo "action: [danger,refresh] Restart ${first_container}:docker restart ${first_container}"
        echo "action: Exec shell (${first_container}):docker exec -it ${first_container} /bin/sh || docker exec -it ${first_container} /bin/bash"
    fi

    echo "action: [danger,refresh] Restart all:docker restart \$(docker ps -q)"
    echo "action: [danger,refresh] Stop all:docker stop \$(docker ps -q)"
else
    echo "action: [refresh] Start all:docker start \$(docker ps -aq)"
fi

if [ $stopped -gt 0 ]; then
    # Get first stopped container
    first_stopped=$(docker ps -aq --filter "status=exited" | head -n1)
    stopped_name=$(docker inspect --format='{{.Name}}' "$first_stopped" 2>/dev/null | sed 's/\///')
    if [ -n "$stopped_name" ]; then
        echo "action: [refresh] Start ${stopped_name}:docker start ${stopped_name}"
        echo "action: [danger,refresh] Remove ${stopped_name}:docker rm ${stopped_name}"
    fi
fi

echo "action: List all:docker ps -a"
echo "action: [refresh,timeout=120] Prune images:docker image prune -f"
echo "action: [danger,refresh,timeout=180] Prune system:docker system prune -af"
