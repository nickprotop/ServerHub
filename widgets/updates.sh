#!/bin/bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

echo "title: System Updates"
echo "refresh: 3600"

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
            # Count upgradable packages
            updates=$(apt list --upgradable 2>/dev/null | grep -c upgradable)
            security=$(apt list --upgradable 2>/dev/null | grep -c security)

            if [ "$security" -gt 0 ]; then
                echo "row: [status:error] $security security update(s) available"
            fi

            if [ "$updates" -gt 0 ]; then
                non_security=$((updates - security))
                if [ "$non_security" -gt 0 ]; then
                    echo "row: [status:warn] $non_security package update(s) available"
                fi

                echo "row: "
                echo "row: [grey70]Total updates: $updates[/]"

                # Show first 5 packages
                echo "row: "
                echo "row: [bold]Top packages:[/]"
                apt list --upgradable 2>/dev/null | grep -v "^Listing" | head -n 5 | while IFS= read -r line; do
                    pkg_name=$(echo "$line" | cut -d'/' -f1)
                    echo "row: [grey70]  • $pkg_name[/]"
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

elif command -v dnf &> /dev/null; then
    # Fedora/RHEL (dnf)
    echo "row: [bold]DNF Package Manager[/]"
    echo "row: "

    # Check for updates
    updates=$(dnf check-update --quiet 2>/dev/null | wc -l || echo "0")

    if [ "$updates" -gt 0 ]; then
        # Check for security updates
        security=$(dnf updateinfo list --security 2>/dev/null | grep -c "^FEDORA" || echo "0")

        if [ "$security" -gt 0 ]; then
            echo "row: [status:error] $security security update(s) available"
        fi

        echo "row: [status:warn] $updates update(s) available"

        echo "row: "
        echo "row: [grey70]Total updates: $updates[/]"
    else
        echo "row: [status:ok] System is up to date"
    fi

elif command -v yum &> /dev/null; then
    # CentOS/RHEL (yum)
    echo "row: [bold]YUM Package Manager[/]"
    echo "row: "

    # Check for updates
    updates=$(yum check-update --quiet 2>/dev/null | wc -l || echo "0")

    if [ "$updates" -gt 0 ]; then
        echo "row: [status:warn] $updates update(s) available"
    else
        echo "row: [status:ok] System is up to date"
    fi

elif command -v pacman &> /dev/null; then
    # Arch Linux (pacman)
    echo "row: [bold]Pacman Package Manager[/]"
    echo "row: "

    # Check for updates
    updates=$(checkupdates 2>/dev/null | wc -l || echo "0")

    if [ "$updates" -gt 0 ]; then
        echo "row: [status:warn] $updates update(s) available"

        # Show first 5 packages
        echo "row: "
        echo "row: [bold]Top packages:[/]"
        checkupdates 2>/dev/null | head -n 5 | while IFS= read -r line; do
            pkg_name=$(echo "$line" | awk '{print $1}')
            echo "row: [grey70]  • $pkg_name[/]"
        done
    else
        echo "row: [status:ok] System is up to date"
    fi

elif command -v zypper &> /dev/null; then
    # openSUSE (zypper)
    echo "row: [bold]Zypper Package Manager[/]"
    echo "row: "

    # Check for updates
    updates=$(zypper list-updates 2>/dev/null | grep -c "^v |" || echo "0")

    if [ "$updates" -gt 0 ]; then
        # Check for security patches
        security=$(zypper list-patches --category security 2>/dev/null | grep -c "^Patch" || echo "0")

        if [ "$security" -gt 0 ]; then
            echo "row: [status:error] $security security patch(es) available"
        fi

        echo "row: [status:warn] $updates update(s) available"
    else
        echo "row: [status:ok] System is up to date"
    fi

else
    echo "row: [status:error] No supported package manager found"
    echo "row: [grey70]Supported: apt, dnf, yum, pacman, zypper[/]"
fi

# Check for system reboot requirement (if available)
echo "row: "
if [ -f /var/run/reboot-required ]; then
    echo "row: [status:error] System reboot required"

    if [ -f /var/run/reboot-required.pkgs ]; then
        echo "row: [grey70]Due to package updates[/]"
    fi
elif [ -f /var/run/reboot-required.pkgs ]; then
    echo "row: [status:warn] Reboot recommended"
fi

# Show last update check time
echo "row: "
echo "row: [grey70]Last check: $(date '+%Y-%m-%d %H:%M')[/]"
