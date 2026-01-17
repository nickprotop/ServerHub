#!/bin/bash
# Generates DefaultConfig.g.cs from config.production.yaml before build
# This embeds the default configuration as C# code

set -e

cd "$(dirname "$0")"

SOURCE_CONFIG="config.production.yaml"
OUTPUT_FILE="src/Config/DefaultConfig.g.cs"

echo "Generating DefaultConfig.g.cs..."

# Check if source config exists
if [ ! -f "$SOURCE_CONFIG" ]; then
    echo "Warning: $SOURCE_CONFIG not found, using config.example.yaml"
    SOURCE_CONFIG="config.example.yaml"
fi

# Create output directory if needed
mkdir -p "$(dirname "$OUTPUT_FILE")"

# Read and escape the YAML content for C# string literal
# We'll use a raw string literal (C# 11+)
YAML_CONTENT=$(cat "$SOURCE_CONFIG")

# Start generating the file
cat > "$OUTPUT_FILE" << 'HEADER'
// Auto-generated file - do not edit manually
// Generated from config.production.yaml during build
// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace ServerHub.Config;

/// <summary>
/// Default configuration embedded at build time
/// Auto-generated from config.production.yaml
/// </summary>
public static class DefaultConfig
{
    /// <summary>
    /// Default configuration as YAML string
    /// </summary>
    public static readonly string YamlContent = """
HEADER

# Append the YAML content (C# 11 raw string literal)
echo "$YAML_CONTENT" >> "$OUTPUT_FILE"

# Close the raw string literal and class
cat >> "$OUTPUT_FILE" << 'FOOTER'
""";
}
FOOTER

echo " Generated default config from $SOURCE_CONFIG"
echo "  Output: $OUTPUT_FILE"
