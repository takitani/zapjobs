# Pilares do Projeto - ZapJobs

**Configurado em:** 2025-12-13
**Ultima revisao:** 2025-12-13
**Tipo:** Biblioteca Open Source (.NET Job Scheduler)

## Regras de Balanceamento

- [ ] Cada ciclo deve ter itens de **3+ pilares diferentes**
- [ ] Cada ciclo deve ter **1+ item de Trust ou Quality**
- [ ] Nenhum pilar deve ter mais de **50% dos itens** do ciclo

## Pilares Ativos

### CORE - Core Features
**Descricao:** Funcionalidades principais do scheduler que diferenciam o produto
**Metricas:** Feature Parity Score, API Coverage
**Exemplos:** Batch jobs, Dead Letter Queue, Rate Limiting, Job Continuations
**Prioridade:** Alta (foco atual)

### ADOPT - Adocao/Distribuicao
**Descricao:** Tudo que facilita adocao e uso da biblioteca
**Metricas:** NuGet Downloads, GitHub Stars, Contributors
**Exemplos:** Publicacao NuGet, documentacao, samples, getting started

### QUALITY - Qualidade/Testes
**Descricao:** Testes, CI/CD, cobertura e confiabilidade
**Metricas:** Test Coverage, Build Success Rate, Bug Count
**Exemplos:** Unit tests, integration tests, GitHub Actions, benchmarks
**Obrigatorio:** Pelo menos 1 item por ciclo

### TRUST - Trust & Safety
**Descricao:** Seguranca, resiliencia e confiabilidade em producao
**Metricas:** Security Issues, Uptime, Error Rate
**Exemplos:** Prevent overlapping, misfire handling, health checks, auth
**Obrigatorio:** Pelo menos 1 item por ciclo

### OBSERVE - Observabilidade
**Descricao:** Metricas, tracing e monitoring para operacao
**Metricas:** MTTR, Alert Coverage, Dashboard Usage
**Exemplos:** OpenTelemetry, Prometheus, webhooks, logging

### DX - Developer Experience
**Descricao:** Ergonomia da API, tooling e produtividade
**Metricas:** API Satisfaction, Time to First Job, Support Tickets
**Exemplos:** Fluent API, source generators, CLI improvements, REST API

### PLATFORM - Plataforma/Storage
**Descricao:** Backends de storage e integracao com ecossistema
**Metricas:** Storage Options, Integration Count
**Exemplos:** Redis storage, SQLite, MySQL, EF Core, Polly

## Mapeamento do Roadmap Atual

| Feature | Pilar | Fase |
|---------|-------|------|
| Unit Tests | QUALITY | 1 |
| Integration Tests | QUALITY | 1 |
| GitHub Actions | QUALITY | 1 |
| NuGet Packages | ADOPT | 1 |
| Job Continuations | CORE | 1 (Feito) |
| Batch Jobs | CORE | 1 |
| Dead Letter Queue | CORE | 1 |
| Prevent Overlapping | TRUST | 1 |
| OpenTelemetry | OBSERVE | 2 |
| Prometheus Metrics | OBSERVE | 2 |
| Health Checks | TRUST | 2 |
| Webhooks | OBSERVE | 2 |
| SignalR Dashboard | DX | 3 |
| REST API | DX | 3 |
| Rate Limiting | CORE | 4 (Feito) |
| Calendar Exclusions | CORE | 4 |
| Job Templates | DX | 4 |
| Multi-tenancy | PLATFORM | 4 |
| Redis Storage | PLATFORM | 5 |
| SQLite Storage | PLATFORM | 5 |
| Source Generators | DX | 6 |

## Historico de Alteracoes

| Data | Alteracao | Motivo |
|------|-----------|--------|
| 2025-12-13 | Configuracao inicial | Setup com foco em Features Core |
