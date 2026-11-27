---
tipo: metodos
atualizado: 2026-11-27
---

# Métodos

Playbooks reutilizáveis do projeto. Cada um resolve uma classe recorrente de problema.

## Build & Publish

**Quando usar:** após qualquer mudança em `src/`.

1. `taskkill //F //IM LauncherMine.UI.exe` (o exe trava o publish — MSB4018)
2. `dotnet build -c Release` — exige **0 avisos, 0 erros**
3. `dotnet publish src/LauncherMine.UI -c Release -r win-x64 --self-contained true -o dist/windows`
4. Se MSB4018 persistir: aguardar alguns segundos (handle residual) e repetir.

> SDK: usar `~/.dotnet/dotnet.exe` se `dotnet` não estiver no PATH.

## Auditoria estática

**Quando usar:** após mexer em XAML, ViewModel ou handlers — antes de publicar.

```bash
py tools/exys_audit.py
```

Valida: XML dos `.axaml`, handlers↔code-behind, bindings↔VM/models, comandos↔`[RelayCommand]`, coerência de abas (`SelectedTabIndex`↔`IsXxxTab`↔páginas), reachability, `x:Name`, verbos HTTP. Estado saudável: **0 FAIL / 0 WARN** (INFOs são estado interno justificado).

## Testes

**Quando usar:** módulos puros do Core (sem UI).

```bash
dotnet test tests/LauncherMine.Tests -c Release
```

xUnit. Ao testar formatos externos (UUID Mojang etc.), **verificar o vetor por implementação independente** (ex: PowerShell/MD5 manual) — já evitou um falso negativo.

## Debug de autenticação externa

**Quando usar:** qualquer falha em OAuth/Xbox/Minecraft services.

1. Reproduzir a chamada HTTP **fora do app** (curl/PowerShell) para ver o corpo real do erro — a Microsoft devolve `AADSTS*`/`XErr` detalhados que `EnsureSuccessStatusCode` esconde.
2. Comparar credenciais/endpoints com launchers OSS maduros (Prism, Modrinth) — fonte de verdade para client IDs e scopes.
3. Adicionar `EnsureSuccessWithDetailsAsync` para incluir status + corpo nas exceções.
4. Nunca deixar erro morrer em silêncio: dialog mostra `StatusText`, footer mostra `StatusMessage`.

## Versionamento diário

**Quando usar:** fim de cada sessão de trabalho.

Registrar em `Obsidian/Versões/AAAA-MM-DD.md` com blocos `## HHh` (feito/decidido/pendente/links). Migrar tarefas concluídas no board.

**Links:** [[Planos/Tarefas]] · [[Home]]
