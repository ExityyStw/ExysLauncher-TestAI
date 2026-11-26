# Exys Launcher - TestAI ⛏️

![Plataforma](https://img.shields.io/badge/plataforma-Windows%20%7C%20Linux-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Interface](https://img.shields.io/badge/interface-Avalonia-purple)

Um launcher de Minecraft moderno, ultra-leve, seguro e estritamente **multiplataforma (Windows e Linux)**, projetado especificamente para modpacks modernos e pesados (ex: **NeoForge 1.21.1 com 300+ mods**), além de instâncias **Fabric**, **Forge** e **Vanilla**.

Inspirado em:
- **Prism Launcher**: Isolamento estrito de instâncias e diretórios (cada instância possui sua pasta própria de `.minecraft`, `mods`, `config` e `saves`).
- **SKlauncher**: Interface escura minimalista com alternador rápido de contas Microsoft oficiais e offline.
- **Horizon Launcher (Rust)**: Eficiência de recursos e opção de encerramento automático pós-inicialização do Java (liberando 100% da memória RAM para o jogo).

## — Funcionalidades. 🖥️

1. **Catálogo Completo e Instalação Automática (Vanilla, NeoForge, Fabric & Forge):**
   - Busca dinâmica online de todas as versões Vanilla da Mojang e das versões de **NeoForge**, **Fabric** e **Forge** compatíveis com a versão escolhida.
   - Instalação automática do Mod Loader escolhido antes da inicialização do jogo.
2. **Gerenciador de Instâncias Isoladas (Prism-like):**
   - Crie, selecione, abra a pasta dedicada ou exclua instâncias diretamente na aba **Instâncias**.
3. **Detecção e Validação de Memória RAM:**
   - Leitura nativa da RAM física instalada (`GlobalMemoryStatusEx` no Windows / `/proc/meminfo` no Linux), indicando se o computador atende aos **16 GB recomendados** para modpacks pesados e limitando o slider ao máximo seguro para não travar o sistema.
4. **Importador de Modpacks (.ZIP):**
   - Importação direta de pacotes exportados do **CurseForge** e **Modrinth**.
   - Extração segura de pastas de `overrides` (`mods`, `config`, `scripts`, `kubejs`, `resourcepacks`) para instâncias isoladas.
5. **Perfis Profissionais de Garbage Collection (Java 21):**
   - **G1GC Ultra-Otimizado**: Configurado para eliminar micro-travamentos (*stuttering*) e picos de congelamento em pacotes com 300+ mods.
   - **Generational ZGC (Java 21+)** & **Shenandoah GC**: Opções de baixa latência para processadores modernos.

## — Pilares de Segurança da Informação. 🛡️

| Vetor de Ameaça | Mitigação Implementada |
| :--- | :--- |
| **Path Traversal (CWE-22) & Zip Slip (CWE-29)** | Resolução canônica de caminhos em `PathSecurity.cs` e validação estrita de fronteiras (*boundary check*), impedindo qualquer tentativa de fuga de diretório por caminhos maliciosos (`../`) em manifestos remotos ou arquivos `.zip`. |
| **Vazamento e Roubo de Credenciais** | Uso de criptografia nativa do sistema operacional através de `ISecureVault`: **DPAPI** no Windows (`ProtectedData.Protect` atrelado ao usuário atual) e **AES-256-GCM** com permissões estritas **POSIX 0600** (`chmod 600`) no Linux. |
| **Adulteração de Mods em Trânsito (MitM)** | Downloads obrigatoriamente via **HTTPS** e cálculo de hash **SHA-256 em tempo real via streaming** em arquivo temporário `.tmp`. Se o hash for divergente, o arquivo é destruído antes de chegar à pasta `mods/`. |
| **Injeção de Comandos (CWE-78)** | Execução do runtime Java e instaladores montada exclusivamente através de listas paramétricas nativas (`ProcessStartInfo.ArgumentList`), sem passar por shells (`cmd.exe` ou `/bin/sh`). |
| **Injeção em Nomes de Usuários Offline** | Validação estrita por Expressão Regular (`^[a-zA-Z0-9_]{3,16}$`) e geração de **UUID v3** compatível byte-a-byte com o padrão histórico da Mojang. |

O projeto não carrega configurações de arquivos `.env` e não precisa de segredos locais para compilar. O `ClientId` Microsoft usado no fluxo OAuth é público; tokens de conta são armazenados no cofre seguro do sistema operacional. Arquivos `.env` locais são ignorados pelo Git.

## — Tecnologias Aplicadas. 👾

- **C# e .NET 8:** linguagem e plataforma da aplicação.
- **Avalonia UI:** interface gráfica multiplataforma com área central rolável (`ScrollViewer`).
- **CommunityToolkit.Mvvm:** implementação da arquitetura MVVM.
- **CmlLib.Core:** integração com o lançamento e download de assets do Minecraft.

## — Estrutura de Diretórios. 📁

```text
ExysLauncher - TestAI/
├── ExysLauncher-TestAI.exe          # Executável portátil autônomo (Single-File)
├── run.bat                          # Inicializador com auto-instalação de dependências (.NET 8)
├── run.sh                           # Inicializador para Linux / WSL / Git Bash
├── install-dependencies.ps1         # Script de instalação oficial do .NET 8 SDK
├── sample-manifest.json             # Exemplo de manifesto para sincronização remota
└── src/
    ├── LauncherMine.Core/           # Motor desacoplado de regras e segurança
    │   ├── Auth/                    # Autenticação Microsoft OAuth2 e Offline segura
    │   ├── Launch/                  # Detecção de Java, isolamento e instalação de loaders
    │   ├── Models/                  # Modelos de dados, presets JVM e metadados
    │   ├── Security/                # DPAPI, AES-GCM Linux, Anti-Path Traversal
    │   ├── Services/                # Catálogo online Mojang, NeoForge, Fabric e Forge
    │   ├── Updater/                 # Custom Updater SHA-256 e Importador de ZIPs
    │   └── Utils/                   # Detecção nativa de RAM do sistema
    └── LauncherMine.UI/             # Interface gráfica em Avalonia UI
        ├── Views/                   # Telas XAML (MainWindow, CreateInstance, AddAccount, Console)
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
