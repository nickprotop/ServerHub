#!/bin/bash
# ServerHub Uninstallation Script
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

set -e

INSTALL_DIR="$HOME/.local"
CONFIG_DIR="$HOME/.config/serverhub"
WIDGETS_DIR="$INSTALL_DIR/share/serverhub"
BINARY_PATH="$INSTALL_DIR/bin/serverhub"

echo "ServerHub Uninstaller"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

# Check if ServerHub is installed
if [ ! -f "$BINARY_PATH" ] && [ ! -d "$WIDGETS_DIR" ]; then
    echo "ServerHub is not installed (binary and widgets not found)"
    exit 0
fi

echo "This will remove:"
echo "  • Binary: $BINARY_PATH"
[ -d "$WIDGETS_DIR" ] && echo "  • Widgets: $WIDGETS_DIR"
[ -d "$CONFIG_DIR" ] && echo "  • Configuration: $CONFIG_DIR (optional - will ask)"
echo ""

read -p "Continue with uninstallation? [y/N] " -n 1 -r < /dev/tty
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "Uninstallation cancelled."
    exit 0
fi

echo ""

# Remove binary
if [ -f "$BINARY_PATH" ]; then
    rm -f "$BINARY_PATH"
    echo "✓ Removed binary: $BINARY_PATH"
else
    echo "⊘ Binary not found: $BINARY_PATH"
fi

# Remove widgets directory
if [ -d "$WIDGETS_DIR" ]; then
    rm -rf "$WIDGETS_DIR"
    echo "✓ Removed widgets: $WIDGETS_DIR"
else
    echo "⊘ Widgets directory not found: $WIDGETS_DIR"
fi

# Ask about config
if [ -d "$CONFIG_DIR" ]; then
    echo ""
    echo "Configuration directory found: $CONFIG_DIR"
    echo "This contains your config.yaml and any custom widgets."
    read -p "Remove configuration directory? [y/N] " -n 1 -r < /dev/tty
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        rm -rf "$CONFIG_DIR"
        echo "✓ Removed configuration: $CONFIG_DIR"
    else
        echo "⊘ Kept configuration: $CONFIG_DIR"
    fi
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✓ ServerHub uninstalled successfully"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "Note: PATH modifications in ~/.bashrc or ~/.zshrc were not removed."
echo "      You may want to manually remove the line:"
echo "      export PATH=\"\$HOME/.local/bin:\$PATH\""
echo ""
