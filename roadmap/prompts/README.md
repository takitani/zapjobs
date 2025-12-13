# Prompts de Implementação

Esta pasta contém prompts detalhados para implementação de cada feature do roadmap do ZapJobs.

## Como Usar

Cada prompt segue o formato:
1. **Objetivo** - O que a feature faz
2. **Contexto** - Estado atual relevante do projeto
3. **Requisitos** - Lista de requisitos funcionais e técnicos
4. **Arquivos Envolvidos** - Quais arquivos criar/modificar
5. **Exemplo de Uso** - Como a feature será utilizada
6. **Critérios de Aceitação** - Como validar a implementação

Para implementar, copie o conteúdo do prompt e cole no Claude Code ou sua ferramenta de AI coding preferida.

## Índice de Prompts

### Fase 1: Foundation

| # | Prompt | Prioridade | Esforço |
|---|--------|------------|---------|
| 01 | [Unit Tests](./01-unit-tests.md) | Crítica | Alto |
| 02 | [Integration Tests](./02-integration-tests.md) | Crítica | Médio |
| 03 | [NuGet Packages](./03-nuget-packages.md) | Crítica | Médio |
| 04 | [GitHub Actions CI/CD](./04-github-actions.md) | Alta | Médio |
| 05 | [Job Continuations](./05-job-continuations.md) | Alta | Médio |
| 06 | [Dead Letter Queue](./06-dead-letter-queue.md) | Alta | Médio |
| 07 | [Prevent Overlapping](./07-prevent-overlapping.md) | Média | Baixo |

### Fase 2: Observability

| # | Prompt | Prioridade | Esforço |
|---|--------|------------|---------|
| 08 | [Webhooks](./08-webhooks.md) | Alta | Médio |
| 09 | [OpenTelemetry](./09-opentelemetry.md) | Alta | Médio |
| 10 | [Prometheus Metrics](./10-prometheus-metrics.md) | Alta | Médio |
| 11 | [Health Checks](./11-health-checks.md) | Média | Baixo |

### Fase 3: Dashboard Avançado

| # | Prompt | Prioridade | Esforço |
|---|--------|------------|---------|
| 12 | [SignalR Real-time](./12-signalr-dashboard.md) | Alta | Alto |
| 13 | [REST API Standalone](./13-rest-api.md) | Alta | Médio |
| 14 | [Dark Mode](./14-dark-mode.md) | Baixa | Baixo |

### Fase 4: Advanced Features

| # | Prompt | Prioridade | Esforço |
|---|--------|------------|---------|
| 15 | [Batch Jobs](./15-batch-jobs.md) | Média | Alto |
| 16 | [Calendar Exclusions](./16-calendar-exclusions.md) | Média | Médio |
| 17 | [Rate Limiting](./17-rate-limiting.md) | Média | Médio |
| 18 | [Job Templates](./18-job-templates.md) | Baixa | Médio |
| 19 | [Multi-tenancy](./19-multi-tenancy.md) | Baixa | Alto |

### Fase 5: Storage Backends

| # | Prompt | Prioridade | Esforço |
|---|--------|------------|---------|
| 20 | [SQLite Storage](./20-sqlite-storage.md) | Média | Médio |
| 21 | [Redis Storage](./21-redis-storage.md) | Média | Alto |

## Ordem de Implementação Recomendada

1. **Unit Tests** - Base para todas as outras features
2. **Integration Tests** - Validar comportamento end-to-end
3. **GitHub Actions** - Automatizar builds e testes
4. **NuGet Packages** - Publicar para usuários
5. **Job Continuations** - Feature mais pedida
6. **Dead Letter Queue** - Importante para produção
7. **Webhooks** - Integração com sistemas externos
8. **OpenTelemetry** - Observabilidade moderna

## Convenções

### Nomenclatura
- Interfaces: `I` prefix (ex: `IJobChain`)
- Entidades: PascalCase (ex: `JobContinuation`)
- Métodos async: `Async` suffix

### Testes
- xUnit como framework
- FluentAssertions para assertions
- Moq para mocking
- Testcontainers para PostgreSQL

### Documentação
- XML comments em todas as interfaces públicas
- README por pacote NuGet
- Exemplos de código em todas as features
