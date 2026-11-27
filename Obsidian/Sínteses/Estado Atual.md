---
tipo: sintese
atualizado: 2026-11-27
---

# Estado Atual — ExysLauncher

## Diagnóstico em uma frase

Launcher funcional, auditado e testado; o único risco aberto é a **validação end-to-end do login Microsoft**, que já passou por três bugs em camadas distintas (exibição, credencial, retorno).

## Saúde técnica

| Dimensão | Estado | Evidência |
|---|---|---|
| Build | ✅ limpo | 0 avisos / 0 erros, Release |
| Auditoria estática | ✅ limpa | `exys_audit.py`: 0 FAIL / 0 WARN / 11 INFO |
| Testes | ✅ 36/36 | xUnit: PathSecurity, OfflineAuth, AccountManager |
| Persistência | ✅ | prefs em `ui-settings.json`, contas em `accounts.json`, segredos no DPAPI |
| Auth offline | ✅ | fluxo completo testado |
| Auth Microsoft | 🟡 | corrigido, **aguardando validação do usuário** |
| Launch (CmlLib) | 🟡 | código pronto; nunca confirmado com conta Microsoft real |

## Riscos

1. **Login Microsoft E2E** — se XSTS/perfil falhar, o erro agora aparece no dialog/footer. Próximo passo é o usuário testar e reportar.
2. **Client ID de terceiro** — [[Planos/Decisões|ADR-002]] depende do app público do Prism; revogação exige registro próprio no Azure.
3. **Sem auto-update** — exe self-contained sem mecanismo de atualização; cada fix exige redistribuir o binário.

## Prioridade recomendada (ver [[Planos/Tarefas]])

1. Confirmar login Microsoft (conta visível + JOGAR abre o jogo).
2. Toast notifications + progresso real em operações longas — maior ganho de UX percebida.
3. Assinatura ED25519 real do manifesto — fecha o item de segurança pendente.
4. Auto-update — destrava distribuição contínua.

## Padrões que emergiram (ver [[Planos/Métodos]])

- **Erro silencioso é o bug recorrente:** deadlock sem exceção, dialog sem `ShowDialog`, `PlayAsync` com return mudo. Regra: todo caminho de falha precisa de superfície visível (`StatusText`/`StatusMessage`/crash log).
- **HTTP externo exige reprodução fora do app** — corpo do erro da Microsoft só aparece fora do `EnsureSuccessStatusCode`.
- **Lock não-reentrante + evento síncrono = deadlock** — núcleo sem lock dentro da seção crítica ([[Planos/Decisões|ADR-003]]).

**Links:** [[Planos/Tarefas]] · [[Planos/Ideias]] · [[Planos/Decisões]] · [[Versões/Índice]] · [[Projeto/ExysLauncher]]
