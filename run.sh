#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=================================================="
echo "    ⛏️   Exys Launcher - TestAI (Linux)           "
echo "=================================================="

DOTNET_BIN=""
if [ -f "$HOME/.dotnet/dotnet" ]; then
    DOTNET_BIN="$HOME/.dotnet/dotnet"
elif command -v dotnet >/dev/null 2>&1; then
    DOTNET_BIN="dotnet"
elif [ -f "/usr/share/dotnet/dotnet" ]; then
    DOTNET_BIN="/usr/share/dotnet/dotnet"
elif [ -f "/c/Program Files/dotnet/dotnet.exe" ]; then
    DOTNET_BIN="/c/Program Files/dotnet/dotnet.exe"
fi

if [ -z "$DOTNET_BIN" ]; then
    echo "⚠️  .NET 8 SDK não detectado. Instalando automaticamente..."
    bash "$SCRIPT_DIR/install-dependencies.sh"
    DOTNET_BIN="$HOME/.dotnet/dotnet"
fi

echo "✅ [.NET SDK Ativo]: $($DOTNET_BIN --version)"
echo "🚀 Iniciando Exys Launcher - TestAI..."

"$DOTNET_BIN" run --project "$SCRIPT_DIR/src/LauncherMine.UI/LauncherMine.UI.csproj"
