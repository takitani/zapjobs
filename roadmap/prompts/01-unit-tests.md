# Prompt: Implementar Testes Unitários

## Objetivo

Criar uma suíte completa de testes unitários para o ZapJobs, cobrindo os principais componentes do Core e da implementação principal.

## Contexto

O projeto ZapJobs atualmente não possui testes. A estrutura é:
- `ZapJobs.Core` - Abstrações e entidades
- `ZapJobs` - Implementação principal (Scheduling, Execution, Tracking)
- `ZapJobs.Storage.InMemory` - Storage em memória
- `ZapJobs.Storage.PostgreSQL` - Storage PostgreSQL

## Requisitos

### Estrutura de Projetos de Teste
1. Criar `tests/ZapJobs.Core.Tests/`
2. Criar `tests/ZapJobs.Tests/`
3. Criar `tests/ZapJobs.Storage.InMemory.Tests/`

### Dependências
```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
<PackageReference Include="xunit" Version="2.9.2" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
<PackageReference Include="FluentAssertions" Version="6.12.1" />
<PackageReference Include="Moq" Version="4.20.72" />
<PackageReference Include="coverlet.collector" Version="6.0.2" />
```

### Testes Necessários

#### ZapJobs.Core.Tests
1. **RetryPolicyTests**
   - `CalculateDelay_FirstAttempt_ReturnsInitialDelay`
   - `CalculateDelay_WithBackoff_IncreasesExponentially`
   - `CalculateDelay_WithJitter_VariesWithinRange`
   - `CalculateDelay_ExceedsMaxDelay_CapsAtMaxDelay`
   - `CreateDefault_ReturnsValidPolicy`
   - `CreateAggressive_HasMoreRetries`

2. **JobExecutionContextTests**
   - `IncrementProcessed_IncreasesCount`
   - `IncrementSucceeded_IncreasesCount`
   - `IncrementFailed_IncreasesCount`
   - `GetMetrics_ReturnsCorrectValues`
   - `SetOutput_StoresValue`
   - `UpdateProgress_SetsProgressAndMessage`

3. **ZapJobsOptionsTests**
   - `Validate_ValidOptions_ReturnsTrue`
   - `Validate_ZeroWorkers_ReturnsFalse`
   - `GetDefaultTimeZone_ReturnsConfiguredOrUtc`

#### ZapJobs.Tests

1. **RetryHandlerTests**
   - `ShouldRetry_FirstAttempt_ReturnsTrue`
   - `ShouldRetry_MaxRetriesExceeded_ReturnsFalse`
   - `ShouldRetry_NonRetryableException_ReturnsFalse`
   - `IsRetryableException_ArgumentException_ReturnsFalse`
   - `IsRetryableException_HttpException_ReturnsTrue`

2. **CronSchedulerTests**
   - `IsValidExpression_ValidCron_ReturnsTrue`
   - `IsValidExpression_InvalidCron_ReturnsFalse`
   - `GetNextOccurrence_ReturnsCorrectTime`
   - `GetNextOccurrence_WithTimezone_RespectsTimezone`
   - `DescribeExpression_ReturnsHumanReadable`

3. **JobSchedulerServiceTests**
   - `EnqueueAsync_CreatesRunWithPendingStatus`
   - `EnqueueAsync_WithInput_SerializesInput`
   - `ScheduleAsync_WithDelay_SetsScheduledAt`
   - `RecurringAsync_WithInterval_SetsNextRun`
   - `RecurringAsync_WithCron_ValidatesExpression`
   - `CancelAsync_PendingJob_SetsCancelledStatus`
   - `CancelAsync_CompletedJob_ReturnsFalse`
   - `TriggerAsync_CreatesManualRun`

4. **JobExecutorTests**
   - `ExecuteAsync_SuccessfulJob_SetsCompletedStatus`
   - `ExecuteAsync_FailingJob_SetsFailedStatus`
   - `ExecuteAsync_Timeout_SetsTimedOutStatus`
   - `ExecuteAsync_RetryableError_SchedulesRetry`
   - `ExecuteAsync_MaxRetries_FailsPermanently`
   - `RegisterJobType_AddsToRegistry`

#### ZapJobs.Storage.InMemory.Tests

1. **InMemoryJobStorageTests**
   - `EnqueueAsync_ReturnsGuid`
   - `GetRunAsync_ExistingRun_ReturnsRun`
   - `GetRunAsync_NonExistent_ReturnsNull`
   - `GetPendingRunsAsync_ReturnsOnlyPending`
   - `TryAcquireRunAsync_FirstWorker_ReturnsTrue`
   - `TryAcquireRunAsync_SecondWorker_ReturnsFalse`
   - `UpsertDefinitionAsync_NewDefinition_Creates`
   - `UpsertDefinitionAsync_ExistingDefinition_Updates`
   - `GetDueJobsAsync_ReturnsDueJobs`
   - `CleanupOldRunsAsync_RemovesOldRuns`

## Arquivos a Criar

```
tests/
├── ZapJobs.Core.Tests/
│   ├── ZapJobs.Core.Tests.csproj
│   ├── Configuration/
│   │   ├── RetryPolicyTests.cs
│   │   └── ZapJobsOptionsTests.cs
│   └── Context/
│       └── JobExecutionContextTests.cs
│
├── ZapJobs.Tests/
│   ├── ZapJobs.Tests.csproj
│   ├── Execution/
│   │   ├── RetryHandlerTests.cs
│   │   └── JobExecutorTests.cs
│   └── Scheduling/
│       ├── CronSchedulerTests.cs
│       └── JobSchedulerServiceTests.cs
│
└── ZapJobs.Storage.InMemory.Tests/
    ├── ZapJobs.Storage.InMemory.Tests.csproj
    └── InMemoryJobStorageTests.cs
```

## Exemplo de Teste

```csharp
public class RetryPolicyTests
{
    [Fact]
    public void CalculateDelay_FirstAttempt_ReturnsInitialDelay()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(30),
            BackoffMultiplier = 2.0,
            UseJitter = false
        };

        // Act
        var delay = policy.CalculateDelay(attemptNumber: 1);

        // Assert
        delay.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(2, 60)]   // 30 * 2^1 = 60
    [InlineData(3, 120)]  // 30 * 2^2 = 120
    [InlineData(4, 240)]  // 30 * 2^3 = 240
    public void CalculateDelay_WithBackoff_IncreasesExponentially(
        int attempt, double expectedSeconds)
    {
        // Arrange
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(30),
            BackoffMultiplier = 2.0,
            UseJitter = false
        };

        // Act
        var delay = policy.CalculateDelay(attempt);

        // Assert
        delay.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }
}
```

## Critérios de Aceitação

1. [ ] Todos os testes passam com `dotnet test`
2. [ ] Coverage mínimo de 80% no Core
3. [ ] Coverage mínimo de 70% na implementação
4. [ ] Sem testes flaky (rodam consistentemente)
5. [ ] Testes executam em menos de 30 segundos
6. [ ] Documentação de como rodar testes no README

## Comandos Úteis

```bash
# Rodar todos os testes
dotnet test

# Rodar com coverage
dotnet test --collect:"XPlat Code Coverage"

# Rodar testes específicos
dotnet test --filter "FullyQualifiedName~RetryPolicyTests"

# Gerar relatório de coverage
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coverage" -reporttypes:Html
```
