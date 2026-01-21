#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

echo "title: Temperature & Sensors"
echo "refresh: 5"

# Track max temperature for status
max_temp=0
has_sensors=false

# Check if sensors command is available
if ! command -v sensors &> /dev/null; then
    # Fallback to thermal zones
    if [ -d /sys/class/thermal/thermal_zone0 ]; then
        temp=$(cat /sys/class/thermal/thermal_zone0/temp 2>/dev/null)
        if [ -n "$temp" ]; then
            temp_c=$((temp / 1000))
            max_temp=$temp_c

            if [ $temp_c -gt 80 ]; then
                status="error"
            elif [ $temp_c -gt 70 ]; then
                status="warn"
            else
                status="ok"
            fi

            echo "row: [status:$status] CPU: [yellow]${temp_c}°C[/]"
            echo "row: [progress:$temp_c:inline]"

            # Extended: show all thermal zones
            if [ "$EXTENDED" = true ]; then
                echo "row: "
                echo "row: [bold]All Thermal Zones:[/]"
                for zone in /sys/class/thermal/thermal_zone*/; do
                    if [ -d "$zone" ]; then
                        zone_name=$(basename "$zone")
                        zone_type=$(cat "${zone}type" 2>/dev/null)
                        zone_temp=$(cat "${zone}temp" 2>/dev/null)
                        if [ -n "$zone_temp" ]; then
                            zone_c=$((zone_temp / 1000))
                            echo "row: [grey70]$zone_name ($zone_type): ${zone_c}°C[/]"
                        fi
                    fi
                done
            fi
        else
            echo "row: [status:warn] Temperature sensors not available"
            echo "row: [grey70]Install lm-sensors: sudo apt install lm-sensors[/]"
        fi
    else
        echo "row: [status:warn] Temperature sensors not available"
        echo "row: [grey70]Install lm-sensors: sudo apt install lm-sensors[/]"
    fi

    # Actions for fallback mode
    echo "action: [sudo,timeout=180] Install lm-sensors:apt install -y lm-sensors && sensors-detect --auto"
    exit 0
fi

has_sensors=true

# Get all sensor data in one call
sensor_data=$(sensors 2>/dev/null)

# CPU Temperature (package or first core)
cpu_temp=$(echo "$sensor_data" | grep -i "core 0\|package id 0\|cpu" | grep -oP '\+\K[0-9]+' | head -n1)

if [ -n "$cpu_temp" ]; then
    max_temp=$cpu_temp

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

# CPU cores - limit to 4 in standard mode
core_temps=$(echo "$sensor_data" | grep "Core " | grep -oP '\+\K[0-9]+\.?[0-9]*')
core_count=$(echo "$core_temps" | wc -l)

if [ -n "$core_temps" ]; then
    echo "row: "
    echo "row: [bold]CPU Cores:[/]"

    # Standard mode: show only first 4 cores
    if [ "$EXTENDED" = true ]; then
        limit_cores=999
    else
        limit_cores=4
    fi

    core_num=0
    echo "$core_temps" | head -n $limit_cores | while read -r temp; do
        temp_int=${temp%.*}
        [ $temp_int -gt $max_temp ] && max_temp=$temp_int

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

    # Show indicator if more cores exist
    if [ "$EXTENDED" != true ] && [ "$core_count" -gt 4 ]; then
        remaining=$((core_count - 4))
        echo "row: [grey70]... and $remaining more cores[/]"
    fi
fi

# Fan speeds
fans=$(echo "$sensor_data" | grep -i "fan" | grep -oP ':\s+\K[0-9]+')
if [ -n "$fans" ]; then
    echo "row: "
    echo "row: [bold]Fans:[/]"
    fan_num=1
    echo "$fans" | while read -r rpm; do
        if [ "$rpm" -lt 500 ]; then
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

        # Extended: GPU details
        if [ "$EXTENDED" = true ]; then
            gpu_name=$(nvidia-smi --query-gpu=name --format=csv,noheader 2>/dev/null)
            gpu_power=$(nvidia-smi --query-gpu=power.draw --format=csv,noheader 2>/dev/null)
            gpu_util=$(nvidia-smi --query-gpu=utilization.gpu --format=csv,noheader 2>/dev/null)
            gpu_mem=$(nvidia-smi --query-gpu=memory.used,memory.total --format=csv,noheader 2>/dev/null)

            echo "row: [grey70]$gpu_name[/]"
            [ -n "$gpu_power" ] && echo "row: [grey70]Power: $gpu_power[/]"
            [ -n "$gpu_util" ] && echo "row: [grey70]Utilization: $gpu_util[/]"
            [ -n "$gpu_mem" ] && echo "row: [grey70]Memory: $gpu_mem[/]"
        fi
    fi
fi

# Extended mode: detailed sensor information
if [ "$EXTENDED" = true ]; then
    # Raw sensors output
    echo "row: "
    echo "row: [bold]All Sensors:[/]"

    # Parse and display all sensor chips
    current_chip=""
    echo "$sensor_data" | while IFS= read -r line; do
        # Chip name line (doesn't start with whitespace)
        if [[ "$line" =~ ^[a-zA-Z] ]] && [[ "$line" != *":"* ]]; then
            current_chip="$line"
            echo "row: "
            echo "row: [cyan1]$current_chip[/]"
        elif [[ "$line" =~ ^[a-zA-Z] ]] && [[ "$line" == *":"* ]]; then
            # Sensor reading line
            echo "row: [grey70]  $line[/]"
        fi
    done | head -n 50

    # Voltage readings
    voltages=$(echo "$sensor_data" | grep -i "volt\|in[0-9]" | head -n 10)
    if [ -n "$voltages" ]; then
        echo "row: "
        echo "row: [bold]Voltages:[/]"
        echo "$voltages" | while read -r line; do
            echo "row: [grey70]$line[/]"
        done
    fi

    # ACPI info
    if [ -d /sys/class/thermal ]; then
        echo "row: "
        echo "row: [bold]Thermal Zones:[/]"
        for zone in /sys/class/thermal/thermal_zone*/; do
            if [ -d "$zone" ]; then
                zone_name=$(basename "$zone")
                zone_type=$(cat "${zone}type" 2>/dev/null)
                zone_temp=$(cat "${zone}temp" 2>/dev/null)
                zone_mode=$(cat "${zone}mode" 2>/dev/null)
                if [ -n "$zone_temp" ]; then
                    zone_c=$((zone_temp / 1000))
                    echo "row: [grey70]$zone_name: ${zone_c}°C ($zone_type)[/]"
                fi
            fi
        done
    fi

    # Cooling devices
    if [ -d /sys/class/thermal ]; then
        cooling_count=$(ls -d /sys/class/thermal/cooling_device*/ 2>/dev/null | wc -l)
        if [ "$cooling_count" -gt 0 ]; then
            echo "row: "
            echo "row: [bold]Cooling Devices:[/]"
            for cooling in /sys/class/thermal/cooling_device*/; do
                if [ -d "$cooling" ]; then
                    cooling_name=$(basename "$cooling")
                    cooling_type=$(cat "${cooling}type" 2>/dev/null)
                    cur_state=$(cat "${cooling}cur_state" 2>/dev/null)
                    max_state=$(cat "${cooling}max_state" 2>/dev/null)
                    echo "row: [grey70]$cooling_name: $cooling_type (state: $cur_state/$max_state)[/]"
                fi
            done | head -n 10
        fi
    fi
fi

# Actions
echo "action: View full sensor data:sensors"
echo "action: [sudo,refresh,timeout=120] Detect sensors:sensors-detect --auto"

# NVIDIA GPU actions
if command -v nvidia-smi &> /dev/null; then
    echo "action: View GPU info:nvidia-smi"
fi

# High temperature actions
if [ "$max_temp" -gt 80 ]; then
    echo "action: [danger] View thermal throttling:cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq && echo 'MHz' && dmesg | grep -i thermal | tail -10"
fi
