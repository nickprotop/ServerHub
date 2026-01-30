#!/bin/bash
# ServerHub Quick Install Script
# Downloads and installs the latest release from GitHub
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

set -e

INSTALL_DIR="$HOME/.local"
CONFIG_DIR="$HOME/.config/serverhub"
WIDGETS_DIR="$INSTALL_DIR/share/serverhub/widgets"
REPO="nickprotop/ServerHub"

echo "Installing ServerHub from latest release..."
echo ""

# 1. Detect architecture
ARCH=$(uname -m)
case "$ARCH" in
    x86_64)
        BINARY_NAME="serverhub-linux-x64"
        ;;
    aarch64|arm64)
        BINARY_NAME="serverhub-linux-arm64"
        ;;
    *)
        echo "Error: Unsupported architecture: $ARCH"
        echo "Supported: x86_64, aarch64/arm64"
        exit 1
        ;;
esac

echo "Detected architecture: $ARCH"
echo "Binary to download: $BINARY_NAME"
echo ""

# 2. Get latest release info
echo "Fetching latest release..."
RELEASE_JSON=$(curl -s "https://api.github.com/repos/$REPO/releases/latest")
RELEASE_TAG=$(echo "$RELEASE_JSON" | grep '"tag_name"' | sed -E 's/.*"([^"]+)".*/\1/')

if [ -z "$RELEASE_TAG" ]; then
    echo "Error: Could not fetch latest release information"
    echo "Please check your internet connection or install manually from:"
    echo "https://github.com/$REPO/releases"
    exit 1
fi

echo "Latest release: $RELEASE_TAG"
echo ""

# 3. Construct download URLs
BINARY_URL="https://github.com/$REPO/releases/download/$RELEASE_TAG/$BINARY_NAME"
WIDGETS_URL="https://github.com/$REPO/releases/download/$RELEASE_TAG/widgets.tar.gz"

# 4. Create directories
mkdir -p "$INSTALL_DIR/bin"
mkdir -p "$WIDGETS_DIR"
mkdir -p "$CONFIG_DIR/widgets"

# 5. Download binary
echo "Downloading ServerHub binary..."
if ! curl -L -f -o "/tmp/serverhub" "$BINARY_URL"; then
    echo "Error: Failed to download binary from $BINARY_URL"
    exit 1
fi

chmod +x "/tmp/serverhub"
mv "/tmp/serverhub" "$INSTALL_DIR/bin/serverhub"
echo "✓ Installed binary to $INSTALL_DIR/bin/serverhub"

# 6. Download and extract widgets
echo "Downloading bundled widgets..."
if ! curl -L -f -o "/tmp/widgets.tar.gz" "$WIDGETS_URL"; then
    echo "Error: Failed to download widgets from $WIDGETS_URL"
    exit 1
fi

tar -xzf "/tmp/widgets.tar.gz" -C "$WIDGETS_DIR/"
chmod +x "$WIDGETS_DIR/"*.sh 2>/dev/null || true
widget_count=$(ls -1 "$WIDGETS_DIR/"*.sh 2>/dev/null | wc -l)
echo "✓ Installed $widget_count bundled widgets to $WIDGETS_DIR"

# Clean up
rm -f "/tmp/widgets.tar.gz"

# 7. Config will be auto-generated on first run
if [ ! -f "$CONFIG_DIR/config.yaml" ]; then
    echo "✓ Config will be auto-generated on first run at $CONFIG_DIR/config.yaml"
else
    echo "✓ Config already exists at $CONFIG_DIR/config.yaml"
fi

# 8. Add to PATH if needed
PATH_ADDED=false
if ! echo "$PATH" | grep -q "$INSTALL_DIR/bin"; then
    echo ""
    if [ -f "$HOME/.bashrc" ]; then
        echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$HOME/.bashrc"
        echo "✓ Added ~/.local/bin to PATH in ~/.bashrc"
        SHELL_RC="$HOME/.bashrc"
        PATH_ADDED=true
    elif [ -f "$HOME/.zshrc" ]; then
        echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$HOME/.zshrc"
        echo "✓ Added ~/.local/bin to PATH in ~/.zshrc"
        SHELL_RC="$HOME/.zshrc"
        PATH_ADDED=true
    else
        echo "Warning: Could not detect shell config file (.bashrc or .zshrc)"
        echo "         Add manually: export PATH=\"\$HOME/.local/bin:\$PATH\""
    fi
else
    echo "✓ ~/.local/bin is already in PATH"
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✓ Installation complete!"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

if [ "$PATH_ADDED" = true ]; then
    echo "To get started, either reload your shell config:"
    echo ""
    echo "  source $SHELL_RC"
    echo ""
    echo "Or run directly with the full path:"
    echo ""
    echo "  ~/.local/bin/serverhub"
    echo ""
else
    echo "Run 'serverhub' to get started!"
    echo ""
fi

echo "Other commands:"
echo "  serverhub --discover   - Find and add custom widgets"
echo "  serverhub --help       - Show all options"
echo ""
echo "Documentation: https://github.com/$REPO"
