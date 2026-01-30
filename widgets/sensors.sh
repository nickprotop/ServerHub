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
            echo "row: [miniprogress:$temp_c:15:warm] Temperature"

            # Extended: show all thermal zones
            if [ "$EXTENDED" = true ]; then
                echo "row: "
                echo "row: [bold]All Thermal Zones:[/]"
                echo "[table:Zone|Type|Temperature|Status]"
                for zone in /sys/class/thermal/thermal_zone*/; do
                    if [ -d "$zone" ]; then
                        zone_name=$(basename "$zone")
                        zone_type=$(cat "${zone}type" 2>/dev/null | cut -c1-20)
                        zone_temp=$(cat "${zone}temp" 2>/dev/null)
                        if [ -n "$zone_temp" ]; then
                            zone_c=$((zone_temp / 1000))
                            if [ "$zone_c" -gt 80 ]; then
                                echo "[tablerow:$zone_name|$zone_type|[red]${zone_c}°C[/]|[red]Critical[/]]"
                            elif [ "$zone_c" -gt 70 ]; then
                                echo "[tablerow:$zone_name|$zone_type|[yellow]${zone_c}°C[/]|[yellow]Warning[/]]"
                            else
                                echo "[tablerow:$zone_name|$zone_type|[green]${zone_c}°C[/]|[green]OK[/]]"
                            fi
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
    echo "row: [miniprogress:$cpu_temp:15:warm] Temperature"
fi

# Dashboard mode: compact tables
if [ "$EXTENDED" = false ]; then
    # CPU cores - limit to 6 in standard mode
    core_temps=$(echo "$sensor_data" | grep "Core " | grep -oP '\+\K[0-9]+\.?[0-9]*')
    core_count=$(echo "$core_temps" | wc -l)

    if [ -n "$core_temps" ] && [ "$core_count" -gt 0 ]; then
        echo "row: "
        echo "row: [bold]CPU Cores:[/]"
        echo "[table:Core|Temperature|Status]"

        core_num=0
        echo "$core_temps" | head -n 6 | while read -r temp; do
            temp_int=${temp%.*}

            if [ $temp_int -gt 85 ]; then
                echo "[tablerow:Core $core_num|[red]${temp}°C[/]|[red]Critical[/]]"
            elif [ $temp_int -gt 75 ]; then
                echo "[tablerow:Core $core_num|[yellow]${temp}°C[/]|[yellow]Warning[/]]"
            else
                echo "[tablerow:Core $core_num|[green]${temp}°C[/]|[green]OK[/]]"
            fi
            ((core_num++))
        done

        # Show indicator if more cores exist
        if [ "$core_count" -gt 6 ]; then
            remaining=$((core_count - 6))
            echo "row: "
            echo "row: [grey70]... and $remaining more cores[/]"
        fi
    fi

    # Fan speeds table
    fans=$(echo "$sensor_data" | grep -i "fan" | grep -oP ':\s+\K[0-9]+')
    if [ -n "$fans" ]; then
        echo "row: "
        echo "row: [bold]Fans:[/]"
        echo "[table:Fan|Speed|Status]"
        fan_num=1
        echo "$fans" | head -n 5 | while read -r rpm; do
            if [ "$rpm" -lt 500 ]; then
                echo "[tablerow:Fan $fan_num|[yellow]${rpm} RPM[/]|[yellow]Low[/]]"
            else
                echo "[tablerow:Fan $fan_num|[cyan1]${rpm} RPM[/]|[green]OK[/]]"
            fi
            ((fan_num++))
        done
    fi

    # GPU temperature (NVIDIA)
    if command -v nvidia-smi &> /dev/null; then
        gpu_temp=$(nvidia-smi --query-gpu=temperature.gpu --format=csv,noheader 2>/dev/null)
        if [ -n "$gpu_temp" ]; then
            echo "row: "
            echo "row: [bold]GPU:[/]"
            echo "[table:Property|Value]"

            if [ $gpu_temp -gt 85 ]; then
                echo "[tablerow:Temperature|[red]${gpu_temp}°C[/]]"
            elif [ $gpu_temp -gt 75 ]; then
                echo "[tablerow:Temperature|[yellow]${gpu_temp}°C[/]]"
            else
                echo "[tablerow:Temperature|[green]${gpu_temp}°C[/]]"
            fi

            gpu_name=$(nvidia-smi --query-gpu=name --format=csv,noheader 2>/dev/null | cut -c1-25)
            [ -n "$gpu_name" ] && echo "[tablerow:Model|$gpu_name]"
        fi
    fi
else
    # Extended mode: detailed sensor information with comprehensive tables
    echo "row: "

    # All CPU cores table
    core_temps=$(echo "$sensor_data" | grep "Core " | grep -oP '\+\K[0-9]+\.?[0-9]*')
    if [ -n "$core_temps" ]; then
        echo "row: [bold]All CPU Cores:[/]"
        echo "[table:Core|Temperature|High|Critical|Status]"

        core_num=0
        echo "$core_temps" | while read -r temp; do
            temp_int=${temp%.*}

            # Try to get high/crit limits
            core_info=$(echo "$sensor_data" | grep "Core $core_num:")
            high=$(echo "$core_info" | grep -oP 'high = \+\K[0-9]+')
            crit=$(echo "$core_info" | grep -oP 'crit = \+\K[0-9]+')
            [ -z "$high" ] && high="85"
            [ -z "$crit" ] && crit="95"

            if [ $temp_int -gt 85 ]; then
                echo "[tablerow:Core $core_num|[red]${temp}°C[/]|${high}°C|${crit}°C|[red]Critical[/]]"
            elif [ $temp_int -gt 75 ]; then
                echo "[tablerow:Core $core_num|[yellow]${temp}°C[/]|${high}°C|${crit}°C|[yellow]Warning[/]]"
            else
                echo "[tablerow:Core $core_num|[green]${temp}°C[/]|${high}°C|${crit}°C|[green]OK[/]]"
            fi
            ((core_num++))
        done
    fi

    # All fans table
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]All Fans:[/]"
    echo "[table:Fan|Speed|Min|Max|Status]"
    fan_num=1
    echo "$sensor_data" | grep -i "fan" | while read -r line; do
        rpm=$(echo "$line" | grep -oP ':\s+\K[0-9]+' | head -n1)
        min=$(echo "$line" | grep -oP 'min =\s+\K[0-9]+')
        [ -z "$min" ] && min="-"

        if [ -n "$rpm" ]; then
            if [ "$rpm" -lt 500 ]; then
                echo "[tablerow:Fan $fan_num|[yellow]${rpm} RPM[/]|$min|N/A|[yellow]Low[/]]"
            else
                echo "[tablerow:Fan $fan_num|[cyan1]${rpm} RPM[/]|$min|N/A|[green]OK[/]]"
            fi
            ((fan_num++))
        fi
    done

    # GPU details table (NVIDIA)
    if command -v nvidia-smi &> /dev/null; then
        gpu_temp=$(nvidia-smi --query-gpu=temperature.gpu --format=csv,noheader 2>/dev/null)
        if [ -n "$gpu_temp" ]; then
            echo "row: "
            echo "row: [divider]"
            echo "row: "
            echo "row: [bold]GPU Details:[/]"
            echo "[table:Property|Value]"

            gpu_name=$(nvidia-smi --query-gpu=name --format=csv,noheader 2>/dev/null)
            gpu_power=$(nvidia-smi --query-gpu=power.draw --format=csv,noheader 2>/dev/null)
            gpu_util=$(nvidia-smi --query-gpu=utilization.gpu --format=csv,noheader 2>/dev/null)
            gpu_mem=$(nvidia-smi --query-gpu=memory.used,memory.total --format=csv,noheader 2>/dev/null)

            echo "[tablerow:Model|$gpu_name]"

            if [ $gpu_temp -gt 85 ]; then
                echo "[tablerow:Temperature|[red]${gpu_temp}°C[/]]"
            elif [ $gpu_temp -gt 75 ]; then
                echo "[tablerow:Temperature|[yellow]${gpu_temp}°C[/]]"
            else
                echo "[tablerow:Temperature|[green]${gpu_temp}°C[/]]"
            fi

            [ -n "$gpu_power" ] && echo "[tablerow:Power|$gpu_power]"
            [ -n "$gpu_util" ] && echo "[tablerow:Utilization|$gpu_util]"
            [ -n "$gpu_mem" ] && echo "[tablerow:Memory|$gpu_mem]"
        fi
    fi

    # Voltages table
    voltages=$(echo "$sensor_data" | grep -i "in[0-9]\|volt" | head -n 10)
    if [ -n "$voltages" ]; then
        echo "row: "
        echo "row: [divider]"
        echo "row: "
        echo "row: [bold]Voltages:[/]"
        echo "[table:Rail|Voltage|Min|Max]"
        echo "$voltages" | while read -r line; do
            rail=$(echo "$line" | awk -F':' '{print $1}' | xargs | cut -c1-15)
            volt=$(echo "$line" | grep -oP ':\s+\K[\+\-]?[0-9]+\.[0-9]+' | head -n1)
            min=$(echo "$line" | grep -oP 'min =\s+\K[\+\-]?[0-9]+\.[0-9]+')
            max=$(echo "$line" | grep -oP 'max =\s+\K[\+\-]?[0-9]+\.[0-9]+')
            [ -z "$min" ] && min="-"
            [ -z "$max" ] && max="-"
            [ -n "$volt" ] && echo "[tablerow:$rail|${volt}V|${min}V|${max}V]"
        done
    fi

    # Thermal zones table
    if [ -d /sys/class/thermal ]; then
        echo "row: "
        echo "row: [divider]"
        echo "row: "
        echo "row: [bold]Thermal Zones:[/]"
        echo "[table:Zone|Type|Temperature|Mode]"
        for zone in /sys/class/thermal/thermal_zone*/; do
            if [ -d "$zone" ]; then
                zone_name=$(basename "$zone")
                zone_type=$(cat "${zone}type" 2>/dev/null | cut -c1-20)
                zone_temp=$(cat "${zone}temp" 2>/dev/null)
                zone_mode=$(cat "${zone}mode" 2>/dev/null)
                if [ -n "$zone_temp" ]; then
                    zone_c=$((zone_temp / 1000))

                    if [ "$zone_c" -gt 80 ]; then
                        echo "[tablerow:$zone_name|$zone_type|[red]${zone_c}°C[/]|$zone_mode]"
                    elif [ "$zone_c" -gt 70 ]; then
                        echo "[tablerow:$zone_name|$zone_type|[yellow]${zone_c}°C[/]|$zone_mode]"
                    else
                        echo "[tablerow:$zone_name|$zone_type|[green]${zone_c}°C[/]|$zone_mode]"
                    fi
                fi
            fi
        done
    fi

    # Cooling devices table
    if [ -d /sys/class/thermal ]; then
        cooling_count=$(ls -d /sys/class/thermal/cooling_device*/ 2>/dev/null | wc -l)
        if [ "$cooling_count" -gt 0 ]; then
            echo "row: "
            echo "row: [divider]"
            echo "row: "
            echo "row: [bold]Cooling Devices:[/]"
            echo "[table:Device|Type|State|Max State]"
            for cooling in /sys/class/thermal/cooling_device*/; do
                if [ -d "$cooling" ]; then
                    cooling_name=$(basename "$cooling")
                    cooling_type=$(cat "${cooling}type" 2>/dev/null | cut -c1-20)
                    cur_state=$(cat "${cooling}cur_state" 2>/dev/null)
                    max_state=$(cat "${cooling}max_state" 2>/dev/null)
                    echo "[tablerow:$cooling_name|$cooling_type|$cur_state|$max_state]"
                fi
            done | head -n 10
        fi
    fi

    # Statistics
    echo "row: "
    echo "row: [divider:─:cyan1]"
    echo "row: "
    echo "row: [bold]Statistics:[/]"
    [ -n "$cpu_temp" ] && echo "row: [grey70]Max CPU temperature: ${cpu_temp}°C[/]"
    core_count=$(echo "$core_temps" | wc -l)
    [ "$core_count" -gt 0 ] && echo "row: [grey70]CPU cores monitored: $core_count[/]"
    fan_count=$(echo "$sensor_data" | grep -ic "fan")
    [ "$fan_count" -gt 0 ] && echo "row: [grey70]Fans detected: $fan_count[/]"
fi

# Actions
echo "action: View full sensor data:sensors"
echo "action: [sudo,refresh,timeout=120] Detect sensors:sensors-detect --auto"

# NVIDIA GPU actions
if command -v nvidia-smi &> /dev/null; then
    echo "action: View GPU info:nvidia-smi"
fi

# High temperature actions
if [ "$max_temp" -gt 80 ] 2>/dev/null; then
    echo "action: [danger] View thermal throttling:cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq && echo 'MHz' && dmesg | grep -i thermal | tail -10"
fi
