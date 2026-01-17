#!/bin/bash
# ServerHub Uninstallation Script
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

set -e

echo " Uninstalling ServerHub..."

# Remove binary
if [ -f "$HOME/.local/bin/serverhub" ]; then
    rm -f "$HOME/.local/bin/serverhub"
    echo " Removed binary: ~/.local/bin/serverhub"
else
    echo "Info: Binary not found: ~/.local/bin/serverhub"
fi

# Remove bundled widgets
if [ -d "$HOME/.local/share/serverhub" ]; then
    rm -rf "$HOME/.local/share/serverhub"
    echo " Removed bundled widgets: ~/.local/share/serverhub"
else
    echo "Info: Bundled widgets directory not found"
fi

echo ""
echo " ServerHub uninstalled"
echo ""
echo "Info:  Configuration and custom widgets preserved at:"
echo "   ~/.config/serverhub/"
echo ""
echo "   To completely remove (including your config and custom widgets):"
echo "   rm -rf ~/.config/serverhub/"
