#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=================================================="
echo "    🔨  LauncherMine - Gerador de Binário Portátil"
echo "=================================================="

OUTPUT_DIR="./dist/linux"
mkdir -p "$OUTPUT_DIR"

echo "[1/2] Compilando e gerando binário único (Self-Contained)..."
dotnet publish src/LauncherMine.UI/LauncherMine.UI.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$OUTPUT_DIR"

chmod +x "$OUTPUT_DIR/LauncherMine.UI"

echo "=================================================="
echo "✅ Pronto! Executável gerado com sucesso:"
echo "👉 $OUTPUT_DIR/LauncherMine.UI"
echo "Esse arquivo pode ser executado diretamente em qualquer distribuição Linux!"
echo "=================================================="
