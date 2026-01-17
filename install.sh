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

# 2. Build the project
if [ ! -d "bin/Debug/net9.0" ] && [ ! -d "bin/Release/net9.0" ]; then
    echo "Building ServerHub..."
    dotnet build -c Release
fi

# Find the binary (prefer Release, fallback to Debug)
BINARY_PATH=""
if [ -f "bin/Release/net9.0/ServerHub" ]; then
    BINARY_PATH="bin/Release/net9.0/ServerHub"
elif [ -f "bin/Release/net9.0/serverhub" ]; then
    BINARY_PATH="bin/Release/net9.0/serverhub"
elif [ -f "bin/Debug/net9.0/ServerHub" ]; then
    BINARY_PATH="bin/Debug/net9.0/ServerHub"
elif [ -f "bin/Debug/net9.0/serverhub" ]; then
    BINARY_PATH="bin/Debug/net9.0/serverhub"
fi

if [ -z "$BINARY_PATH" ]; then
    echo "Error: ServerHub binary not found. Build may have failed."
    exit 1
fi

# 3. Copy binary
cp "$BINARY_PATH" "$INSTALL_DIR/bin/serverhub"
chmod +x "$INSTALL_DIR/bin/serverhub"
echo "Installed binary to $INSTALL_DIR/bin/serverhub"

# 4. Copy bundled widgets
if [ -d "widgets" ] && [ -n "$(ls -A widgets/*.sh 2>/dev/null)" ]; then
    cp widgets/*.sh "$WIDGETS_DIR/" 2>/dev/null || true
    chmod +x "$WIDGETS_DIR/"*.sh 2>/dev/null || true
    widget_count=$(ls -1 "$WIDGETS_DIR/"*.sh 2>/dev/null | wc -l)
    echo " Installed $widget_count bundled widget(s) to $WIDGETS_DIR"
else
    echo "Warning: No widgets found in ./widgets/ directory"
fi

# 5. Generate example config if not exists
if [ ! -f "$CONFIG_DIR/config.yaml" ]; then
    cat > "$CONFIG_DIR/config.yaml" << 'EOF'
# ServerHub Configuration
# Bundled widgets are automatically validated - no checksum needed!

default_refresh: 5

widgets:
  cpu:
    path: cpu.sh
    refresh: 2
    priority: 1

  memory:
    path: memory.sh
    refresh: 2
    priority: 1

  disk:
    path: disk.sh
    refresh: 10
    priority: 1

  sysinfo:
    path: sysinfo.sh
    refresh: 30
    priority: 2

  network:
    path: network.sh
    refresh: 2
    priority: 2

  processes:
    path: processes.sh
    refresh: 3
    priority: 2

  services:
    path: services.sh
    refresh: 5
    priority: 2

  docker:
    path: docker.sh
    refresh: 5
    priority: 2
    column_span: 2

layout:
  order:
    - cpu
    - memory
    - disk
    - sysinfo
    - network
    - processes
    - services
    - docker

breakpoints:
  single: 0
  double: 100
  triple: 160
  quad: 220
EOF
    echo " Created example config at $CONFIG_DIR/config.yaml"
else
    echo "Info: Config already exists at $CONFIG_DIR/config.yaml"
fi

# 6. Check PATH
if ! echo "$PATH" | grep -q "$INSTALL_DIR/bin"; then
    echo ""
    echo "Warning:  Add ~/.local/bin to your PATH:"
    echo ""
    if [ -f "$HOME/.bashrc" ]; then
        echo "  echo 'export PATH=\"\$HOME/.local/bin:\$PATH\"' >> ~/.bashrc"
        echo "  source ~/.bashrc"
    elif [ -f "$HOME/.zshrc" ]; then
        echo "  echo 'export PATH=\"\$HOME/.local/bin:\$PATH\"' >> ~/.zshrc"
        echo "  source ~/.zshrc"
    else
        echo "  export PATH=\"\$HOME/.local/bin:\$PATH\""
    fi
else
    echo " ~/.local/bin is already in PATH"
fi

echo ""
echo " Installation complete!"
echo ""
echo "Quick start:"
echo "  serverhub              - Run with default config"
echo "  serverhub --discover   - Find and add custom widgets"
echo "  serverhub --help       - Show all options"
