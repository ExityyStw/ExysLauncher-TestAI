# ExysLauncher ⛏️

![Plataforma](https://img.shields.io/badge/plataforma-Windows%20%7C%20Linux-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Interface](https://img.shields.io/badge/interface-Avalonia%2011-purple)
![Testes](https://img.shields.io/badge/testes-36%20xUnit-green)

Launcher de Minecraft desktop, leve e seguro, focado em **modpacks modernos e pesados** — Vanilla, Fabric, Forge e NeoForge — com contas **offline e Microsoft**, instâncias isoladas e distribuição em exe único portátil.

## Funcionalidades

- **Contas offline e Microsoft** — login Microsoft via device code flow (OAuth2 → Xbox Live → XSTS → Minecraft Services); refresh tokens no cofre seguro do SO (DPAPI).
- **Instâncias isoladas** — criar, importar modpacks `.zip` (CurseForge/Modrinth, com `overrides`), selecionar, abrir pasta ou excluir.
- **Catálogo online** — versões Vanilla da Mojang + loaders NeoForge/Fabric/Forge compatíveis, com instalação automática.
- **Console do jogo embutido** — janela própria com buffer limitado, sem abrir console do Windows.
- **Presets de JVM** — G1GC ultra-otimizado, ZGC e Shenandoah; args customizados e caminho de Java manual.
- **Preferências persistentes** — tema, RAM, presets e flags salvos em `%APPDATA%\LauncherMine\ui-settings.json`.
- **Detecção de RAM** — leitura nativa do hardware, slider limitado ao máximo seguro.
- **Crash reporting** — exceções da UI salvas em `%APPDATA%\LauncherMine\logs\` antes de fechar.

## Segurança

| Vetor | Mitigação |
| :--- | :--- |
| Path Traversal / Zip Slip | `PathSecurity` com resolução canônica e boundary check |
| Roubo de credenciais | `ISecureVault`: DPAPI (Windows) e AES-256-GCM `0600` (Linux) |
| Mods adulterados em trânsito | HTTPS + SHA-256 em streaming; divergência destrói o `.tmp` |
| Injeção de comandos | `ProcessStartInfo.ArgumentList` — sem shell intermediário |
| Usernames maliciosos | Regex `^[a-zA-Z0-9_]{3,16}$` + UUID v3 compatível Mojang |

## Stack

- **C# / .NET 8** · **Avalonia 11** (MVVM com CommunityToolkit) · **CmlLib.Core** (launch/downloads)

## Estrutura

```text
├── src/
│   ├── LauncherMine.Core/    # Auth, Launch, Instâncias, Updater, Segurança, Modelos
│   └── LauncherMine.UI/      # Views .axaml + ViewModels (MVVM)
├── tests/LauncherMine.Tests/ # xUnit — PathSecurity, OfflineAuth, AccountManager
├── tools/exys_audit.py       # Auditoria estática UI↔VM↔XAML
├── Obsidian/                 # Vault de documentação viva (versões, tarefas, decisões)
├── dist/windows/             # Saída do publish (gitignored)
├── publish.bat / publish.sh  # Gera o exe único
├── run.bat / run.sh          # Executa com auto-instalação do .NET 8
└── LauncherMine.sln
```

## Uso

1. **Exe portátil:** `dist/windows/LauncherMine.UI.exe` — self-contained, não exige .NET instalado.
2. **`run.bat`** — detecta/instala o .NET 8 SDK e executa.
3. **Linux:** `chmod +x run.sh && ./run.sh`

## Desenvolvimento

```powershell
# Build (0 avisos / 0 erros)
dotnet build -c Release

# Testes (36 testes)
dotnet test tests/LauncherMine.Tests -c Release

# Auditoria estática de consistência (0 FAIL / 0 WARN)
py tools/exys_audit.py

# Publish — exe único em dist/windows/
dotnet publish src/LauncherMine.UI/LauncherMine.UI.csproj -c Release -r win-x64 `
  --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./dist/windows
```

> Se `dotnet` não estiver no PATH: `~/.dotnet/dotnet.exe`. Antes do publish, feche o exe em execução (`taskkill //F //IM LauncherMine.UI.exe`).

## Documentação viva

O vault [`Obsidian/`](Obsidian/Home.md) registra versões diárias por hora, tarefas, ideias, métodos e decisões arquiteturais (ADRs).

## Licença

Projeto em desenvolvimento ativo.
