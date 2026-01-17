#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

echo "title: CPU Usage"
echo "refresh: 2"

# Get CPU usage percentage
if command -v mpstat &> /dev/null; then
    cpu_usage=$(mpstat 1 1 | awk '/Average:/ {print 100 - $NF}')
    cpu_usage=${cpu_usage%.*}
else
    # Fallback to top
    cpu_usage=$(top -bn1 | grep "Cpu(s)" | awk '{print $2}' | cut -d'%' -f1)
    cpu_usage=${cpu_usage%.*}
fi

# Determine status
if [ "$cpu_usage" -lt 70 ]; then
    status="ok"
elif [ "$cpu_usage" -lt 90 ]; then
    status="warn"
else
    status="error"
fi

echo "row: [status:$status] CPU: ${cpu_usage}%"
echo "row: [progress:${cpu_usage}:inline]"

# Get load average
load=$(uptime | awk -F'load average:' '{print $2}')
echo "row: Load Average:${load}"

# Get CPU info
cpu_model=$(lscpu | grep "Model name" | cut -d':' -f2 | xargs)
cpu_cores=$(nproc)
echo "row: [grey70]${cpu_model} (${cpu_cores} cores)[/]"
