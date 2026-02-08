#!/bin/bash
# ServerHub Uninstallation Script
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

set -e

INSTALL_DIR="$HOME/.local"
CONFIG_DIR="$HOME/.config/serverhub"
WIDGETS_DIR="$INSTALL_DIR/share/serverhub"
BINARY_PATH="$INSTALL_DIR/bin/serverhub"
UNINSTALL_PATH="$INSTALL_DIR/bin/serverhub-uninstall"

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
[ -f "$UNINSTALL_PATH" ] && echo "  • Uninstaller: $UNINSTALL_PATH"
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

# Remove uninstaller
if [ -f "$UNINSTALL_PATH" ]; then
    rm -f "$UNINSTALL_PATH"
    echo "✓ Removed uninstaller: $UNINSTALL_PATH"
else
    echo "⊘ Uninstaller not found: $UNINSTALL_PATH"
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

# Ask about shell completion removal
COMPLETION_FOUND=false
if [ -f "$HOME/.bashrc" ] && grep -q "serverhub completion bash" "$HOME/.bashrc" 2>/dev/null; then
    COMPLETION_FOUND=true
    SHELL_RC="$HOME/.bashrc"
elif [ -f "$HOME/.zshrc" ] && grep -q "serverhub completion zsh" "$HOME/.zshrc" 2>/dev/null; then
    COMPLETION_FOUND=true
    SHELL_RC="$HOME/.zshrc"
fi

if [ "$COMPLETION_FOUND" = true ]; then
    echo ""
    echo "Shell completion configuration found in: $SHELL_RC"
    read -p "Remove completion configuration? [y/N] " -n 1 -r < /dev/tty
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        # Remove ServerHub completion lines from shell config
        sed -i '/# ServerHub completion/,+3d' "$SHELL_RC"
        echo "✓ Removed completion configuration from: $SHELL_RC"
        echo "  Note: Reload your shell or run: source $SHELL_RC"
    else
        echo "⊘ Kept completion configuration in: $SHELL_RC"
    fi
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✓ ServerHub uninstalled successfully"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

# Check if PATH or completion still exist
if [ -f "$HOME/.bashrc" ]; then
    SHELL_CONFIG="$HOME/.bashrc"
elif [ -f "$HOME/.zshrc" ]; then
    SHELL_CONFIG="$HOME/.zshrc"
fi

if [ -n "$SHELL_CONFIG" ]; then
    HAS_PATH=$(grep -c 'export PATH.*\.local/bin' "$SHELL_CONFIG" 2>/dev/null || true)
    HAS_COMPLETION=$(grep -c "serverhub completion" "$SHELL_CONFIG" 2>/dev/null || true)

    if [ "$HAS_PATH" -gt 0 ] || [ "$HAS_COMPLETION" -gt 0 ]; then
        echo "Note: Some configurations remain in $SHELL_CONFIG:"
        [ "$HAS_PATH" -gt 0 ] && echo "      • PATH modification: export PATH=\"\$HOME/.local/bin:\$PATH\""
        [ "$HAS_COMPLETION" -gt 0 ] && echo "      • Shell completion for serverhub"
        echo "      These are harmless but can be manually removed if desired."
        echo ""
    fi
fi
