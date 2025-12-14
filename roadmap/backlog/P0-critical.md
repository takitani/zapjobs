# P0 - Critico

Items bloqueadores para publicacao e producao.

**Pilar obrigatorio:** QUALITY ou TRUST em cada ciclo

| # | Feature | Pilar | Esforco | Impacto | Status | Prompt |
|---|---------|-------|---------|---------|--------|--------|
| 1 | Unit Tests | QUALITY | A | Blocker | Pendente | [01-unit-tests.md](../prompts/01-unit-tests.md) |
| 2 | Integration Tests | QUALITY | M | Blocker | Pendente | [02-integration-tests.md](../prompts/02-integration-tests.md) |
| 3 | GitHub Actions CI/CD | QUALITY | M | Blocker | Pendente | [04-github-actions.md](../prompts/04-github-actions.md) |
| 4 | NuGet Packages | ADOPT | M | Blocker | Pendente | [03-nuget-packages.md](../prompts/03-nuget-packages.md) |

## Justificativas

### 1. Unit Tests
- **Por que P0:** Sem testes, nao ha confianca para publicar no NuGet
- **Impacto:** Garante que o core funciona, permite refatoracao segura
- **Dependencias:** Nenhuma

### 2. Integration Tests
- **Por que P0:** Storage PostgreSQL precisa de testes reais
- **Impacto:** Valida que jobs funcionam end-to-end
- **Dependencias:** Unit Tests (compartilham infra)

### 3. GitHub Actions CI/CD
- **Por que P0:** Automatiza build/test em cada PR
- **Impacto:** Qualidade garantida, publicacao automatizada
- **Dependencias:** Unit Tests, Integration Tests

### 4. NuGet Packages
- **Por que P0:** Sem NuGet, ninguem pode usar a biblioteca
- **Impacto:** Distribuicao para toda comunidade .NET
- **Dependencias:** Todos os testes passando, CI/CD funcionando
