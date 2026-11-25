# Exys Launcher - TestAI ⛏️

![Plataforma](https://img.shields.io/badge/plataforma-Windows%20%7C%20Linux-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Interface](https://img.shields.io/badge/interface-Avalonia-purple)

Um launcher de Minecraft moderno, ultra-leve, seguro e estritamente **multiplataforma (Windows e Linux)**, projetado especificamente para modpacks modernos e pesados (ex: **NeoForge 1.21.1 com 300+ mods**).

Inspirado em:
- **Prism Launcher**: Isolamento estrito de instâncias e diretórios (cada instância possui sua pasta própria de `.minecraft`, `mods`, `config` e `saves`).
- **SKlauncher**: Alternador rápido de contas com suporte tanto a contas Microsoft oficiais quanto a contas offline com validação estrita anti-injeção.
- **Horizon Launcher (Rust)**: Eficiência de recursos e opção de encerramento automático pós-inicialização do Java (liberando 100% da memória RAM para o jogo).

## — Funcionalidades. 🖥️

1. **Suporte Completo ao NeoForge 1.21.1 & Fabric:**
   - Seleção da versão do loader (`21.1.88`, `21.1.84`, etc.).
   - Resolução automática de assets oficiais e bibliotecas da Mojang na primeira inicialização.
2. **Importador de Modpacks (.ZIP):**
   - Importação direta de pacotes exportados do **CurseForge** e **Modrinth**.
   - Extração segura de pastas de `overrides` (`mods`, `config`, `scripts`, `kubejs`, `resourcepacks`) para instâncias isoladas.
3. **Perfis Profissionais de Garbage Collection (Java 21):**
   - **Generational ZGC (Java 21+)**: Pausas de garbage collection ultracurtas (< 1ms), ideais para processadores modernos.
   - **G1GC Ultra-Otimizado**: Configurado para eliminar micro-travamentos (*stuttering*) e picos de congelamento em pacotes com 300+ mods.
4. **Interface Estilo Papel & Tinta (Hand-Drawn / Papercraft):**
   - Design limpo e agradável, sem cara de IA genérica. Textura de papel marfim, tipografia nítida e alinhamento milimétrico.

## — Pilares de Segurança da Informação. 🛡️

| Vetor de Ameaça | Mitigação Implementada |
| :--- | :--- |
| **Path Traversal (CWE-22) & Zip Slip (CWE-29)** | Resolução canônica de caminhos em `PathSecurity.cs` e validação estrita de fronteiras (*boundary check*), impedindo qualquer tentativa de fuga de diretório por caminhos maliciosos (`../`) em manifestos remotos ou arquivos `.zip`. |
| **Vazamento e Roubo de Credenciais** | Uso de criptografia nativa do sistema operacional através de `ISecureVault`: **DPAPI** no Windows (`ProtectedData.Protect` atrelado ao usuário atual) e **AES-256-GCM** com permissões estritas **POSIX 0600** (`chmod 600`) no Linux. |
| **Adulteração de Mods em Trânsito (MitM)** | Downloads obrigatoriamente via **HTTPS** e cálculo de hash **SHA-256 em tempo real via streaming** em arquivo temporário `.tmp`. Se o hash for divergente, o arquivo é destruído antes de chegar à pasta `mods/`. |
| **Injeção de Comandos (CWE-78)** | Execução do runtime Java montada exclusivamente através de listas paramétricas nativas (`ProcessStartInfo.ArgumentList`), sem passar por shells (`cmd.exe` ou `/bin/sh`). |
| **Injeção em Nomes de Usuários Offline** | Validação estrita por Expressão Regular (`^[a-zA-Z0-9_]{3,16}$`) e geração de **UUID v3** compatível byte-a-byte com o padrão histórico da Mojang. |

O projeto não carrega configurações de arquivos `.env` e não precisa de segredos locais para compilar. O `ClientId` Microsoft usado no fluxo OAuth é público; tokens de conta são armazenados no cofre seguro do sistema operacional. Arquivos `.env` locais são ignorados pelo Git.

## — Tecnologias Aplicadas. 👾

- **C# e .NET 8:** linguagem e plataforma da aplicação.
- **Avalonia UI:** interface gráfica multiplataforma.
- **CommunityToolkit.Mvvm:** implementação da arquitetura MVVM.
- **CmlLib.Core:** integração com o lançamento do Minecraft.

## — Estrutura de Diretórios. 📁

```text
LauncherMine/ (ou ExysLauncher-TestAI/)
├── ExysLauncher-TestAI.exe          # Executável portátil autônomo (Single-File)
├── run.bat                          # Inicializador com auto-instalação de dependências (.NET 8)
├── run.sh                           # Inicializador para Linux / WSL / Git Bash
├── install-dependencies.ps1         # Script de instalação oficial do .NET 8 SDK
├── sample-manifest.json             # Exemplo de manifesto para sincronização remota
└── src/
    ├── LauncherMine.Core/           # Motor desacoplado de regras e segurança
    │   ├── Auth/                    # Autenticação Microsoft OAuth2 e Offline segura
    │   ├── Launch/                  # Detecção de Java, isolamento e montagem de processo
    │   ├── Models/                  # Modelos de dados, presets JVM e metadados
    │   ├── Security/                # DPAPI, AES-GCM Linux, Anti-Path Traversal
    │   └── Updater/                 # Custom Updater SHA-256 e Importador de ZIPs
    └── LauncherMine.UI/             # Interface gráfica em Avalonia UI
        ├── Views/                   # Telas XAML (MainWindow, AddAccount, Console)
        └── ViewModels/              # Lógica MVVM reativa com CommunityToolkit
```

## — Como Usar? 🌟

1. **Executável direto:** dê dois cliques em `ExysLauncher-TestAI.exe`. Ele é 100% independente e não exige que você instale nada no computador.
2. **Script com auto-instalação (`run.bat`):** dê dois cliques no arquivo. Caso o computador não tenha o .NET 8 instalado, o script baixa e configura as ferramentas oficiais da Microsoft em segundo plano e abre o launcher.
3. **Linux (`run.sh`):** execute os comandos:

   ```bash
   chmod +x run.sh
   ./run.sh
   ```

## — Como Compilar e Distribuir? 🔨

Para gerar um novo executável único (*Single-File*):

```powershell
# Publicação para Windows (gera o binário único em dist/windows/):
dotnet publish src/LauncherMine.UI/LauncherMine.UI.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./dist/windows

# Publicação para Linux (gera o binário nativo em dist/linux/):
dotnet publish src/LauncherMine.UI/LauncherMine.UI.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./dist/linux
```

---
