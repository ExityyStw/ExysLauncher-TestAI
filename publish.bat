@echo off
chcp 65001 >nul
setlocal

title LauncherMine - Compilador Portátil (.exe)

echo ==================================================
echo    🔨  LauncherMine - Gerador de Executável (.exe)
echo ==================================================

set "OUTPUT_DIR=dist\windows"
if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

echo [1/2] Compilando binário único autônomo (Self-Contained)...
dotnet publish src\LauncherMine.UI\LauncherMine.UI.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUTPUT_DIR%"

if %errorlevel% equ 0 (
    echo ==================================================
    echo ✅ Executável gerado com sucesso!
    echo 👉 %OUTPUT_DIR%\LauncherMine.UI.exe
    echo.
    echo Esse .exe é 100%% portátil: você pode enviá-lo para qualquer
    echo pessoa e ela NÃO precisará instalar o .NET para jogar!
    echo ==================================================
) else (
    echo ❌ Falha ao compilar. Verifique se o .NET 8 SDK está instalado.
)

pause
