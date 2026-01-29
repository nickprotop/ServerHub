#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

echo "title: System Info"
echo "refresh: 60"

# Gather basic system info
hostname=$(hostname)
if [ -f /etc/os-release ]; then
    os_name=$(grep "^PRETTY_NAME=" /etc/os-release | cut -d'"' -f2)
else
    os_name=$(uname -s)
fi
kernel=$(uname -r)
arch=$(uname -m)

# Uptime
uptime_info=$(uptime -p 2>/dev/null | sed 's/up //')
if [ -z "$uptime_info" ]; then
    uptime_info=$(awk '{print int($1/86400)"d "int(($1%86400)/3600)"h "int(($1%3600)/60)"m"}' /proc/uptime)
fi

# Last boot
last_boot=$(who -b 2>/dev/null | awk '{print $3, $4}')

# Users
users_count=$(who 2>/dev/null | wc -l)
users_list=$(who 2>/dev/null | awk '{print $1}' | sort -u | tr '\n' ', ' | sed 's/,$//')

# Dashboard mode: Compact tables
if [ "$EXTENDED" = false ]; then
    echo "row: [status:ok] [bold cyan1]$hostname[/]"
    echo "row: "

    # Basic system info table
    echo "row: [bold]System:[/]"
    echo "[table:Property|Value]"
    echo "[tablerow:OS|$os_name]"
    echo "[tablerow:Kernel|$kernel]"
    echo "[tablerow:Architecture|$arch]"
    echo "[tablerow:Uptime|[green]$uptime_info[/]]"
    if [ -n "$last_boot" ]; then
        echo "[tablerow:Last Boot|$last_boot]"
    fi

    # Users table
    if [ "$users_count" -gt 0 ]; then
        echo "row: "
        echo "row: [bold]Logged In Users:[/]"
        echo "[table:Count|Users]"
        users_short=$(echo "$users_list" | cut -c1-40)
        echo "[tablerow:[cyan1]$users_count[/]|$users_short]"
    fi
else
    # Extended mode: Detailed view with comprehensive tables
    echo "row: [status:ok] Hostname: [bold cyan1]$hostname[/]"
    echo "row: "

    # System information table
    echo "row: [bold]System Information:[/]"
    echo "[table:Property|Value]"
    echo "[tablerow:Operating System|$os_name]"
    echo "[tablerow:Kernel Version|$kernel]"
    echo "[tablerow:Architecture|$arch]"
    echo "[tablerow:Uptime|[green]$uptime_info[/]]"
    if [ -n "$last_boot" ]; then
        echo "[tablerow:Last Boot|$last_boot]"
    fi
    echo "[tablerow:Current Time|$(date '+%Y-%m-%d %H:%M:%S')]"

    # Virtualization type
    virt_type="Physical"
    if grep -q "hypervisor" /proc/cpuinfo 2>/dev/null; then
        if [ -f /sys/class/dmi/id/sys_vendor ]; then
            vendor=$(cat /sys/class/dmi/id/sys_vendor 2>/dev/null)
            case "$vendor" in
                *VMware*) virt_type="VMware VM" ;;
                *Microsoft*) virt_type="Hyper-V VM" ;;
                *QEMU*|*KVM*) virt_type="KVM/QEMU VM" ;;
                *Xen*) virt_type="Xen VM" ;;
                *) virt_type="Virtual Machine" ;;
            esac
        else
            virt_type="Virtual Machine"
        fi
    fi
    echo "[tablerow:System Type|$virt_type]"

    # Timezone
    timezone=$(timedatectl 2>/dev/null | grep "Time zone" | awk '{print $3}')
    [ -z "$timezone" ] && timezone=$(cat /etc/timezone 2>/dev/null)
    [ -n "$timezone" ] && echo "[tablerow:Timezone|$timezone]"

    # CPU Information
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]CPU Information:[/]"
    echo "[table:Property|Value]"

    cpu_model=$(grep -m1 "model name" /proc/cpuinfo 2>/dev/null | cut -d':' -f2 | xargs | cut -c1-50)
    cpu_cores=$(grep -c "^processor" /proc/cpuinfo 2>/dev/null)
    cpu_physical=$(grep "^physical id" /proc/cpuinfo 2>/dev/null | sort -u | wc -l)
    [ "$cpu_physical" -eq 0 ] && cpu_physical=1
    cpu_cache=$(grep -m1 "cache size" /proc/cpuinfo 2>/dev/null | cut -d':' -f2 | xargs)

    echo "[tablerow:Model|$cpu_model]"
    echo "[tablerow:Cores/Threads|$cpu_cores cores]"
    echo "[tablerow:Physical CPUs|$cpu_physical socket(s)]"
    [ -n "$cpu_cache" ] && echo "[tablerow:Cache Size|$cpu_cache]"

    # CPU frequency
    if [ -f /sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq ]; then
        cur_freq=$(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq 2>/dev/null)
        max_freq=$(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_max_freq 2>/dev/null)
        if [ -n "$cur_freq" ] && [ -n "$max_freq" ]; then
            cur_mhz=$((cur_freq / 1000))
            max_mhz=$((max_freq / 1000))
            echo "[tablerow:Frequency|${cur_mhz} MHz (max: ${max_mhz} MHz)]"
        fi
    fi

    # Memory Information
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Memory Information:[/]"
    echo "[table:Property|Value]"

    mem_total=$(grep MemTotal /proc/meminfo | awk '{printf "%.2f GB", $2/1024/1024}')
    mem_available=$(grep MemAvailable /proc/meminfo | awk '{printf "%.2f GB", $2/1024/1024}')
    echo "[tablerow:Total Memory|$mem_total]"
    echo "[tablerow:Available|$mem_available]"

    # Memory hardware info (requires sudo)
    if command -v dmidecode &> /dev/null && sudo -n true 2>/dev/null; then
        mem_type=$(sudo dmidecode -t memory 2>/dev/null | grep -m1 "Type:" | awk '{print $2}')
        mem_speed=$(sudo dmidecode -t memory 2>/dev/null | grep -m1 "Speed:" | awk '{print $2, $3}')
        [ -n "$mem_type" ] && echo "[tablerow:Type|$mem_type @ $mem_speed]"
    fi

    # Storage Devices
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Storage Devices:[/]"
    echo "[table:Device|Size|Type|Model]"
    lsblk -d -o NAME,SIZE,ROTA,MODEL 2>/dev/null | tail -n +2 | while read -r name size rota model; do
        [ -z "$name" ] && continue
        type_str="SSD"
        [ "$rota" = "1" ] && type_str="HDD"
        model_short=$(echo "$model" | cut -c1-30)
        echo "[tablerow:/dev/$name|$size|$type_str|$model_short]"
    done

    # Network Interfaces
    echo "row: "
    echo "row: [divider]"
    echo "row: "
    echo "row: [bold]Network Interfaces:[/]"
    echo "[table:Interface|Status|IP Address|MAC Address]"
    for iface in /sys/class/net/*/; do
        iface_name=$(basename "$iface")
        [ "$iface_name" = "lo" ] && continue

        mac=$(cat "$iface/address" 2>/dev/null)
        state=$(cat "$iface/operstate" 2>/dev/null)
        ip_addr=$(ip -4 addr show "$iface_name" 2>/dev/null | grep -oP 'inet \K[\d.]+' | head -n1)
        [ -z "$ip_addr" ] && ip_addr="-"

        if [ "$state" = "up" ]; then
            echo "[tablerow:$iface_name|[green]$state[/]|$ip_addr|$mac]"
        else
            echo "[tablerow:$iface_name|[grey70]$state[/]|$ip_addr|$mac]"
        fi
    done

    # Logged in users
    if [ "$users_count" -gt 0 ]; then
        echo "row: "
        echo "row: [divider]"
        echo "row: "
        echo "row: [bold]Logged In Users:[/]"
        echo "[table:User|Terminal|Login Time|From]"
        who 2>/dev/null | while read -r user tty time rest; do
            login_from=$(echo "$rest" | grep -oP '\(\K[^)]+' || echo "local")
            echo "[tablerow:$user|$tty|$time|$login_from]"
        done
    fi

    # System hardware details
    echo "row: "
    echo "row: [divider:─:cyan1]"
    echo "row: "
    echo "row: [bold]Hardware Details:[/]"

    if [ -f /sys/class/dmi/id/product_name ]; then
        product=$(cat /sys/class/dmi/id/product_name 2>/dev/null)
        echo "row: [grey70]Product: $product[/]"
    fi

    if [ -f /sys/class/dmi/id/bios_version ]; then
        bios_version=$(cat /sys/class/dmi/id/bios_version 2>/dev/null)
        bios_date=$(cat /sys/class/dmi/id/bios_date 2>/dev/null)
        echo "row: [grey70]BIOS: $bios_version ($bios_date)[/]"
    fi

    # Motherboard info
    if [ -f /sys/class/dmi/id/board_vendor ]; then
        board_vendor=$(cat /sys/class/dmi/id/board_vendor 2>/dev/null)
        board_name=$(cat /sys/class/dmi/id/board_name 2>/dev/null)
        echo "row: [grey70]Motherboard: $board_vendor $board_name[/]"
    fi
fi

# Actions
echo "action: View hardware info:lscpu && echo '---' && free -h && echo '---' && lsblk"
echo "action: View PCI devices:lspci 2>/dev/null | head -30"
echo "action: View USB devices:lsusb 2>/dev/null"
echo "action: System details:uname -a && cat /etc/os-release"
echo "action: [sudo,danger] Reboot system:reboot"
echo "action: [sudo,danger] Shutdown system:shutdown -h now"
