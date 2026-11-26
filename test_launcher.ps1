# Script de teste para verificar o Exys Launcher
# Verifica elementos basicos e integridade do executavel

Write-Host "=" * 50
Write-Host "TESTE DO EXYS LAUNCHER"
Write-Host "=" * 50

# Teste 1: Executavel existe
$exePath = "dist\windows\LauncherMine.UI.exe"
if (Test-Path $exePath) {
    $sizeMB = (Get-Item $exePath).Length / 1MB
    Write-Host "✅ Executavel encontrado: $exePath"
    Write-Host "   Tamanho: $([math]::Round($sizeMB, 1)) MB"
} else {
    Write-Host "❌ Executavel nao encontrado: $exePath"
}

# Teste 2: Artefatos de build
Write-Host "`n📦 Verificando artefatos de build..."
$artifacts = @(
    "dist\windows\LauncherMine.UI.exe",
    "src\LauncherMine.Core\bin\Release\net8.0\LauncherMine.Core.dll",
    "src\LauncherMine.UI\bin\Release\net8.0\LauncherMine.UI.dll"
)

foreach ($artifact in $artifacts) {
    if (Test-Path $artifact) {
        Write-Host "✅ $artifact"
    } else {
        Write-Host "❌ $artifact"
    }
}

Write-Host "`n" + "=" * 50
Write-Host "Teste concluido. Verifique manualmente a interface para sobreposicoes."
