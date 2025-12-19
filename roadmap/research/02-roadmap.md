# ZapJobs Roadmap

Roadmap priorizado baseado em análise de concorrentes e necessidades do projeto.

## Análise de Concorrentes

### Hangfire
**O mais popular job scheduler para .NET**

| Feature | Hangfire | ZapJobs |
|---------|----------|---------|
| Dashboard built-in | Sim | Sim |
| Fire-and-forget | Sim | Sim |
| Delayed jobs | Sim | Sim |
| Recurring/CRON | Sim | Sim |
| Retries automáticos | Sim | Sim |
| Continuations (chains) | Sim | Sim |
| Batch jobs (atomico) | Pro ($500/yr) | ✅ Sim (grátis!) |
| Nested batches | Pro | ✅ Sim (grátis!) |
| Dead letter queue | Pro | ✅ Sim (grátis!) |
| Rate limiting | Pro | ✅ Sim (grátis!) |
| Event broadcasting | Pro | ✅ Sim (grátis!) |
| Event history | Não | ✅ Sim |
| SQL Server storage | Sim | PostgreSQL |
| Redis storage | Sim | **Falta** |
| Publicação NuGet | Sim | **Falta** |

**Fontes**: [Hangfire Overview](https://www.hangfire.io/overview.html), [Hangfire Batches](https://docs.hangfire.io/en/latest/background-methods/using-batches.html)

### Quartz.NET
**Enterprise-grade scheduler com CRON avançado**

| Feature | Quartz.NET | ZapJobs |
|---------|------------|---------|
| CRON expressions | Avançado | Básico (via Cronos) |
| Calendar exclusions | Sim | **Falta** |
| Clustering/HA | Sim | Parcial (workers) |
| Multiple triggers | Sim | Parcial |
| Misfire handling | Sim | **Falta** |
| Dashboard | Não | Sim |
| Database support | Multi | PostgreSQL only |

**Fontes**: [Quartz.NET Cron](https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/crontriggers.html), [Quartz GitHub](https://github.com/quartznet/quartznet)

### TickerQ
**Moderno, reflection-free**

| Feature | TickerQ | ZapJobs |
|---------|---------|---------|
| Source generators | Sim | **Falta** |
| SignalR real-time | Sim | **Falta** |
| Parent-child workflows | Sim | Sim |
| EF Core integration | Sim | Npgsql direto |
| Distributed locking | Sim | Sim |
| Dashboard | Vue.js | Vanilla JS |
| Priority queues | Sim | Sim |

**Fontes**: [TickerQ GitHub](https://github.com/Arcenox-co/TickerQ), [TickerQ Article](https://antondevtips.com/blog/tickerq-the-modern-dotnet-job-scheduler-that-beats-quartz-and-hangfire)

### Coravel
**Zero-config, simples**

| Feature | Coravel | ZapJobs |
|---------|---------|---------|
| Fluent API | Excelente | Bom |
| Zero config | Sim | Quase |
| Prevent overlapping | Sim | **Falta** |
| Event broadcasting | Sim | ✅ Sim |
| Checkpoints/Resume | Não | ✅ Sim |
| Event history | Não | ✅ Sim |
| Persistence | Não | Sim |
| Dashboard | Não | Sim |
| DI integration | Nativo | Sim |

**Fontes**: [Coravel Docs](https://docs.coravel.net/Scheduler/), [Coravel GitHub](https://github.com/jamesmh/coravel)

---

## Roadmap Priorizado

### Fase 1: Foundation (Alta Prioridade)

#### 1.1 Qualidade e CI/CD
| Item | Descrição | Esforço | Impacto |
|------|-----------|---------|---------|
| Unit Tests | Testes unitários para Core e principais serviços | Alto | Crítico |
| Integration Tests | Testes de integração com PostgreSQL | Médio | Crítico |
| GitHub Actions | CI/CD para build, test, publish | Médio | Alto |
| NuGet Packages | Publicação dos 6 pacotes no NuGet | Médio | Crítico |

#### 1.2 Features Core Faltantes (Inspirado em Hangfire)
| Item | Descrição | Esforço | Impacto | Status |
|------|-----------|---------|---------|--------|
| Job Continuations | `ContinueWith` para encadear jobs | Médio | Alto | ✅ Feito |
| Batch Jobs | Criar grupo de jobs atomicamente | Alto | Alto | ✅ Feito |
| Dead Letter Queue | Jobs que falharam permanentemente | Médio | Alto | ✅ Feito |
| Rate Limiting | Limitar execuções por período | Médio | Médio | ✅ Feito |
| Event Broadcasting | Sistema de eventos para extensibilidade | Médio | Médio | ✅ Feito |
| Checkpoints/Resume | Estado durável para jobs longos | Alto | Alto | ✅ Feito |
| Event History/Replay | Auditoria e time-travel debugging | Alto | Alto | ✅ Feito |
| Prevent Overlapping | Opção para evitar execuções simultâneas | Baixo | Médio | ✅ Feito |

### Fase 2: Observability (Média Prioridade)

#### 2.1 Métricas e Tracing
| Item | Descrição | Esforço | Impacto | Status |
|------|-----------|---------|---------|--------|
| OpenTelemetry | Traces e spans para jobs | Médio | Alto | ✅ Feito |
| Prometheus Metrics | Exportar métricas para Prometheus | Médio | Alto | ✅ Feito (via OpenTelemetry) |
| Health Checks | IHealthCheck para ASP.NET Core | Baixo | Médio | ✅ Feito |

#### 2.2 Webhooks e Notificações
| Item | Descrição | Esforço | Impacto |
|------|-----------|---------|---------|
| Webhooks | Notificar URLs em eventos de jobs | Médio | Alto |
| Event System | Eventos internos para extensibilidade | Médio | Médio |

### Fase 3: Dashboard Avançado (Média Prioridade)

#### 3.1 Real-time (Inspirado em TickerQ)
| Item | Descrição | Esforço | Impacto |
|------|-----------|---------|---------|
| SignalR Integration | Dashboard com updates real-time | Alto | Alto |
| Dark Mode | Tema escuro para dashboard | Baixo | Baixo |
| Job Management UI | Criar/editar jobs pelo dashboard | Médio | Médio |

#### 3.2 API REST Standalone
| Item | Descrição | Esforço | Impacto |
|------|-----------|---------|---------|
| REST API | API completa para gerenciamento | Médio | Alto |
| OpenAPI/Swagger | Documentação automática da API | Baixo | Médio |

### Fase 4: Advanced Features (Baixa Prioridade)

#### 4.1 Scheduling Avançado (Inspirado em Quartz.NET)
| Item | Descrição | Esforço | Impacto |
|------|-----------|---------|---------|
| Calendar Exclusions | Excluir feriados, fins de semana | Médio | Médio |
| Misfire Handling | Estratégias para jobs perdidos | Médio | Médio |
| Rate Limiting | Limitar execuções por período | Médio | Médio |
| Job Priority | Prioridade dentro da mesma fila | Baixo | Baixo |

#### 4.2 Extensibilidade
| Item | Descrição | Esforço | Impacto |
|------|-----------|---------|---------|
| Job Templates | Templates reutilizáveis | Médio | Médio |
| Bulk Operations | Operações em lote | Médio | Baixo |
| Multi-tenancy | Suporte a múltiplos tenants | Alto | Médio |

### Fase 5: Storage Backends (Futuro)

| Item | Descrição | Esforço | Impacto |
|------|-----------|---------|---------|
| SQLite Storage | Para aplicações embarcadas | Médio | Médio |
| Redis Storage | Para alta performance | Alto | Médio |
| MySQL Storage | Para compatibilidade | Médio | Baixo |
| MongoDB Storage | Para cenários NoSQL | Alto | Baixo |

### Fase 6: Performance (Futuro)

| Item | Descrição | Esforço | Impacto |
|------|-----------|---------|---------|
| Source Generators | Descoberta de jobs compile-time | Alto | Médio |
| EF Core Storage | Alternativa ao Npgsql direto | Alto | Baixo |
| Polly Integration | Resiliência e circuit breaker | Médio | Médio |

---

## Features Copiadas dos Concorrentes

### Do Hangfire (Mais Popular)
1. **Job Continuations** - Encadear jobs com `ContinueWith`
2. **Batch Jobs** - Grupo atômico de jobs (como transação)
3. **Nested Batches** - Batches dentro de batches

### Do Quartz.NET (Enterprise)
1. **Calendar Exclusions** - Pular feriados/fins de semana
2. **Misfire Strategies** - O que fazer quando job perde execução
3. **Multiple Triggers** - Mais de um trigger por job

### Do TickerQ (Moderno)
1. **SignalR Dashboard** - Updates em real-time
2. **Source Generators** - Zero reflection em runtime
3. **Parent-Child Workflows** - Jobs com dependências

### Do Coravel (Simples)
1. **Prevent Overlapping** - Evitar execuções simultâneas
2. **Event Broadcasting** - Sistema de eventos para hooks
3. **Fluent Improvements** - API ainda mais simples

---

## Diferenciais do ZapJobs

O que já fazemos melhor:

1. **Dashboard Incluído** - Quartz.NET não tem
2. **Persistence** - Coravel é só in-memory
3. **Schema Customizável** - Prefixo e schema PostgreSQL
4. **CLI Completo** - Setup interativo com wizard
5. **100% Open Source** - Sem versão Pro paga (vs Hangfire)
6. **Typed Jobs** - Input/Output com generics
7. **Batch Jobs Grátis** - Hangfire cobra $500/ano
8. **Event History** - Temporal limita 51,200 eventos; ZapJobs ilimitado
9. **Checkpoints/Resume** - Jobs longos sobrevivem crashes
10. **Time-Travel Debugging** - Reconstruir estado em qualquer ponto

---

## Estimativa de Releases

### v1.0 - MVP (Atual)
- Core funcional
- PostgreSQL + InMemory storage
- Dashboard + CLI
- CRON + Interval scheduling
- Retry com backoff

### v1.1 - Quality
- Unit tests
- Integration tests
- GitHub Actions CI/CD
- NuGet packages publicados

### v1.2 - Chaining ✅
- ~~Job continuations~~ ✅
- ~~Dead letter queue~~ ✅
- ~~Batch jobs~~ ✅
- ~~Nested batches~~ ✅
- Prevent overlapping

### v1.3 - Advanced Features ✅
- ~~Rate limiting~~ ✅
- ~~Event broadcasting~~ ✅
- ~~Checkpoints/Resume~~ ✅
- ~~Event history/Replay~~ ✅

### v1.4 - Observability
- OpenTelemetry
- Prometheus metrics
- Webhooks

### v2.0 - Real-time
- SignalR dashboard
- REST API standalone

### v2.1 - Advanced
- Calendar exclusions
- Multi-tenancy

---

## Próximos Passos

1. **Imediato**: Implementar testes e CI/CD
2. **Curto prazo**: Publicar NuGet packages
3. **Médio prazo**: Job continuations e webhooks
4. **Longo prazo**: SignalR e features avançadas

Consulte a pasta `prompts/` para instruções detalhadas de implementação de cada feature.
