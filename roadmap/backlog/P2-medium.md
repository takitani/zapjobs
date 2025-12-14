# P2 - Media Prioridade

Features de experiencia do desenvolvedor e dashboard avancado.

**Foco:** Developer Experience (DX) e usabilidade

| # | Feature | Pilar | Esforco | Impacto | Status | Prompt |
|---|---------|-------|---------|---------|--------|--------|
| 1 | SignalR Dashboard | DX | A | UX | Pendente | [12-signalr-dashboard.md](../prompts/12-signalr-dashboard.md) |
| 2 | REST API Standalone | DX | M | Operacional | Pendente | [13-rest-api.md](../prompts/13-rest-api.md) |
| 3 | Dark Mode | DX | B | UX | Pendente | [14-dark-mode.md](../prompts/14-dark-mode.md) |
| 4 | Calendar Exclusions | CORE | M | Paridade | Pendente | [16-calendar-exclusions.md](../prompts/16-calendar-exclusions.md) |
| 5 | Job Templates | DX | M | DevX | Pendente | [18-job-templates.md](../prompts/18-job-templates.md) |
| 6 | Unique Jobs / Dedup | CORE | M | Paridade | Pendente | [22-unique-jobs.md](../prompts/22-unique-jobs.md) |

## Justificativas

### Dashboard (SignalR + Dark Mode)
- **SignalR:** TickerQ tem, updates em tempo real sem polling
- **Dark Mode:** Usuarios pedem, melhora experiencia noturna
- **Impacto:** Melhora percepcao de qualidade do produto

### REST API Standalone
- **Por que:** Permite integracao com sistemas externos
- **Diferencial:** Swagger/OpenAPI automatico
- **Casos de uso:** Microservicos, ferramentas de deploy, automacao

### Calendar Exclusions
- **Por que:** Quartz.NET tem, pular feriados e fins de semana
- **Casos de uso:** Jobs de relatorio que so rodam em dias uteis
- **Implementacao:** Baseado em calendarios configuráveis

### Unique Jobs / Deduplication
- **Por que:** BullMQ e Sidekiq Pro tem, previne jobs duplicados
- **Modos sugeridos:**
  - `UntilExecuting`: Lock ate job comecar
  - `UntilExecuted`: Lock ate job terminar
  - `Throttle`: Ignora por TTL

### Job Templates
- **Por que:** Reutilizar configuracoes comuns
- **Casos de uso:** Jobs com retry/timeout/queue padronizados

## Ordem de Implementacao Sugerida

1. **Dark Mode** (B) - Quick win, melhora UX imediatamente
2. **REST API** (M) - Habilita integracao externa
3. **Unique Jobs** (M) - Previne problemas em producao
4. **Calendar Exclusions** (M) - Paridade com Quartz.NET
5. **Job Templates** (M) - Melhora DX
6. **SignalR Dashboard** (A) - Maior esforco, maior impacto visual
