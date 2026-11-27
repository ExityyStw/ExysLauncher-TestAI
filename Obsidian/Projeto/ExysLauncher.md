---
tipo: contexto
---

# ExysLauncher

Launcher de Minecraft desktop: **C# / .NET 8 + Avalonia 11**, distribuído como exe self-contained win-x64.

## Estrutura

| Camada | Caminho | Papel |
|---|---|---|
| UI | `src/LauncherMine.UI` | Views `.axaml` + `MainWindowViewModel` (MVVM, CommunityToolkit) |
| Core | `src/LauncherMine.Core` | Auth, Launch (CmlLib), Instâncias, Updater, Segurança, Modelos |
| Testes | `tests/LauncherMine.Tests` | xUnit — PathSecurity, OfflineAuth, AccountManager |
| Tools | `tools/exys_audit.py` | Auditoria estática de consistência UI↔VM↔XAML |
| Build | `publish.bat` / `dotnet publish` | saída em `dist/windows/` |

## Repositório

`https://github.com/ExityyStw/ExysLauncher-TestAI`

## Estado em uma linha

Funcional e auditado (0 FAIL/0 WARN); validação end-to-end do login Microsoft é o item aberto.

**Links:** [[Sínteses/Estado Atual]] · [[Planos/Tarefas]] · [[Planos/Métodos]]
