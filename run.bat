@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

title Exys Launcher - TestAI

echo ==================================================
echo    ⛏️   Exys Launcher - TestAI                     
echo ==================================================

:: 1. Verifica no diretório local do usuário (.dotnet)
if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "DOTNET_CMD=%USERPROFILE%\.dotnet\dotnet.exe"
    goto :START
)

:: 2. Verifica se dotnet está no PATH
where dotnet >nul 2>&1
if %errorlevel% equ 0 (
    set "DOTNET_CMD=dotnet"
    goto :START
)

:: 3. Verifica em Arquivos de Programas
if exist "C:\Program Files\dotnet\dotnet.exe" (
    set "DOTNET_CMD=C:\Program Files\dotnet\dotnet.exe"
    goto :START
)

echo ⚠️  .NET 8 SDK não encontrado. Instalando automaticamente...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-dependencies.ps1"

if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "DOTNET_CMD=%USERPROFILE%\.dotnet\dotnet.exe"
    goto :START
) else (
    echo ❌ Falha na instalação automática. Baixe manualmente em https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

:START
echo ✅ [.NET 8 Ativo]: %DOTNET_CMD%
echo 🚀 Abrindo Exys Launcher - TestAI...

"%DOTNET_CMD%" run --project "%~dp0src\LauncherMine.UI\LauncherMine.UI.csproj"

if %errorlevel% neq 0 (
    echo.
    echo ⚠️ O launcher foi encerrado. Pressione qualquer tecla para sair.
    pause >nul
)
