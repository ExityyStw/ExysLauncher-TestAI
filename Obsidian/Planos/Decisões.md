---
tipo: decisoes
atualizado: 2026-11-27
---

# Decisões (ADRs)

Registro do porquê das escolhas — para não re-litigar o que já foi decidido.

## ADR-001: Tema único claro, sem alternância

**Data:** 2026-11-27 · **Status:** aceita

**Contexto:** existia toggle claro/escuro + persistência de `isDarkMode`.
**Decisão:** remoção total — `RequestedThemeVariant="Light"` forçado, dicionário Dark removido do `App.axaml`, props/comando de tema deletados do VM.
**Motivo:** usuário avaliou o layout como final; a feature gerava superfície de manutenção sem valor.
**Consequência:** qualquer pedido futuro de tema exige reverter esta ADR.

## ADR-002: Device code flow Microsoft com client ID público do Prism

**Data:** 2026-11-27 · **Status:** aceita

**Contexto:** client ID próprio (`00000000402b5328`) retornava `AADSTS700016` — app não existe no tenant consumers.
**Decisão:** usar `c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb` + scope `XboxLive.SignIn offline_access` — configuração pública que o Prism Launcher usa, validada ao vivo.
**Consequência:** dependência de client ID de terceiro; se a Microsoft revogar, precisa de registro próprio (Azure App Registration).

## ADR-003: Locks com núcleo sem reentrância no AccountManager

**Data:** 2026-11-27 · **Status:** aceita

**Contexto:** `AddOrUpdateAccountAsync` segurava `SemaphoreSlim` e chamava `SaveAccountsAsync`, que readquiria o mesmo semáforo → deadlock silencioso (contas nunca salvas, eventos nunca disparados).
**Decisão:** extrair `SaveAccountsCoreAsync()` sem lock, chamado por quem já detém `_lock`; métodos públicos mantêm lock próprio.
**Regra derivada:** dentro de seção crítica, nunca chamar método público que readquire o mesmo lock. Testes de regressão com timeout em `AccountManagerTests`.

## ADR-004: Stack Avalonia 11 + CmlLib + .NET 8 self-contained

**Data:** início do projeto · **Status:** aceita

C#/.NET 8, UI Avalonia (cross-platform futura), `CmlLib.Core` para launch/instalação de versões, publish `win-x64` self-contained em `dist/windows/`.

## ADR-005: Segredos em ISecureVault do SO (DPAPI no Windows)

**Data:** início do projeto · **Status:** aceita

Refresh tokens nunca em texto plano: `WindowsSecureVault` (DPAPI) / `LinuxSecureVault` atrás da interface `ISecureVault`, via `SecureVaultFactory`.

**Links:** [[Sínteses/Estado Atual]] · [[Projeto/ExysLauncher]]
