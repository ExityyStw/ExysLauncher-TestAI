# 🏠 ExysLauncher — Vault

Mapa de conteúdo central. Tudo no projeto passa por aqui.

## Fluxo de informação

```
Versões (o que aconteceu)  →  Planos (o que fazer)  →  Sínteses (o que significa)
```

- **[[Versões/Índice|Versões]]** — log diário com blocos por hora. Toda sessão de trabalho gera uma entrada.
- **[[Planos/Tarefas|Tarefas]]** — board do que está pendente, em andamento e concluído.
- **[[Planos/Ideias|Ideias]]** — backlog de possibilidades com status (bruta → aprovada → descartada).
- **[[Planos/Métodos|Métodos]]** — playbooks reutilizáveis: como buildar, auditar, testar, debugar.
- **[[Planos/Decisões|Decisões]]** — ADRs: o porquê das escolhas arquiteturais.
- **[[Sínteses/Estado Atual|Estado Atual]]** — síntese viva do projeto: saúde, riscos e prioridades.
- **[[Projeto/ExysLauncher|ExysLauncher]]** — contexto do projeto (stack, estrutura, links externos).

## Regras do vault

1. **Uma nota, um assunto.** Detalhes vivem na nota dona; outras notas só linkam.
2. **Wikilinks com propósito.** Lincar quando a conexão muda a leitura — nunca "por via das dúvidas".
3. **Versão diária é obrigatória.** Ao fim de cada sessão: `Versões/AAAA-MM-DD.md` com blocos `## HHh`.
4. **Tarefa concluída migra.** Move de "pendente" para "concluído" linkando o dia da versão.
5. **Ideia morta não se apaga.** Marca como `descartada` com o motivo — evita ressuscitar discussão.

## Templates

`_templates/` contém: `Versão Diária`, `Tarefa`, `Ideia`, `Método`, `Decisão`.
