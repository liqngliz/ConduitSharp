#!/bin/bash
set -e

echo "🚀 Booting Conduit Live..."

# 1. Detect OS and Arch
OS="$(uname -s | tr '[:upper:]' '[:lower:]')"
ARCH="$(uname -m)"
if [ "$ARCH" = "aarch64" ] || [ "$ARCH" = "arm64" ]; then ARCH="arm64"; else ARCH="x64"; fi
if [ "$OS" = "darwin" ]; then OS="osx"; fi

# Determine binary suffix
SUFFIX="$OS-$ARCH"

echo "📦 Detected platform: $SUFFIX"

# 2. Setup directory
INSTALL_DIR="$HOME/.conduit-live"
mkdir -p "$INSTALL_DIR/Configuration"
mkdir -p "$INSTALL_DIR/logs"

# 3. Write default transparent OpenAI proxy route if it doesn't exist
ROUTE_FILE="$INSTALL_DIR/Configuration/routes.json"
if [ ! -f "$ROUTE_FILE" ]; then
  echo "📥 Fetching default routes.json from repo..."
  curl -sSL "https://raw.githubusercontent.com/liqngliz/ConduitSharp/main/examples/ConduitSharp.Spend/Configuration/routes.json" -o "$ROUTE_FILE"
fi

# 4. Fetch the latest tokenflow release tag
# For production, we query the GitHub API and filter for tags starting with "tokenflow-v"
LATEST_TAG=$(curl -s https://api.github.com/repos/liqngliz/ConduitSharp/releases | grep '"tag_name": "tokenflow-v' | head -n 1 | cut -d '"' -f 4)
if [ -z "$LATEST_TAG" ]; then
  # Fallback if API rate limited
  LATEST_TAG="tokenflow-v1.0.0" 
fi

# Asset names carry the tag: tokenflow.yml packages conduitsharp-spend-<tag>-<rid>.tar.gz
DOWNLOAD_URL="https://github.com/liqngliz/ConduitSharp/releases/download/$LATEST_TAG/conduitsharp-spend-$LATEST_TAG-$SUFFIX.tar.gz"

# Only download if we don't have the binary, or if we want to force update
if [ ! -f "$INSTALL_DIR/ConduitSharp.Spend" ]; then
  echo "⬇️  Downloading ConduitSharp Spend ($LATEST_TAG)..."
  curl -sSL "$DOWNLOAD_URL" -o "/tmp/conduit.tar.gz"
  tar -xzf "/tmp/conduit.tar.gz" -C "$INSTALL_DIR"
  rm "/tmp/conduit.tar.gz"
  chmod +x "$INSTALL_DIR/ConduitSharp.Spend"
fi

echo "✅ Ready! Starting the dashboard..."
echo "📊 Dashboard: http://localhost:5050"
echo "🔌 Point your AI SDKs to http://localhost:5050 (e.g., http://localhost:5050/llm/claude)"
echo ""

# 5. Run it
cd "$INSTALL_DIR"
export ASPNETCORE_URLS="http://+:5050"
export Gateway__RoutesPath="Configuration/routes.json"
export CONDUIT_SPEND_DATA="./logs"

./ConduitSharp.Spend
