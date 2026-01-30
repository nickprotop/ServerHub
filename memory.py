#!/usr/bin/env python3
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.
#
# Memory Usage Widget - Python Implementation
# Proof of concept demonstrating language-agnostic widget protocol

import sys
import os
import subprocess
from pathlib import Path

# Try to import psutil, fall back to /proc parsing if not available
try:
    import psutil
    HAS_PSUTIL = True
except ImportError:
    HAS_PSUTIL = False


def parse_meminfo():
    """Parse /proc/meminfo into a dictionary (values in MB)."""
    meminfo = {}
    try:
        with open('/proc/meminfo', 'r') as f:
            for line in f:
                parts = line.split()
                if len(parts) >= 2:
                    key = parts[0].rstrip(':')
                    value = int(parts[1]) // 1024  # KB to MB
                    meminfo[key] = value
    except (IOError, ValueError):
        pass
    return meminfo


def get_memory_info():
    """Get memory info by parsing /proc/meminfo."""
    meminfo = parse_meminfo()

    total = meminfo.get('MemTotal', 0)
    available = meminfo.get('MemAvailable', 0)
    free = meminfo.get('MemFree', 0)
    buffers = meminfo.get('Buffers', 0)
    cached = meminfo.get('Cached', 0)

    # Calculate used memory (same as 'free' command)
    used = total - free - buffers - cached

    swap_total = meminfo.get('SwapTotal', 0)
    swap_free = meminfo.get('SwapFree', 0)
    swap_used = swap_total - swap_free

    return {
        'total': total,
        'used': used,
        'available': available,
        'percent': round(used * 100 / total) if total > 0 else 0,
        'buffers': buffers,
        'cached': cached,
        'swap_total': swap_total,
        'swap_used': swap_used,
        'swap_percent': round(swap_used * 100 / swap_total) if swap_total > 0 else 0,
        # Advanced details
        'active': meminfo.get('Active', 0),
        'inactive': meminfo.get('Inactive', 0),
        'dirty': meminfo.get('Dirty', 0),
        'slab': meminfo.get('Slab', 0),
        # Huge pages
        'huge_total': meminfo.get('HugePages_Total', 0),
        'huge_free': meminfo.get('HugePages_Free', 0),
        'huge_rsvd': meminfo.get('HugePages_Rsvd', 0),
        'huge_size': meminfo.get('Hugepagesize', 0),
    }


def get_shared_memory():
    """Get shared memory from 'free' command output."""
    try:
        result = subprocess.run(['free', '-m'], capture_output=True, text=True)
        for line in result.stdout.split('\n'):
            if line.startswith('Mem:'):
                parts = line.split()
                if len(parts) >= 5:
                    return int(parts[4])
    except (subprocess.SubprocessError, ValueError, IndexError):
        pass
    return 0


def get_swap_partitions():
    """Parse /proc/swaps for swap partition info."""
    partitions = []
    try:
        with open('/proc/swaps', 'r') as f:
            lines = f.readlines()[1:]  # Skip header
            for line in lines:
                parts = line.split()
                if len(parts) >= 5:
                    filename = parts[0]
                    swap_type = parts[1]
                    size = int(parts[2]) // 1024  # KB to MB
                    used = int(parts[3]) // 1024
                    priority = parts[4]
                    dev_name = os.path.basename(filename)
                    partitions.append({
                        'device': dev_name,
                        'type': swap_type,
                        'size': size,
                        'used': used,
                        'priority': priority,
                    })
    except (IOError, ValueError, IndexError):
        pass
    return partitions


def get_memory_pressure():
    """Get memory pressure stats from /proc/pressure/memory."""
    pressure = {}
    try:
        with open('/proc/pressure/memory', 'r') as f:
            for line in f:
                if line.startswith('some'):
                    # Parse: some avg10=0.00 avg60=0.00 avg300=0.00 total=0
                    for part in line.split():
                        if part.startswith('avg10='):
                            pressure['some_avg10'] = part.split('=')[1]
                        elif part.startswith('avg60='):
                            pressure['some_avg60'] = part.split('=')[1]
                elif line.startswith('full'):
                    for part in line.split():
                        if part.startswith('avg10='):
                            pressure['full_avg10'] = part.split('=')[1]
                        elif part.startswith('avg60='):
                            pressure['full_avg60'] = part.split('=')[1]
    except (IOError, FileNotFoundError):
        pass
    return pressure


def get_top_memory_processes(limit=10):
    """Get top memory-consuming processes using ps command."""
    processes = []
    try:
        result = subprocess.run(
            ['ps', 'aux', '--sort=-%mem'],
            capture_output=True, text=True
        )
        lines = result.stdout.strip().split('\n')[1:limit+1]  # Skip header
        for line in lines:
            parts = line.split()
            if len(parts) >= 11:
                pid = parts[1]
                mem_percent = float(parts[3])
                mem_kb = int(parts[5])
                mem_mb = mem_kb // 1024
                cmd = parts[10]
                # Strip path from command
                cmd = os.path.basename(cmd.split()[0] if cmd else 'unknown')
                if len(cmd) > 20:
                    cmd = cmd[:20]
                processes.append({
                    'pid': pid,
                    'name': cmd,
                    'percent': mem_percent,
                    'mem_mb': mem_mb,
                })
    except (subprocess.SubprocessError, ValueError, IndexError):
        pass
    return processes


def manage_history(cache_dir: Path, filename: str, value: float, max_samples: int):
    """Store value in history file and return comma-separated history."""
    history_file = cache_dir / filename

    # Read existing history
    history = []
    if history_file.exists():
        try:
            history = [float(x.strip()) for x in history_file.read_text().strip().split('\n') if x.strip()]
        except (ValueError, IOError):
            history = []

    # Append new value and trim
    history.append(value)
    history = history[-max_samples:]

    # Write back
    try:
        history_file.write_text('\n'.join(str(int(v)) for v in history) + '\n')
    except IOError:
        pass

    return ','.join(str(int(v)) for v in history)


def main():
    # Check for extended mode
    extended = '--extended' in sys.argv

    # Output protocol headers
    print("title: Memory Usage")
    print("refresh: 2")

    # Setup cache directory
    cache_dir = Path.home() / '.cache' / 'serverhub'
    cache_dir.mkdir(parents=True, exist_ok=True)

    # Get memory info
    mem = get_memory_info()
    shared = get_shared_memory()

    # Determine sample count based on mode
    max_samples = 30 if extended else 10

    # Store and get history
    mem_history = manage_history(cache_dir, 'memory-usage.txt', mem['percent'], max_samples)

    swap_history = None
    if mem['swap_total'] > 0:
        swap_history = manage_history(cache_dir, 'swap-usage.txt', mem['swap_percent'], max_samples)

    # Determine status
    if mem['percent'] < 70:
        status = 'ok'
    elif mem['percent'] < 90:
        status = 'warn'
    else:
        status = 'error'

    if not extended:
        # Dashboard mode: Compact overview with sparklines
        print(f"row: [status:{status}] Memory: {mem['used']}MB / {mem['total']}MB [sparkline:{mem_history}:yellow]")
        print(f"row: [progress:{int(mem['percent'])}]")
        print("row: ")

        # Memory breakdown table
        print("row: [bold]Memory Breakdown:[/]")
        print("[table:Type|Usage]")
        print(f"[tablerow:RAM Used|[miniprogress:{int(mem['percent'])}:12]]")

        if mem['swap_total'] > 0:
            print(f"[tablerow:Swap Used|[miniprogress:{int(mem['swap_percent'])}:12]]")
        else:
            print("[tablerow:Swap|[grey70]Not configured[/]]")

        cache_mb = mem['buffers'] + mem['cached']
        print(f"[tablerow:Cache|{cache_mb}MB]")
        print(f"[tablerow:Available|{mem['available']}MB]")

        # Quick memory info
        avail_percent = int(mem['available'] * 100 / mem['total']) if mem['total'] > 0 else 0
        print("row: ")
        print(f"row: [grey70]Available: {mem['available']}MB ({avail_percent}%)[/]")
    else:
        # Extended mode: Detailed view with graphs and tables
        print(f"row: [status:{status}] Memory: {mem['used']}MB / {mem['total']}MB ({int(mem['percent'])}%)")
        print(f"row: [progress:{int(mem['percent'])}]")
        print("row: ")
        print(f"row: Available: {mem['available']}MB")

        # Memory history graph
        print("row: ")
        print("row: [divider]")
        print("row: ")
        print("row: [bold]Memory Usage History (last 60s):[/]")
        print(f"row: [graph:{mem_history}:yellow:Memory %]")

        # Swap graph if configured
        if mem['swap_total'] > 0 and swap_history:
            print("row: ")
            print("row: [bold]Swap Usage History:[/]")
            print(f"row: [graph:{swap_history}:red:Swap %]")

        # Detailed breakdown table
        print("row: ")
        print("row: [divider]")
        print("row: ")
        print("row: [bold]Memory Breakdown:[/]")
        print("[table:Type|Size|Percentage]")

        print(f"[tablerow:Total RAM|{mem['total']}MB|100%]")
        print(f"[tablerow:Used|{mem['used']}MB|[miniprogress:{int(mem['percent'])}:10]]")
        avail_pct = int(mem['available'] * 100 / mem['total']) if mem['total'] > 0 else 0
        print(f"[tablerow:Available|{mem['available']}MB|{avail_pct}%]")
        buf_pct = int(mem['buffers'] * 100 / mem['total']) if mem['total'] > 0 else 0
        print(f"[tablerow:Buffers|{mem['buffers']}MB|{buf_pct}%]")
        cache_pct = int(mem['cached'] * 100 / mem['total']) if mem['total'] > 0 else 0
        print(f"[tablerow:Cache|{mem['cached']}MB|{cache_pct}%]")

        # Swap breakdown
        if mem['swap_total'] > 0:
            swap_avail = mem['swap_total'] - mem['swap_used']
            swap_avail_pct = int(swap_avail * 100 / mem['swap_total']) if mem['swap_total'] > 0 else 0
            print(f"[tablerow:Swap Total|{mem['swap_total']}MB|100%]")
            print(f"[tablerow:Swap Used|{mem['swap_used']}MB|[miniprogress:{int(mem['swap_percent'])}:10]]")
            print(f"[tablerow:Swap Free|{swap_avail}MB|{swap_avail_pct}%]")

        # Advanced memory details
        print("row: ")
        print("row: [divider]")
        print("row: ")
        print("row: [bold]Advanced Details:[/]")
        print("[table:Metric|Value]")
        print(f"[tablerow:Active|{mem['active']}MB]")
        print(f"[tablerow:Inactive|{mem['inactive']}MB]")
        print(f"[tablerow:Shared|{shared}MB]")
        print(f"[tablerow:Dirty|{mem['dirty']}MB]")
        print(f"[tablerow:Slab (kernel)|{mem['slab']}MB]")

        # Swap partitions
        if mem['swap_total'] > 0:
            partitions = get_swap_partitions()
            if partitions:
                print("row: ")
                print("row: [divider]")
                print("row: ")
                print("row: [bold]Swap Partitions:[/]")
                print("[table:Device|Type|Size|Used|Priority]")
                for p in partitions:
                    print(f"[tablerow:{p['device']}|{p['type']}|{p['size']}MB|{p['used']}MB|{p['priority']}]")

        # Top memory processes
        processes = get_top_memory_processes(10)
        if processes:
            print("row: ")
            print("row: [divider]")
            print("row: ")
            print("row: [bold]Top Memory Processes:[/]")
            print("[table:Process|Memory|Percent|PID]")
            for proc in processes:
                print(f"[tablerow:{proc['name']}|{proc['mem_mb']}MB|{proc['percent']:.1f}%|{proc['pid']}]")

        # Memory pressure (if available)
        pressure = get_memory_pressure()
        if pressure:
            print("row: ")
            print("row: [divider]")
            print("row: ")
            print("row: [bold]Memory Pressure:[/]")
            print("[table:Type|10s avg|60s avg]")
            print(f"[tablerow:Some stall|{pressure.get('some_avg10', '0.00')}%|{pressure.get('some_avg60', '0.00')}%]")
            print(f"[tablerow:Full stall|{pressure.get('full_avg10', '0.00')}%|{pressure.get('full_avg60', '0.00')}%]")

        # Huge pages (if configured)
        if mem['huge_total'] > 0:
            print("row: ")
            print("row: [divider:─:cyan1]")
            print("row: ")
            print("row: [bold]Huge Pages:[/]")
            print("[table:Metric|Value]")
            print(f"[tablerow:Total|{mem['huge_total']}]")
            print(f"[tablerow:Free|{mem['huge_free']}]")
            print(f"[tablerow:Reserved|{mem['huge_rsvd']}]")
            print(f"[tablerow:Page Size|{mem['huge_size']}KB]")

    # Actions (context-based)
    if mem['percent'] > 90:
        processes = get_top_memory_processes(1)
        if processes:
            p = processes[0]
            print(f"action: [sudo,danger,refresh] Kill {p['name']} ({p['pid']}):kill -9 {p['pid']}")

    if mem['swap_total'] > 0 and mem['swap_percent'] > 50:
        print("action: [sudo,danger,refresh] Clear swap:swapoff -a && swapon -a")

    print("action: [sudo,refresh] Drop caches:sh -c 'sync && echo 3 > /proc/sys/vm/drop_caches' && echo 'Caches dropped'")
    print("action: View memory map:cat /proc/meminfo")
    print("action: Show OOM killer history:dmesg | grep -i 'killed process' | tail -10")
    print(f"action: Clear memory history:rm -f {cache_dir}/memory-usage.txt {cache_dir}/swap-usage.txt")


if __name__ == '__main__':
    main()
