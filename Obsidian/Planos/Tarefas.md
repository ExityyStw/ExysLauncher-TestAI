---
tipo: board
atualizado: 2026-11-27
---

# Tarefas

## 🔴 Em andamento

- [ ] **Validar login Microsoft end-to-end** — prioridade máxima. Fluxo corrigido (client ID + `offline_access` + `ShowDialog`), mas ainda não confirmado: conta aparecer em `accounts.json` e launch funcionar.
  - Critério de pronto: conta Microsoft visível no card/menu **e** `JOGAR` abrindo o jogo com sessão válida.

## 🟡 Pendentes (por prioridade)

- [ ] **Toast notifications** — feedback de ações sem depender do `StatusMessage` do rodapé.
- [ ] **Assinatura ED25519 real do manifesto** — estrutura pronta em `ManifestService`; falta a chave pública do servidor.
- [ ] **Auto-update do launcher** — checar GitHub Releases e baixar instalador.
- [ ] **Gerenciador de mods por instância** — adicionar/remover mods com lista/drag-and-drop.
- [ ] **Backup/exportar instância** — útil para troca de PC.
- [ ] **Mais testes** — `ModpackUpdater`, `ManifestService`, parsing de manifesto.
- [ ] **Onboarding de primeira execução** — fluxo guiado conta → instância.
- [ ] **Progresso em operações longas** — `ProgressBar` já existe no footer; ligar a downloads/instalação.
- [ ] **Instalador + ícone próprio** — MSIX/Inno Setup ou equivalente.
- [ ] **Acessar logs pela UI** — abrir pasta de logs / exportar relatório de bug.

## ✅ Concluídas recentemente

- [x] Deadlock no `AccountManager` (SemaphoreSlim reentrante) — [[Versões/2026-11-27]]
- [x] Login Microsoft: client ID inválido → credenciais do Prism + scope `XboxLive.SignIn offline_access` — [[Versões/2026-11-27]]
- [x] Dialog Microsoft nunca exibido (`StartLoginAsync` sem `ShowDialog`) — [[Versões/2026-11-27]]
- [x] Auditor `tools/exys_audit.py` + zeragem de FAIL/WARN — [[Versões/2026-11-27]]
- [x] Persistência de preferências em `ui-settings.json` + bug `CustomJavaPath` — [[Versões/2026-11-27]]
- [x] Estados vazios com CTA, crash reporting global, 36 testes xUnit — [[Versões/2026-11-27]]

**Links:** [[Planos/Ideias]] · [[Sínteses/Estado Atual]] · [[Planos/Métodos]]
