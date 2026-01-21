#!/bin/bash
# ServerHub Installation Script (User-Local)
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

set -e

INSTALL_DIR="$HOME/.local"
CONFIG_DIR="$HOME/.config/serverhub"
WIDGETS_DIR="$INSTALL_DIR/share/serverhub/widgets"

echo "Installing ServerHub (user-local)..."

# 1. Create directories
mkdir -p "$INSTALL_DIR/bin"
mkdir -p "$WIDGETS_DIR"
mkdir -p "$CONFIG_DIR/widgets"

# 2. Publish the project as single-file self-contained
PUBLISH_DIR="bin/Release/net9.0/linux-x64/publish"
if [ ! -f "$PUBLISH_DIR/ServerHub" ]; then
    echo "Publishing ServerHub (single-file self-contained)..."
    dotnet publish -c Release -r linux-x64
fi

# Find the single-file executable
BINARY_PATH="$PUBLISH_DIR/ServerHub"
if [ ! -f "$BINARY_PATH" ]; then
    echo "Error: ServerHub binary not found at $BINARY_PATH"
    echo "Publish may have failed."
    exit 1
fi

# 3. Copy the single binary
echo "Installing ServerHub to $INSTALL_DIR/bin/..."
cp "$BINARY_PATH" "$INSTALL_DIR/bin/serverhub"
chmod +x "$INSTALL_DIR/bin/serverhub"
echo " Installed single binary to $INSTALL_DIR/bin/serverhub"

# 4. Copy bundled widgets
if [ -d "widgets" ] && [ -n "$(ls -A widgets/*.sh 2>/dev/null)" ]; then
    cp widgets/*.sh "$WIDGETS_DIR/" 2>/dev/null || true
    chmod +x "$WIDGETS_DIR/"*.sh 2>/dev/null || true
    widget_count=$(ls -1 "$WIDGETS_DIR/"*.sh 2>/dev/null | wc -l)
    echo " Installed $widget_count bundled widget(s) to $WIDGETS_DIR"
else
    echo "Warning: No widgets found in ./widgets/ directory"
fi

# 5. Config will be auto-generated on first run
if [ ! -f "$CONFIG_DIR/config.yaml" ]; then
    echo "Info: Config will be auto-generated on first run at $CONFIG_DIR/config.yaml"
else
    echo "Info: Config already exists at $CONFIG_DIR/config.yaml"
fi

# 6. Add to PATH if needed
PATH_ADDED=false
if ! echo "$PATH" | grep -q "$INSTALL_DIR/bin"; then
    echo ""
    if [ -f "$HOME/.bashrc" ]; then
        echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$HOME/.bashrc"
        echo " Added ~/.local/bin to PATH in ~/.bashrc"
        SHELL_RC="$HOME/.bashrc"
        PATH_ADDED=true
    elif [ -f "$HOME/.zshrc" ]; then
        echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$HOME/.zshrc"
        echo " Added ~/.local/bin to PATH in ~/.zshrc"
        SHELL_RC="$HOME/.zshrc"
        PATH_ADDED=true
    else
        echo "Warning: Could not detect shell config file (.bashrc or .zshrc)"
        echo "         Add manually: export PATH=\"\$HOME/.local/bin:\$PATH\""
    fi
else
    echo " ~/.local/bin is already in PATH"
fi

echo ""
echo " Installation complete!"
echo ""

if [ "$PATH_ADDED" = true ]; then
    echo "To reload your PATH (choose one):"
    echo "  source $SHELL_RC       - Reload config in current terminal"
    echo "  exec \$SHELL              - Restart your shell"
    echo "  Or open a new terminal"
    echo ""
fi

echo "Quick start:"
echo "  serverhub              - Run with default config"
echo "  serverhub --discover   - Find and add custom widgets"
echo "  serverhub --help       - Show all options"
