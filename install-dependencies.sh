#!/usr/bin/env bash
set -euo pipefail

echo "=================================================="
echo "  ⛏️  LauncherMine - Instalador de Dependências    "
echo "=================================================="

TARGET_DIR="$HOME/.dotnet"
mkdir -p "$TARGET_DIR"

echo "[1/2] Baixando e instalando .NET 8.0 SDK oficial..."
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0 --install-dir "$TARGET_DIR"

echo "[2/2] Configurando PATH..."
export PATH="$TARGET_DIR:$PATH"
if [ -f "$HOME/.bashrc" ] && ! grep -q "$TARGET_DIR" "$HOME/.bashrc"; then
    echo "export PATH=\"$TARGET_DIR:\$PATH\"" >> "$HOME/.bashrc"
fi

echo "=================================================="
echo "✅ .NET 8.0 SDK instalado com sucesso em $TARGET_DIR!"
echo "=================================================="
