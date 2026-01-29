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
    pkg_manager="APT"

    # Check if apt cache exists and is recent
    if [ -f /var/cache/apt/pkgcache.bin ]; then
        cache_age=$(( ($(date +%s) - $(stat -c %Y /var/cache/apt/pkgcache.bin)) / 3600 ))

        if [ "$cache_age" -gt 24 ]; then
            echo "row: [status:warn] Package cache is ${cache_age}h old"
            echo "row: [grey70]Run 'apt update' to refresh[/]"
        else
            # Count upgradable packages (single call, store result)
            upgradable_list=$(apt list --upgradable 2>/dev/null | grep -v "^Listing")
            total_updates=$(echo "$upgradable_list" | grep -c upgradable)
            security_updates=$(echo "$upgradable_list" | grep -c security)

            if [ "$EXTENDED" = false ]; then
                # Dashboard mode: compact tables
                if [ "$security_updates" -gt 0 ]; then
                    echo "row: [status:error] $security_updates security | $total_updates total updates"
                elif [ "$total_updates" -gt 50 ]; then
                    echo "row: [status:warn] $total_updates updates available"
                elif [ "$total_updates" -gt 0 ]; then
                    echo "row: [status:info] $total_updates updates available"
                else
                    echo "row: [status:ok] System is up to date"
                fi
                echo "row: "

                # Update summary table
                echo "row: [bold]Update Summary:[/]"
                echo "[table:Type|Count|Cache Age]"
                if [ "$security_updates" -gt 0 ]; then
                    echo "[tablerow:[red]Security[/]|$security_updates|${cache_age}h]"
                fi
                non_security=$((total_updates - security_updates))
                if [ "$non_security" -gt 0 ]; then
                    echo "[tablerow:[yellow]Regular[/]|$non_security|${cache_age}h]"
                fi
                [ "$total_updates" -eq 0 ] && echo "[tablerow:[green]All up to date[/]|0|${cache_age}h]"

                # Top packages table (show first 5)
                if [ "$total_updates" -gt 0 ]; then
                    echo "row: "
                    echo "row: [bold]Top Packages:[/]"
                    echo "[table:Package|Current → New]"
                    echo "$upgradable_list" | head -n 5 | while IFS= read -r line; do
                        pkg_name=$(echo "$line" | cut -d'/' -f1 | cut -c1-20)
                        version_info=$(echo "$line" | grep -oP '\[.*\]' | head -n1 | tr -d '[]')
                        [ -n "$pkg_name" ] && echo "[tablerow:$pkg_name|$version_info]"
                    done

                    # Show indicator if more packages exist
                    if [ "$total_updates" -gt 5 ]; then
                        remaining=$((total_updates - 5))
                        echo "row: "
                        echo "row: [grey70]... and $remaining more packages[/]"
                    fi
                fi
            else
                # Extended mode: detailed tables
                echo "row: [status:$([ "$security_updates" -gt 0 ] && echo "error" || ([ "$total_updates" -gt 0 ] && echo "warn" || echo "ok"))] Security: [red]$security_updates[/] | Total: [yellow]$total_updates[/]"
                echo "row: "

                # All upgradable packages table
                echo "row: [bold]All Upgradable Packages:[/]"
                echo "[table:Package|Current|New|Type]"
                if [ "$total_updates" -gt 0 ]; then
                    echo "$upgradable_list" | head -n 30 | while IFS= read -r line; do
                        pkg_name=$(echo "$line" | cut -d'/' -f1 | cut -c1-25)
                        if echo "$line" | grep -qi "security"; then
                            pkg_type="[red]Security[/]"
                        else
                            pkg_type="Regular"
                        fi

                        # Extract versions
                        current=$(echo "$line" | grep -oP '\[upgradable from: \K[^\]]+')
                        new=$(echo "$line" | awk '{print $2}')

                        [ -n "$pkg_name" ] && echo "[tablerow:$pkg_name|$current|$new|$pkg_type]"
                    done

                    if [ "$total_updates" -gt 30 ]; then
                        remaining=$((total_updates - 30))
                        echo "row: "
                        echo "row: [grey70]... and $remaining more packages[/]"
                    fi
                else
                    echo "[tablerow:No updates available|-|-|-]"
                fi

                # Package statistics table
                echo "row: "
                echo "row: [divider]"
                echo "row: "
                echo "row: [bold]Package Statistics:[/]"
                echo "[table:Property|Value]"
                installed=$(dpkg -l 2>/dev/null | grep -c "^ii" || echo "?")
                echo "[tablerow:Installed Packages|$installed]"
                echo "[tablerow:Upgradable|$total_updates]"
                echo "[tablerow:Security Updates|$security_updates]"

                # Held packages
                held=$(apt-mark showhold 2>/dev/null | wc -l)
                [ "$held" -gt 0 ] && echo "[tablerow:Held Packages|[yellow]$held[/]]"

                # Auto-removable
                autoremove=$(apt list --autoremove 2>/dev/null | grep -c "autoremove" || echo 0)
                [ "$autoremove" -gt 0 ] && echo "[tablerow:Auto-removable|[yellow]$autoremove[/]]"

                # Cache age
                echo "[tablerow:Cache Age|${cache_age}h]"

                # Recent updates table
                echo "row: "
                echo "row: [divider]"
                echo "row: "
                echo "row: [bold]Recent Updates:[/]"
                echo "[table:Date|Action]"
                if [ -f /var/log/apt/history.log ]; then
                    grep "End-Date:" /var/log/apt/history.log 2>/dev/null | tail -n 10 | while read -r line; do
                        date_str=$(echo "$line" | cut -d: -f2- | xargs | cut -c1-35)
                        echo "[tablerow:[grey70]$date_str[/]|Update completed]"
                    done
                else
                    echo "[tablerow:No history available|-]"
                fi

                # Repositories table
                echo "row: "
                echo "row: [divider]"
                echo "row: "
                echo "row: [bold]Repositories:[/]"
                echo "[table:Type|Repository]"
                grep -r "^deb " /etc/apt/sources.list /etc/apt/sources.list.d/*.list 2>/dev/null | head -n 10 | while read -r line; do
                    repo_type=$(echo "$line" | awk '{print $2}')
                    repo_url=$(echo "$line" | awk '{print $3}' | sed 's|.*//||' | cut -d'/' -f1 | cut -c1-30)
                    echo "[tablerow:$repo_type|$repo_url]"
                done

                # Statistics
                echo "row: "
                echo "row: [divider:─:cyan1]"
                echo "row: "
                echo "row: [bold]Statistics:[/]"
                echo "row: [grey70]Package manager: APT[/]"
                echo "row: [grey70]Last cache update: ${cache_age}h ago[/]"
                echo "row: [grey70]Total installed: $installed packages[/]"
            fi
        fi
    else
        echo "row: [status:warn] APT cache not found"
        echo "row: [grey70]Run 'apt update' first[/]"
    fi

elif command -v dnf &> /dev/null; then
    # Fedora/RHEL (dnf)
    pkg_manager="DNF"

    # Check for updates
    update_list=$(dnf check-update --quiet 2>/dev/null)
    total_updates=$(echo "$update_list" | grep -c "." || echo "0")

    if [ "$EXTENDED" = false ]; then
        # Dashboard mode
        if [ "$total_updates" -gt 0 ]; then
            security_updates=$(dnf updateinfo list --security 2>/dev/null | grep -c "^FEDORA" || echo "0")

            if [ "$security_updates" -gt 0 ]; then
                echo "row: [status:error] $security_updates security | $total_updates total"
            else
                echo "row: [status:warn] $total_updates updates available"
            fi
        else
            echo "row: [status:ok] System is up to date"
        fi
        echo "row: "

        echo "row: [bold]Update Summary:[/]"
        echo "[table:Type|Count]"
        [ "$security_updates" -gt 0 ] && echo "[tablerow:[red]Security[/]|$security_updates]"
        [ "$total_updates" -gt 0 ] && echo "[tablerow:[yellow]Total[/]|$total_updates]"
        [ "$total_updates" -eq 0 ] && echo "[tablerow:[green]All up to date[/]|0]"
    else
        # Extended mode
        echo "row: [status:$([ "$security_updates" -gt 0 ] && echo "error" || ([ "$total_updates" -gt 0 ] && echo "warn" || echo "ok"))] Security: [red]$security_updates[/] | Total: [yellow]$total_updates[/]"
        echo "row: "

        echo "row: [bold]Upgradable Packages:[/]"
        echo "[table:Package]"
        echo "$update_list" | head -n 20 | while read -r line; do
            pkg=$(echo "$line" | awk '{print $1}' | cut -c1-40)
            [ -n "$pkg" ] && echo "[tablerow:$pkg]"
        done

        installed=$(rpm -qa 2>/dev/null | wc -l || echo "?")
        echo "row: "
        echo "row: [grey70]Installed packages: $installed[/]"
    fi

elif command -v pacman &> /dev/null; then
    # Arch Linux (pacman)
    pkg_manager="Pacman"

    update_list=$(checkupdates 2>/dev/null)
    total_updates=$(echo "$update_list" | grep -c "." || echo "0")

    if [ "$EXTENDED" = false ]; then
        if [ "$total_updates" -gt 0 ]; then
            echo "row: [status:warn] $total_updates updates available"
        else
            echo "row: [status:ok] System is up to date"
        fi
        echo "row: "

        echo "row: [bold]Top Packages:[/]"
        echo "[table:Package]"
        echo "$update_list" | head -n 5 | while IFS= read -r line; do
            pkg_name=$(echo "$line" | awk '{print $1}' | cut -c1-25)
            echo "[tablerow:$pkg_name]"
        done
    else
        echo "row: [status:$([ "$total_updates" -gt 0 ] && echo "warn" || echo "ok")] Total: [yellow]$total_updates[/]"
        echo "row: "

        echo "row: [bold]All Updates:[/]"
        echo "[table:Package|Version]"
        echo "$update_list" | head -n 30 | while read -r line; do
            pkg=$(echo "$line" | awk '{print $1}' | cut -c1-25)
            ver=$(echo "$line" | awk '{print $4}' | cut -c1-20)
            echo "[tablerow:$pkg|$ver]"
        done

        installed=$(pacman -Q 2>/dev/null | wc -l || echo "?")
        orphans=$(pacman -Qdt 2>/dev/null | wc -l || echo 0)
        echo "row: "
        echo "row: [grey70]Installed packages: $installed[/]"
        [ "$orphans" -gt 0 ] && echo "row: [yellow]Orphan packages: $orphans[/]"
    fi

else
    echo "row: [status:error] No supported package manager found"
    echo "row: [grey70]Supported: apt, dnf, yum, pacman, zypper[/]"
fi

# Check for system reboot requirement
if [ -f /var/run/reboot-required ]; then
    echo "row: "
    echo "row: [status:error] [red]⚠[/] System reboot required"
    reboot_required=true

    if [ -f /var/run/reboot-required.pkgs ]; then
        echo "row: [grey70]Due to package updates[/]"
    fi
elif [ -f /var/run/reboot-required.pkgs ]; then
    echo "row: "
    echo "row: [status:warn] Reboot recommended"
    reboot_required=true
fi

# APT Actions
if command -v apt &> /dev/null; then
    echo "action: [sudo,refresh,timeout=120] Update package cache:apt update"
    if [ "$total_updates" -gt 0 ]; then
        echo "action: [sudo,danger,refresh,timeout=600] Upgrade packages:apt upgrade -y"
        echo "action: [sudo,danger,refresh,timeout=600] Full upgrade:apt full-upgrade -y"
    fi
    autoremove=$(apt list --autoremove 2>/dev/null | grep -c "autoremove" || echo 0)
    if [ "$autoremove" -gt 0 ] 2>/dev/null; then
        echo "action: [sudo,refresh,timeout=120] Auto-remove unused:apt autoremove -y"
    fi
fi

# DNF Actions
if command -v dnf &> /dev/null; then
    echo "action: [sudo,refresh,timeout=120] Check for updates:dnf check-update"
    if [ "$total_updates" -gt 0 ]; then
        echo "action: [sudo,danger,refresh,timeout=600] Upgrade packages:dnf upgrade -y"
    fi
fi

# Pacman Actions
if command -v pacman &> /dev/null; then
    echo "action: [sudo,refresh,timeout=120] Sync database:pacman -Sy"
    if [ "$total_updates" -gt 0 ]; then
        echo "action: [sudo,danger,refresh,timeout=600] Upgrade system:pacman -Syu --noconfirm"
    fi
fi

# Reboot action if required
if [ "$reboot_required" = true ]; then
    echo "action: [sudo,danger] Reboot system:reboot"
fi

echo "action: View update history:cat /var/log/apt/history.log 2>/dev/null | tail -100 || echo 'No history available'"
