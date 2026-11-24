# Script de instalação automática do .NET 8 SDK
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$targetDir = "$env:USERPROFILE\.dotnet"
$scriptUrl = "https://dot.net/v1/dotnet-install.ps1"
$tempScript = "$env:TEMP\dotnet-install.ps1"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  ⛏️  LauncherMine - Instalador de Dependências    " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

Write-Host "[1/3] Baixando instalador oficial da Microsoft..." -ForegroundColor Yellow
Invoke-WebRequest -Uri $scriptUrl -OutFile $tempScript -UseBasicParsing

Write-Host "[2/3] Instalando .NET 8.0 SDK em $targetDir..." -ForegroundColor Yellow
& $tempScript -Channel 8.0 -InstallDir $targetDir

Write-Host "[3/3] Registrando no PATH do usuário..." -ForegroundColor Yellow
$currentPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($null -eq $currentPath) { $currentPath = "" }
if ($currentPath -notlike "*$targetDir*") {
    [Environment]::SetEnvironmentVariable("Path", "$targetDir;$currentPath", "User")
}
$env:PATH = "$targetDir;$env:PATH"

Write-Host "==================================================" -ForegroundColor Green
Write-Host "✅ .NET 8.0 SDK instalado com sucesso!" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Green
