# ZapJobs Roadmap

Este diretorio contém a documentação completa do roadmap do ZapJobs, incluindo o estado atual do projeto, features planejadas e prompts de implementação.

## Estrutura

```
roadmap/
├── README.md                    # Este arquivo
├── 01-current-state.md          # Estado atual detalhado do projeto
├── 02-roadmap.md                # Roadmap priorizado com todas as features
└── prompts/                     # Prompts de implementação
    ├── README.md                # Índice dos prompts
    ├── 01-unit-tests.md
    ├── 02-integration-tests.md
    ├── 03-nuget-packages.md
    ├── 04-github-actions.md
    ├── 05-job-dependencies.md
    ├── 06-dead-letter-queue.md
    ├── 07-rate-limiting.md
    ├── 08-webhooks.md
    ├── 09-opentelemetry.md
    ├── 10-prometheus-metrics.md
    ├── 11-signalr-dashboard.md
    ├── 12-rest-api.md
    ├── 13-job-templates.md
    ├── 14-bulk-operations.md
    └── 15-multi-tenancy.md
```

## Visão Geral

O ZapJobs é um job scheduler database-driven para .NET que já possui:
- Core sólido com abstrações bem definidas
- Storage PostgreSQL e InMemory
- Dashboard web completo
- CLI interativo
- Sistema de retry com backoff exponencial
- Filas priorizadas
- Heartbeat e monitoramento de workers

O roadmap foca em:
1. **Qualidade** - Testes e CI/CD
2. **Distribuição** - Publicação NuGet
3. **Robustez** - Features avançadas de scheduling
4. **Observabilidade** - Métricas e tracing
5. **Extensibilidade** - Webhooks, templates, multi-tenancy

## Prioridades

### Alta Prioridade
- Testes unitários e de integração
- Publicação NuGet
- CI/CD com GitHub Actions
- Job dependencies (chains)
- Dead letter queue

### Média Prioridade
- Rate limiting
- Webhooks
- OpenTelemetry
- Prometheus metrics
- REST API standalone

### Baixa Prioridade (Futuro)
- SignalR real-time dashboard
- Job templates
- Bulk operations
- Multi-tenancy

## Como Usar os Prompts

Cada arquivo na pasta `prompts/` contém um prompt detalhado para implementação de uma feature específica. O formato inclui:

1. **Objetivo** - O que a feature faz
2. **Contexto** - Estado atual relevante
3. **Requisitos** - Lista de requisitos funcionais e técnicos
4. **Arquivos Envolvidos** - Quais arquivos criar/modificar
5. **Exemplo de Uso** - Como a feature será usada
6. **Critérios de Aceitação** - Como validar a implementação

Para implementar uma feature:
1. Leia o prompt correspondente
2. Revise o contexto do projeto em `01-current-state.md`
3. Implemente seguindo os requisitos
4. Valide com os critérios de aceitação

## Contribuindo

Para sugerir novas features ou melhorias:
1. Crie uma issue descrevendo a feature
2. Se aprovada, crie um prompt seguindo o formato existente
3. Submeta um PR com a implementação
