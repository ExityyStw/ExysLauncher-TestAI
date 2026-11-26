#!/usr/bin/env python3
"""
Script de teste para verificar o Exys Launcher.
Verifica elementos básicos e integridade do executável.
"""

import os
import subprocess
import sys
from pathlib import Path

def test_executable_exists():
    """Verifica se o executável existe."""
    exe_path = Path("dist/windows/LauncherMine.UI.exe")
    if exe_path.exists():
        size_mb = exe_path.stat().st_size / (1024 * 1024)
        print(f"✅ Executável encontrado: {exe_path}")
        print(f"   Tamanho: {size_mb:.1f} MB")
        return True
    else:
        print(f"❌ Executável não encontrado: {exe_path}")
        return False

def test_executable_launch():
    """Testa se o executável inicia sem erros."""
    exe_path = Path("dist/windows/LauncherMine.UI.exe")
    if not exe_path.exists():
        return False
    
    try:
        print("\n🚀 Iniciando launcher...")
        process = subprocess.Popen(
            [str(exe_path)],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if sys.platform == "win32" else 0
        )
        
        # Aguarda 3 segundos para ver se inicia sem crash
        import time
        time.sleep(3)
        
        # Verifica se ainda está rodando
        poll = process.poll()
        if poll is None:
            print("✅ Launcher iniciou com sucesso (rodando por 3 segundos)")
            process.terminate()
            return True
        else:
            print(f"❌ Launcher encerrou com código: {poll}")
            return False
    except Exception as e:
        print(f"❌ Erro ao iniciar launcher: {e}")
        return False

def test_build_artifacts():
    """Verifica se os artefatos de build foram gerados."""
    print("\n📦 Verificando artefatos de build...")
    
    artifacts = [
        "dist/windows/LauncherMine.UI.exe",
        "src/Launcher.Core/bin/Release/net8.0/LauncherMine.Core.dll",
        "src/Launcher.UI/bin/Release/net8.0/LauncherMine.UI.dll",
    ]
    
    all_ok = True
    for artifact in artifacts:
        if Path(artifact).exists():
            print(f"✅ {artifact}")
        else:
            print(f"❌ {artifact}")
            all_ok = False
    
    return all_ok

def main():
    print("=" * 50)
    print("TESTE DO EXYS LAUNCHER")
    print("=" * 50)
    
    results = []
    
    # Teste 1: Executável existe
    results.append(test_executable_exists())
    
    # Teste 2: Artefatos de build
    results.append(test_build_artifacts())
    
    # Teste 3: Inicialização (opcional)
    print("\n⚠️  Deseja testar a inicialização do launcher? (y/n)")
    choice = input().strip().lower()
    if choice == 'y':
        results.append(test_executable_launch())
    
    print("\n" + "=" * 50)
    if all(results):
        print("✅ TODOS OS TESTES PASSARAM")
        return 0
    else:
        print("❌ ALGUNS TESTES FALHARAM")
        return 1

if __name__ == "__main__":
    sys.exit(main())
