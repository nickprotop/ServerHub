#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

echo "title: System Info"
echo "refresh: 60"  # Increased from 30s - mostly static data

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
uptime_info=$(uptime -p 2>/dev/null | sed 's/up //')
if [ -z "$uptime_info" ]; then
    uptime_info=$(awk '{print int($1/86400)"d "int(($1%86400)/3600)"h "int(($1%3600)/60)"m"}' /proc/uptime)
fi
echo "row: [green]⏱[/] Uptime: [cyan1]$uptime_info[/]"

# Last boot time
last_boot=$(who -b 2>/dev/null | awk '{print $3, $4}')
if [ -n "$last_boot" ]; then
    echo "row: Last boot: [grey70]$last_boot[/]"
fi

echo "row: "

# Users logged in
users_count=$(who 2>/dev/null | wc -l)
if [ "$users_count" -gt 0 ]; then
    users_list=$(who | awk '{print $1}' | sort -u | tr '\n' ' ')
    echo "row: Users logged in: [cyan1]$users_count[/]"
    echo "row: [grey70]$users_list[/]"
else
    echo "row: Users logged in: [grey70]0[/]"
fi

# Extended mode: detailed hardware information
if [ "$EXTENDED" = true ]; then
    echo "row: "
    echo "row: [bold]CPU Information:[/]"

    # CPU model
    cpu_model=$(grep -m1 "model name" /proc/cpuinfo 2>/dev/null | cut -d':' -f2 | xargs)
    echo "row: [grey70]Model: $cpu_model[/]"

    # CPU cores and threads
    cpu_cores=$(grep -c "^processor" /proc/cpuinfo 2>/dev/null)
    cpu_physical=$(grep "^physical id" /proc/cpuinfo 2>/dev/null | sort -u | wc -l)
    [ "$cpu_physical" -eq 0 ] && cpu_physical=1
    echo "row: [grey70]Cores: $cpu_cores (${cpu_physical} socket(s))[/]"

    # CPU cache
    cpu_cache=$(grep -m1 "cache size" /proc/cpuinfo 2>/dev/null | cut -d':' -f2 | xargs)
    [ -n "$cpu_cache" ] && echo "row: [grey70]Cache: $cpu_cache[/]"

    # CPU frequency
    if [ -f /sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq ]; then
        cur_freq=$(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq 2>/dev/null)
        max_freq=$(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_max_freq 2>/dev/null)
        if [ -n "$cur_freq" ]; then
            cur_mhz=$((cur_freq / 1000))
            max_mhz=$((max_freq / 1000))
            echo "row: [grey70]Frequency: ${cur_mhz}MHz / ${max_mhz}MHz max[/]"
        fi
    fi

    # Memory hardware info
    echo "row: "
    echo "row: [bold]Memory Information:[/]"
    mem_total=$(grep MemTotal /proc/meminfo | awk '{printf "%.1f GB", $2/1024/1024}')
    echo "row: [grey70]Total: $mem_total[/]"

    if command -v dmidecode &> /dev/null; then
        mem_slots=$(sudo dmidecode -t memory 2>/dev/null | grep -c "Size:.*MB\|Size:.*GB" || echo "?")
        mem_type=$(sudo dmidecode -t memory 2>/dev/null | grep -m1 "Type:" | awk '{print $2}')
        mem_speed=$(sudo dmidecode -t memory 2>/dev/null | grep -m1 "Speed:" | awk '{print $2, $3}')
        [ -n "$mem_type" ] && echo "row: [grey70]Type: $mem_type @ $mem_speed[/]"
    fi

    # Disk hardware
    echo "row: "
    echo "row: [bold]Storage Devices:[/]"
    lsblk -d -o NAME,SIZE,MODEL,ROTA 2>/dev/null | tail -n +2 | while read -r name size model rota; do
        [ -z "$name" ] && continue
        type_str="SSD"
        [ "$rota" = "1" ] && type_str="HDD"
        model_short=$(echo "$model" | cut -c1-25)
        echo "row: [grey70]/dev/$name: $size $type_str $model_short[/]"
    done

    # Network interfaces
    echo "row: "
    echo "row: [bold]Network Interfaces:[/]"
    for iface in /sys/class/net/*/; do
        iface_name=$(basename "$iface")
        [ "$iface_name" = "lo" ] && continue

        mac=$(cat "$iface/address" 2>/dev/null)
        state=$(cat "$iface/operstate" 2>/dev/null)
        ip_addr=$(ip -4 addr show "$iface_name" 2>/dev/null | grep -oP 'inet \K[\d.]+' | head -n1)

        if [ "$state" = "up" ]; then
            echo "row: [status:ok] [grey70]$iface_name: $ip_addr ($mac)[/]"
        else
            echo "row: [grey70]$iface_name: $state ($mac)[/]"
        fi
    done

    # Virtualization
    echo "row: "
    echo "row: [bold]System Type:[/]"
    if [ -f /sys/class/dmi/id/product_name ]; then
        product=$(cat /sys/class/dmi/id/product_name 2>/dev/null)
        echo "row: [grey70]Product: $product[/]"
    fi

    virt_type="Physical"
    if grep -q "hypervisor" /proc/cpuinfo 2>/dev/null; then
        if [ -f /sys/class/dmi/id/sys_vendor ]; then
            vendor=$(cat /sys/class/dmi/id/sys_vendor 2>/dev/null)
            case "$vendor" in
                *VMware*) virt_type="VMware" ;;
                *Microsoft*) virt_type="Hyper-V" ;;
                *QEMU*|*KVM*) virt_type="KVM/QEMU" ;;
                *Xen*) virt_type="Xen" ;;
                *) virt_type="Virtual" ;;
            esac
        else
            virt_type="Virtual"
        fi
    fi
    echo "row: [grey70]Type: $virt_type[/]"

    # BIOS info
    if [ -f /sys/class/dmi/id/bios_version ]; then
        bios_version=$(cat /sys/class/dmi/id/bios_version 2>/dev/null)
        bios_date=$(cat /sys/class/dmi/id/bios_date 2>/dev/null)
        echo "row: [grey70]BIOS: $bios_version ($bios_date)[/]"
    fi

    # Timezone and locale
    echo "row: "
    echo "row: [bold]Locale:[/]"
    timezone=$(timedatectl 2>/dev/null | grep "Time zone" | awk '{print $3}')
    [ -z "$timezone" ] && timezone=$(cat /etc/timezone 2>/dev/null)
    echo "row: [grey70]Timezone: $timezone[/]"
    echo "row: [grey70]Time: $(date '+%Y-%m-%d %H:%M:%S')[/]"
fi

# Actions
echo "action: View hardware info:lscpu && echo '---' && free -h && echo '---' && lsblk"
echo "action: View PCI devices:lspci 2>/dev/null | head -30"
echo "action: View USB devices:lsusb 2>/dev/null"
echo "action: [sudo,danger] Reboot system:reboot"
echo "action: [sudo,danger] Shutdown system:shutdown -h now"
