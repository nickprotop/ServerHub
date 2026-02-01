#!/usr/bin/env python3
"""
{{WIDGET_TITLE}}
{{DESCRIPTION}}
Author: {{AUTHOR}}
"""

import sys
import os
from pathlib import Path

# Extended mode detection
EXTENDED_MODE = "--extended" in sys.argv

def main():
    try:
        print(f"title: {{WIDGET_TITLE}}")
        print(f"refresh: {{REFRESH_INTERVAL}}")

        # TODO: Replace with your actual data collection
        # Example: Collect metrics
        current_value = 42
        history = [30, 35, 40, 42, 45, 50, 48, 42]

        if not EXTENDED_MODE:
            # Dashboard mode (compact)
            status = "ok" if current_value < 80 else "error"
            print(f"row: [status:{status}] Current: {current_value}")
            print(f"row: [sparkline:{','.join(map(str, history))}]")
            print(f"row: Average: {sum(history) // len(history)}")
        else:
            # Extended mode (detailed)
            print("row: [bold]Current Status[/]")
            status = "ok" if current_value < 80 else "error"
            print(f"row: [status:{status}] Value: {current_value}")

            print("row:")
            print("row: [bold]History Graph[/]")
            print(f"row: [graph:{','.join(map(str, history))}]")

            print("row:")
            print("row: [bold]Statistics[/]")
            print("[table:Metric|Value]")
            print(f"[tablerow:Average|{sum(history) // len(history)}]")
            print(f"[tablerow:Minimum|{min(history)}]")
            print(f"[tablerow:Maximum|{max(history)}]")
            print(f"[tablerow:Samples|{len(history)}]")

        # Actions
        print(f"action: Refresh:python3 {{OUTPUT_FILE}}")

    except Exception as e:
        print(f"row: [status:error] Error: {str(e)}")

if __name__ == "__main__":
    main()
