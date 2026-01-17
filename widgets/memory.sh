#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

echo "title: Memory Usage"
echo "refresh: 2"

# Get memory usage
mem_info=$(free -m | awk 'NR==2{printf "%.0f %.0f %.0f", $3,$2,$3*100/$2}')
read -r used total percent <<< "$mem_info"

# Determine status
if [ "$percent" -lt 70 ]; then
    status="ok"
elif [ "$percent" -lt 90 ]; then
    status="warn"
else
    status="error"
fi

echo "row: [status:$status] Memory: ${used}MB / ${total}MB (${percent}%)"
echo "row: [progress:${percent}:inline]"

# Get swap info
swap_info=$(free -m | awk 'NR==3{if($2>0) printf "%.0f %.0f %.0f", $3,$2,$3*100/$2; else print "0 0 0"}')
read -r swap_used swap_total swap_percent <<< "$swap_info"

if [ "$swap_total" -gt 0 ]; then
    echo "row: Swap: ${swap_used}MB / ${swap_total}MB (${swap_percent}%)"
else
    echo "row: Swap: [grey70]Not configured[/]"
fi

# Get available memory
available=$(free -m | awk 'NR==2{print $7}')
echo "row: Available: ${available}MB"
