# ZapJobs - Roadmap

> Biblioteca de job scheduling database-driven para .NET. 100% open source, sem versao Pro.

**Ultima atualizacao:** 2025-12-19

## Status

| Prioridade | Total | Feito | Pendente | Descricao |
|------------|-------|-------|----------|-----------|
| P0 | 4 | 0 | 4 | Bloqueadores (Testes, CI/CD, NuGet) |
| P1 | 9 | 4 | 5 | Alto impacto (Core + Observabilidade) |
| P2 | 6 | 0 | 6 | Media prioridade (DX + Dashboard) |
| P3 | 13 | 2 | 11 | Futuro (Storage + Avancado) |
| **Total** | **32** | **6** | **26** | |

## Features Concluidas

| Feature | Pilar | Versao | Diferencial |
|---------|-------|--------|-------------|
| Job Continuations | CORE | 1.0 | Encadeamento de jobs |
| Batch Jobs | CORE | 1.0 | Grupos atomicos (Hangfire Pro $500/ano) |
| Dead Letter Queue | CORE | 1.0 | Jobs falhos para revisao |
| Rate Limiting | CORE | 1.0 | Controle de taxa (Hangfire Pro) |
| Event Broadcasting | CORE | 1.0 | Pub/sub para eventos de job lifecycle |
| Checkpoints/Resume | CORE | 1.0 | Estado duravel para jobs longos (Temporal-level) |

## Proximos (P0 Pendentes)

| # | Item | Pilar | Esforco | Prompt |
|---|------|-------|---------|--------|
| 1 | Unit Tests | QUALITY | A | [01-unit-tests.md](./prompts/01-unit-tests.md) |
| 2 | Integration Tests | QUALITY | M | [02-integration-tests.md](./prompts/02-integration-tests.md) |
| 3 | GitHub Actions | QUALITY | M | [04-github-actions.md](./prompts/04-github-actions.md) |
| 4 | NuGet Packages | ADOPT | M | [03-nuget-packages.md](./prompts/03-nuget-packages.md) |

## P1 Pendentes (Alto Impacto)

| # | Item | Pilar | Esforco | Diferencial |
|---|------|-------|---------|-------------|
| 1 | Prevent Overlapping | TRUST | B | Evita jobs duplicados |
| 2 | Health Checks | TRUST | B | Integracao ASP.NET Core |
| 3 | OpenTelemetry | OBSERVE | M | **Unico no mercado .NET** |
| 4 | Prometheus Metrics | OBSERVE | M | Observabilidade nativa |
| 5 | Webhooks | OBSERVE | M | Integracao externa |

## Estrutura do Roadmap

```
roadmap/
├── README.md                  # Este arquivo (resumo)
├── pillars.md                 # Framework de pilares do projeto
├── backlog/
│   ├── P0-critical.md         # Bloqueadores
│   ├── P1-high.md             # Alto impacto
│   ├── P2-medium.md           # Media prioridade
│   └── P3-future.md           # Futuro/Experimentacao
├── research/
│   ├── MARKET_ANALYSIS.md     # Analise competitiva
│   ├── 01-current-state.md    # Estado atual do projeto
│   └── 02-roadmap.md          # Roadmap detalhado (legado)
├── prompts/                   # Prompts de implementacao pendentes
│   ├── README.md
│   ├── 01-unit-tests.md
│   └── ...
├── completed/                 # Docs de features implementadas
└── archive/                   # Prompts implementados
    ├── 05-job-continuations.md
    ├── 06-dead-letter-queue.md
    ├── 15-batch-jobs.md
    ├── 17-rate-limiting.md
    ├── 26-event-broadcasting.md
    └── 30-checkpoints-resume.md
```

## Pilares do Projeto

| Pilar | Descricao | Obrigatorio |
|-------|-----------|-------------|
| **CORE** | Features principais do scheduler | - |
| **ADOPT** | Publicacao, documentacao, samples | - |
| **QUALITY** | Testes, CI/CD, cobertura | 1+ por ciclo |
| **TRUST** | Seguranca, resiliencia, producao | 1+ por ciclo |
| **OBSERVE** | Metricas, tracing, monitoring | - |
| **DX** | Developer Experience, API, tooling | - |
| **PLATFORM** | Storage backends, integracoes | - |

**Regras de balanceamento:**
- Cada ciclo deve ter itens de 3+ pilares diferentes
- Cada ciclo deve ter 1+ item de TRUST ou QUALITY
- Nenhum pilar deve ter mais de 50% dos itens do ciclo

## Diferenciais vs Concorrentes

### vs Hangfire
- **100% gratis:** Batches, Rate Limiting, Continuations (Pro = $500/ano)
- **Typed Jobs:** Input/Output com generics
- **PostgreSQL-first:** Sem dependencia de SQL Server
- **Checkpoints/Resume:** Hangfire nao tem

### vs Quartz.NET
- **Dashboard incluido:** Quartz nao tem
- **API moderna:** Fluent, async/await nativo
- **Mais simples:** Menos configuracao

### vs TickerQ
- **Mais features:** Batches, Dead Letter, Rate Limiting, Checkpoints
- **Mais storage:** PostgreSQL + InMemory (+ SQLite, Redis futuro)
- **100% open source:** Sem versao paga

### vs Temporal (Inspiracao)
- **Mais simples:** Checkpoints explicitos vs replay complexo
- **100% gratis:** Sem licenca enterprise
- **.NET nativo:** Nao requer SDK separado ou servidor externo

## Como Usar

### Ver backlog completo
```
/roadmap:list           # Lista todas as prioridades
/roadmap:list P0        # Apenas items criticos
```

### Detalhar um item
```
/roadmap:item batch-jobs
```

### Gerar prompt de implementacao
```
/roadmap:prompts --item unit-tests
```

## Links

- [Backlog P0 - Critico](./backlog/P0-critical.md)
- [Backlog P1 - Alto](./backlog/P1-high.md)
- [Backlog P2 - Medio](./backlog/P2-medium.md)
- [Backlog P3 - Futuro](./backlog/P3-future.md)
- [Analise de Mercado](./research/MARKET_ANALYSIS.md)
- [Pilares do Projeto](./pillars.md)
- [Prompts de Implementacao](./prompts/)
