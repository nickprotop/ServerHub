#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

echo "title: System Info"
echo "refresh: 30"

# Hostname
hostname=$(hostname)
echo "row: [bold cyan1]$hostname[/]"
echo "row: "

# OS Distribution
if [ -f /etc/os-release ]; then
    os_name=$(grep "^PRETTY_NAME=" /etc/os-release | cut -d'"' -f2)
else
    os_name=$(uname -s)
fi
echo "row: OS: [grey70]$os_name[/]"

# Kernel version
kernel=$(uname -r)
echo "row: Kernel: [grey70]$kernel[/]"

# Architecture
arch=$(uname -m)
echo "row: Arch: [grey70]$arch[/]"

echo "row: "

# Uptime
uptime_info=$(uptime -p | sed 's/up //')
echo "row: [green]⏱[/] Uptime: [cyan1]$uptime_info[/]"

# Last boot time
last_boot=$(who -b | awk '{print $3, $4}')
echo "row: Last boot: [grey70]$last_boot[/]"

echo "row: "

# Users logged in
users_count=$(who | wc -l)
if [ $users_count -gt 0 ]; then
    users_list=$(who | awk '{print $1}' | sort -u | tr '\n' ' ')
    echo "row: Users logged in: [cyan1]$users_count[/]"
    echo "row: [grey70]$users_list[/]"
else
    echo "row: Users logged in: [grey70]0[/]"
fi
