#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

echo "title: Temperature & Sensors"
echo "refresh: 5"

# Check if sensors command is available
if ! command -v sensors &> /dev/null; then
    # Fallback to thermal zones
    if [ -d /sys/class/thermal/thermal_zone0 ]; then
        temp=$(cat /sys/class/thermal/thermal_zone0/temp 2>/dev/null)
        if [ -n "$temp" ]; then
            temp_c=$((temp / 1000))

            if [ $temp_c -gt 80 ]; then
                status="error"
            elif [ $temp_c -gt 70 ]; then
                status="warn"
            else
                status="ok"
            fi

            echo "row: [status:$status] CPU: [yellow]${temp_c}°C[/]"
            echo "row: [progress:$temp_c:inline]"
        else
            echo "row: [status:warn] Temperature sensors not available"
            echo "row: [grey70]Install lm-sensors: sudo apt install lm-sensors[/]"
        fi
    else
        echo "row: [status:warn] Temperature sensors not available"
        echo "row: [grey70]Install lm-sensors: sudo apt install lm-sensors[/]"
    fi
    exit 0
fi

# Initialize sensors if needed
sensors &> /dev/null

# CPU Temperature
cpu_temp=$(sensors | grep -i "core 0\|package id 0\|cpu" | grep -oP '\+\K[0-9]+' | head -n1)

if [ -n "$cpu_temp" ]; then
    if [ $cpu_temp -gt 85 ]; then
        status="error"
    elif [ $cpu_temp -gt 75 ]; then
        status="warn"
    else
        status="ok"
    fi

    echo "row: [status:$status] CPU: [yellow]${cpu_temp}°C[/]"

    # Calculate progress bar (0-100°C scale)
    progress=$cpu_temp
    [ $progress -gt 100 ] && progress=100
    echo "row: [progress:$progress:inline]"
fi

# All CPU cores
core_temps=$(sensors | grep "Core " | grep -oP '\+\K[0-9]+\.?[0-9]*' | head -n 8)
if [ -n "$core_temps" ]; then
    echo "row: "
    echo "row: [bold]CPU Cores:[/]"
    core_num=0
    echo "$core_temps" | while read -r temp; do
        temp_int=${temp%.*}

        if [ $temp_int -gt 85 ]; then
            status="error"
        elif [ $temp_int -gt 75 ]; then
            status="warn"
        else
            status="ok"
        fi

        echo "row: [status:$status] Core $core_num: [grey70]${temp}°C[/]"
        ((core_num++))
    done
fi

# Fan speeds
fans=$(sensors | grep -i "fan" | grep -oP ':\s+\K[0-9]+')
if [ -n "$fans" ]; then
    echo "row: "
    echo "row: [bold]Fans:[/]"
    fan_num=1
    echo "$fans" | while read -r rpm; do
        if [ $rpm -lt 500 ]; then
            status="warn"
        else
            status="ok"
        fi
        echo "row: [status:$status] Fan $fan_num: [cyan1]${rpm} RPM[/]"
        ((fan_num++))
    done
fi

# GPU temperature (NVIDIA)
if command -v nvidia-smi &> /dev/null; then
    gpu_temp=$(nvidia-smi --query-gpu=temperature.gpu --format=csv,noheader 2>/dev/null)
    if [ -n "$gpu_temp" ]; then
        echo "row: "

        if [ $gpu_temp -gt 85 ]; then
            status="error"
        elif [ $gpu_temp -gt 75 ]; then
            status="warn"
        else
            status="ok"
        fi

        echo "row: [status:$status] GPU: [yellow]${gpu_temp}°C[/]"
    fi
fi
