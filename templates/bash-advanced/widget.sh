#!/bin/bash
# {{WIDGET_TITLE}}
# {{DESCRIPTION}}
# Author: {{AUTHOR}}

CACHE_FILE="{{CACHE_FILE}}"
MAX_SAMPLES={{MAX_SAMPLES}}

# Ensure cache directory exists
mkdir -p "$(dirname "$CACHE_FILE")"

# Check if running in extended mode
EXTENDED_MODE=false
if [[ "$1" == "--extended" ]]; then
    EXTENDED_MODE=true
fi

echo "title: {{WIDGET_TITLE}}"
echo "refresh: {{REFRESH_INTERVAL}}"

# TODO: Replace with your actual metric collection
# Example: Get current value (0-100)
CURRENT_VALUE=$((RANDOM % 100))

# Update history
echo "$CURRENT_VALUE" >> "$CACHE_FILE"
tail -n "$MAX_SAMPLES" "$CACHE_FILE" > "$CACHE_FILE.tmp"
mv "$CACHE_FILE.tmp" "$CACHE_FILE"

# Read history
mapfile -t HISTORY < "$CACHE_FILE"

# Dashboard mode (compact)
if [[ "$EXTENDED_MODE" == "false" ]]; then
    # Show current status
    if (( CURRENT_VALUE > 80 )); then
        echo "row: [status:error] Current: ${CURRENT_VALUE}%"
    elif (( CURRENT_VALUE > 50 )); then
        echo "row: [status:warn] Current: ${CURRENT_VALUE}%"
    else
        echo "row: [status:ok] Current: ${CURRENT_VALUE}%"
    fi

    # Show sparkline (compact trend)
    echo "row: [sparkline:${HISTORY[*]}]"

    # Show average
    AVG=$(awk '{s+=$1} END {print int(s/NR)}' <<< "${HISTORY[*]}")
    echo "row: Average: ${AVG}%"
else
    # Extended mode (detailed view)
    echo "row: [bold]Current Status[/]"
    if (( CURRENT_VALUE > 80 )); then
        echo "row: [status:error] Value: ${CURRENT_VALUE}%"
    elif (( CURRENT_VALUE > 50 )); then
        echo "row: [status:warn] Value: ${CURRENT_VALUE}%"
    else
        echo "row: [status:ok] Value: ${CURRENT_VALUE}%"
    fi

    echo "row:"
    echo "row: [bold]History Graph[/]"
    echo "row: [graph:${HISTORY[*]}]"

    echo "row:"
    echo "row: [bold]Statistics[/]"
    echo "[table:Metric|Value]"
    AVG=$(awk '{s+=$1} END {print int(s/NR)}' <<< "${HISTORY[*]}")
    MIN=$(printf '%s\n' "${HISTORY[@]}" | sort -n | head -n1)
    MAX=$(printf '%s\n' "${HISTORY[@]}" | sort -n | tail -n1)
    echo "[tablerow:Average|${AVG}%]"
    echo "[tablerow:Minimum|${MIN}%]"
    echo "[tablerow:Maximum|${MAX}%]"
    echo "[tablerow:Samples|${#HISTORY[@]}]"
fi

# Actions
echo "action: Refresh:bash {{OUTPUT_FILE}}"
echo "action: Clear History:rm -f $CACHE_FILE && echo 'History cleared'"
