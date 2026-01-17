#!/bin/bash
# Generates BundledWidgets.g.cs from widgets/*.sh before build
# This file contains hardcoded SHA256 checksums for bundled widgets

set -e

cd "$(dirname "$0")"

WIDGETS_DIR="widgets"
OUTPUT_FILE="src/Config/BundledWidgets.g.cs"

echo "Generating BundledWidgets.g.cs..."

# Create output directory if needed
mkdir -p "$(dirname "$OUTPUT_FILE")"

# Start generating the file
cat > "$OUTPUT_FILE" << 'HEADER'
// Auto-generated file - do not edit manually
// Generated from widgets/ directory during build
// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace ServerHub.Config;

/// <summary>
/// Bundled widgets with hardcoded checksums for tamper detection
/// Auto-generated during build from widgets/ directory
/// </summary>
public static class BundledWidgets
{
    /// <summary>
    /// SHA256 checksums for bundled widgets
    /// Key: widget filename (e.g., "cpu.sh")
    /// Value: SHA256 checksum (lowercase hex)
    /// </summary>
    public static readonly Dictionary<string, string> Checksums = new()
    {
HEADER

# Generate checksum entries for each widget
WIDGET_COUNT=0
for file in "$WIDGETS_DIR"/*.sh; do
    if [ -f "$file" ]; then
        filename=$(basename "$file")

        # Compute SHA256 checksum
        if command -v sha256sum >/dev/null 2>&1; then
            checksum=$(sha256sum "$file" | awk '{print $1}')
        elif command -v shasum >/dev/null 2>&1; then
            checksum=$(shasum -a 256 "$file" | awk '{print $1}')
        else
            echo "Error: Neither sha256sum nor shasum found"
            exit 1
        fi

        echo "        [\"$filename\"] = \"$checksum\"," >> "$OUTPUT_FILE"
        WIDGET_COUNT=$((WIDGET_COUNT + 1))
    fi
done

# Close the dictionary and class
cat >> "$OUTPUT_FILE" << 'FOOTER'
    };
}
FOOTER

echo " Generated checksums for $WIDGET_COUNT bundled widgets"
echo "  Output: $OUTPUT_FILE"
