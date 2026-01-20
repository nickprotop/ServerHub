#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

# Check for extended mode
EXTENDED=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED=true
fi

echo "title: System Updates"
echo "refresh: 3600"

# Store update counts for actions
total_updates=0
security_updates=0
reboot_required=false

# Detect package manager and check for updates
if command -v apt &> /dev/null; then
    # Debian/Ubuntu (apt)
    echo "row: [bold]APT Package Manager[/]"
    echo "row: "

    # Check if apt cache exists and is recent
    if [ -f /var/cache/apt/pkgcache.bin ]; then
        cache_age=$(( ($(date +%s) - $(stat -c %Y /var/cache/apt/pkgcache.bin)) / 3600 ))

        if [ "$cache_age" -gt 24 ]; then
            echo "row: [status:warn] Package cache is ${cache_age}h old"
            echo "row: [grey70]  Run 'apt update' to refresh[/]"
        else
            # Count upgradable packages (single call, store result)
            upgradable_list=$(apt list --upgradable 2>/dev/null | grep -v "^Listing")
            total_updates=$(echo "$upgradable_list" | grep -c upgradable)
            security_updates=$(echo "$upgradable_list" | grep -c security)

            if [ "$security_updates" -gt 0 ]; then
                echo "row: [status:error] $security_updates security update(s) available"
            fi

            if [ "$total_updates" -gt 0 ]; then
                non_security=$((total_updates - security_updates))
                if [ "$non_security" -gt 0 ]; then
                    echo "row: [status:warn] $non_security package update(s) available"
                fi

                echo "row: "
                echo "row: [grey70]Total updates: $total_updates[/]"

                # Show first 5 packages in standard mode
                echo "row: "
                echo "row: [bold]Top packages:[/]"
                echo "$upgradable_list" | head -n 5 | while IFS= read -r line; do
                    pkg_name=$(echo "$line" | cut -d'/' -f1)
                    [ -n "$pkg_name" ] && echo "row: [grey70]  - $pkg_name[/]"
                done
            else
                echo "row: [status:ok] System is up to date"
            fi

            echo "row: "
            echo "row: [grey70]Cache age: ${cache_age}h[/]"
        fi
    else
        echo "row: [status:warn] APT cache not found"
        echo "row: [grey70]Run 'apt update' first[/]"
    fi

    # Extended mode: detailed APT information
    if [ "$EXTENDED" = true ]; then
        echo "row: "
        echo "row: [bold]All Upgradable Packages:[/]"
        if [ "$total_updates" -gt 0 ]; then
            echo "$upgradable_list" | head -n 30 | while IFS= read -r line; do
                pkg_name=$(echo "$line" | cut -d'/' -f1)
                version_info=$(echo "$line" | grep -oP '\[.*\]' | head -n1)
                [ -n "$pkg_name" ] && echo "row: [grey70]$pkg_name $version_info[/]"
            done
            if [ "$total_updates" -gt 30 ]; then
                remaining=$((total_updates - 30))
                echo "row: [grey70]... and $remaining more[/]"
            fi
        else
            echo "row: [grey70]No updates available[/]"
        fi

        # Package statistics
        echo "row: "
        echo "row: [bold]Package Statistics:[/]"
        installed=$(dpkg -l 2>/dev/null | grep -c "^ii" || echo "?")
        echo "row: [grey70]Installed packages: $installed[/]"

        # Held packages
        held=$(apt-mark showhold 2>/dev/null | wc -l)
        [ "$held" -gt 0 ] && echo "row: [grey70]Held packages: $held[/]"

        # Auto-removable
        autoremove=$(apt list --autoremove 2>/dev/null | grep -c "autoremove" || echo 0)
        [ "$autoremove" -gt 0 ] && echo "row: [yellow]Auto-removable: $autoremove[/]"

        # APT history
        echo "row: "
        echo "row: [bold]Recent Updates:[/]"
        if [ -f /var/log/apt/history.log ]; then
            grep "End-Date:" /var/log/apt/history.log 2>/dev/null | tail -n 5 | while read -r line; do
                date_str=$(echo "$line" | cut -d: -f2- | xargs)
                echo "row: [grey70]$date_str[/]"
            done
        else
            echo "row: [grey70]No history available[/]"
        fi

        # Repositories
        echo "row: "
        echo "row: [bold]Repositories:[/]"
        grep -r "^deb " /etc/apt/sources.list /etc/apt/sources.list.d/*.list 2>/dev/null | head -n 5 | while read -r line; do
            repo=$(echo "$line" | awk '{print $2}' | sed 's|.*//||' | cut -d'/' -f1)
            echo "row: [grey70]$repo[/]"
        done
    fi

    # APT Actions
    echo "action: [sudo,timeout=120] Update package cache:apt update"
    if [ "$total_updates" -gt 0 ]; then
        echo "action: [sudo,danger,timeout=600] Upgrade packages:apt upgrade -y"
        echo "action: [sudo,danger,timeout=600] Full upgrade:apt full-upgrade -y"
    fi
    if [ "$autoremove" -gt 0 ] 2>/dev/null; then
        echo "action: [sudo,timeout=120] Auto-remove unused:apt autoremove -y"
    fi

elif command -v dnf &> /dev/null; then
    # Fedora/RHEL (dnf)
    echo "row: [bold]DNF Package Manager[/]"
    echo "row: "

    # Check for updates
    update_list=$(dnf check-update --quiet 2>/dev/null)
    total_updates=$(echo "$update_list" | grep -c "." || echo "0")

    if [ "$total_updates" -gt 0 ]; then
        # Check for security updates
        security_updates=$(dnf updateinfo list --security 2>/dev/null | grep -c "^FEDORA" || echo "0")

        if [ "$security_updates" -gt 0 ]; then
            echo "row: [status:error] $security_updates security update(s) available"
        fi

        echo "row: [status:warn] $total_updates update(s) available"
        echo "row: "
        echo "row: [grey70]Total updates: $total_updates[/]"
    else
        echo "row: [status:ok] System is up to date"
    fi

    # Extended mode
    if [ "$EXTENDED" = true ]; then
        echo "row: "
        echo "row: [bold]Update Details:[/]"
        echo "$update_list" | head -n 20 | while read -r line; do
            pkg=$(echo "$line" | awk '{print $1}')
            [ -n "$pkg" ] && echo "row: [grey70]$pkg[/]"
        done

        echo "row: "
        echo "row: [bold]Package Statistics:[/]"
        installed=$(rpm -qa 2>/dev/null | wc -l || echo "?")
        echo "row: [grey70]Installed packages: $installed[/]"
    fi

    # DNF Actions
    echo "action: [sudo,timeout=120] Check for updates:dnf check-update"
    if [ "$total_updates" -gt 0 ]; then
        echo "action: [sudo,danger,timeout=600] Upgrade packages:dnf upgrade -y"
    fi

elif command -v yum &> /dev/null; then
    # CentOS/RHEL (yum)
    echo "row: [bold]YUM Package Manager[/]"
    echo "row: "

    # Check for updates
    total_updates=$(yum check-update --quiet 2>/dev/null | wc -l || echo "0")

    if [ "$total_updates" -gt 0 ]; then
        echo "row: [status:warn] $total_updates update(s) available"
    else
        echo "row: [status:ok] System is up to date"
    fi

    # YUM Actions
    echo "action: [sudo,timeout=120] Check for updates:yum check-update"
    if [ "$total_updates" -gt 0 ]; then
        echo "action: [sudo,danger,timeout=600] Upgrade packages:yum update -y"
    fi

elif command -v pacman &> /dev/null; then
    # Arch Linux (pacman)
    echo "row: [bold]Pacman Package Manager[/]"
    echo "row: "

    # Check for updates
    update_list=$(checkupdates 2>/dev/null)
    total_updates=$(echo "$update_list" | grep -c "." || echo "0")

    if [ "$total_updates" -gt 0 ]; then
        echo "row: [status:warn] $total_updates update(s) available"

        # Show first 5 packages
        echo "row: "
        echo "row: [bold]Top packages:[/]"
        echo "$update_list" | head -n 5 | while IFS= read -r line; do
            pkg_name=$(echo "$line" | awk '{print $1}')
            echo "row: [grey70]  - $pkg_name[/]"
        done
    else
        echo "row: [status:ok] System is up to date"
    fi

    # Extended mode
    if [ "$EXTENDED" = true ]; then
        echo "row: "
        echo "row: [bold]All Updates:[/]"
        echo "$update_list" | head -n 30 | while read -r line; do
            echo "row: [grey70]$line[/]"
        done

        echo "row: "
        echo "row: [bold]Package Statistics:[/]"
        installed=$(pacman -Q 2>/dev/null | wc -l || echo "?")
        echo "row: [grey70]Installed packages: $installed[/]"

        orphans=$(pacman -Qdt 2>/dev/null | wc -l || echo 0)
        [ "$orphans" -gt 0 ] && echo "row: [yellow]Orphan packages: $orphans[/]"
    fi

    # Pacman Actions
    echo "action: [sudo,timeout=120] Sync database:pacman -Sy"
    if [ "$total_updates" -gt 0 ]; then
        echo "action: [sudo,danger,timeout=600] Upgrade system:pacman -Syu --noconfirm"
    fi

elif command -v zypper &> /dev/null; then
    # openSUSE (zypper)
    echo "row: [bold]Zypper Package Manager[/]"
    echo "row: "

    # Check for updates
    total_updates=$(zypper list-updates 2>/dev/null | grep -c "^v |" || echo "0")

    if [ "$total_updates" -gt 0 ]; then
        # Check for security patches
        security_updates=$(zypper list-patches --category security 2>/dev/null | grep -c "^Patch" || echo "0")

        if [ "$security_updates" -gt 0 ]; then
            echo "row: [status:error] $security_updates security patch(es) available"
        fi

        echo "row: [status:warn] $total_updates update(s) available"
    else
        echo "row: [status:ok] System is up to date"
    fi

    # Zypper Actions
    echo "action: [sudo,timeout=120] Refresh repos:zypper refresh"
    if [ "$total_updates" -gt 0 ]; then
        echo "action: [sudo,danger,timeout=600] Update system:zypper update -y"
    fi

else
    echo "row: [status:error] No supported package manager found"
    echo "row: [grey70]Supported: apt, dnf, yum, pacman, zypper[/]"
fi

# Check for system reboot requirement (if available)
echo "row: "
if [ -f /var/run/reboot-required ]; then
    echo "row: [status:error] System reboot required"
    reboot_required=true

    if [ -f /var/run/reboot-required.pkgs ]; then
        echo "row: [grey70]Due to package updates[/]"
    fi
elif [ -f /var/run/reboot-required.pkgs ]; then
    echo "row: [status:warn] Reboot recommended"
    reboot_required=true
fi

# Show last update check time
echo "row: "
echo "row: [grey70]Last check: $(date '+%Y-%m-%d %H:%M')[/]"

# Reboot action if required
if [ "$reboot_required" = true ]; then
    echo "action: [sudo,danger] Reboot system:reboot"
fi

echo "action: View update history:cat /var/log/apt/history.log 2>/dev/null | tail -100 || echo 'No history available'"
