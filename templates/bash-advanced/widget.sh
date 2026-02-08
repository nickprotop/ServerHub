#!/bin/bash
# {{WIDGET_TITLE}}
# {{DESCRIPTION}}
# Author: {{AUTHOR}}

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

# Store metric in database for historical tracking
echo "datastore: {{WIDGET_NAME}}_metric value=$CURRENT_VALUE"

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

    # Show sparkline with last 10 samples from storage
    echo "row: [history_sparkline:{{WIDGET_NAME}}_metric.value:last_10:spectrum:15]"

    # Show aggregated average from last 30 samples
    echo "row: [datafetch:{{WIDGET_NAME}}_metric.value:last_30:avg] [grey70]Avg (30 samples)[/]"
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
    echo "row: [bold]History Graph (last 30 samples)[/]"
    # Show last 30 samples as vertical bar graph
    echo "row: [history_graph:{{WIDGET_NAME}}_metric.value:last_30:spectrum:Value %:0-100:40]"

    echo "row:"
    echo "row: [bold]History Graph (last hour)[/]"
    # Show 1 hour of data as smooth line graph
    echo "row: [history_line:{{WIDGET_NAME}}_metric.value:1h:warm:Value (1h):0-100:60:8:braille]"

    echo "row:"
    echo "row: [bold]Statistics (last 30 samples)[/]"
    echo "[table:Metric|Value]"
    # Use storage aggregations instead of manual calculations
    echo "[tablerow:Average|[datafetch:{{WIDGET_NAME}}_metric.value:last_30:avg]%]"
    echo "[tablerow:Minimum|[datafetch:{{WIDGET_NAME}}_metric.value:last_30:min]%]"
    echo "[tablerow:Maximum|[datafetch:{{WIDGET_NAME}}_metric.value:last_30:max]%]"
    echo "[tablerow:Count|[datafetch:{{WIDGET_NAME}}_metric.value:last_30:count]]"
fi

# Actions
echo "action: Refresh:bash {{OUTPUT_FILE}}"
