# Prompt: Implementar Testes de Integração

## Objetivo

Criar testes de integração para validar o comportamento end-to-end do ZapJobs com PostgreSQL real usando Testcontainers.

## Contexto

O projeto tem testes unitários (após implementar 01-unit-tests.md), mas precisa de testes de integração para validar:
- Operações de storage PostgreSQL
- Ciclo completo de execução de jobs
- Scheduling e polling
- Retry e recovery

## Requisitos

### Dependências Adicionais
```xml
<PackageReference Include="Testcontainers.PostgreSql" Version="3.10.0" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.0" />
```

### Estrutura
```
tests/
└── ZapJobs.IntegrationTests/
    ├── ZapJobs.IntegrationTests.csproj
    ├── Fixtures/
    │   ├── PostgreSqlFixture.cs
    │   └── ZapJobsFixture.cs
    ├── Storage/
    │   └── PostgreSqlStorageIntegrationTests.cs
    ├── Execution/
    │   └── JobExecutionIntegrationTests.cs
    └── Scheduling/
        └── SchedulingIntegrationTests.cs
```

### Testes Necessários

#### PostgreSqlStorageIntegrationTests
1. `EnqueueAndGetRun_RoundTrip_PreservesData`
2. `TryAcquireRun_ConcurrentWorkers_OnlyOneSucceeds`
3. `GetDueJobs_ReturnsOnlyDue`
4. `CleanupOldRuns_RemovesOnlyOld`
5. `Migration_CreatesAllTables`
6. `CustomSchemaAndPrefix_UsesCorrectNames`

#### JobExecutionIntegrationTests
1. `ExecuteJob_Success_CompletesAndLogs`
2. `ExecuteJob_Failure_RetriesAndFails`
3. `ExecuteJob_Timeout_MarksAsFailed`
4. `ExecuteJob_WithInput_DeserializesCorrectly`
5. `ExecuteJob_WithOutput_SerializesResult`
6. `ExecuteJob_ConcurrentJobs_ExecutesInParallel`

#### SchedulingIntegrationTests
1. `RecurringJob_EnqueuesOnSchedule`
2. `DelayedJob_ExecutesAfterDelay`
3. `CancelJob_PreventsExecution`
4. `MultipleWorkers_DistributeJobs`

## Exemplo de Fixture

```csharp
public class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;
    public string ConnectionString => _container.GetConnectionString();
    public PostgreSqlJobStorage Storage { get; private set; } = null!;

    public PostgreSqlFixture()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("zapjobs_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new PostgreSqlStorageOptions
        {
            ConnectionString = ConnectionString,
            AutoMigrate = false
        };

        var runner = new MigrationRunner(options);
        await runner.MigrateAsync();

        Storage = new PostgreSqlJobStorage(options);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("PostgreSQL")]
public class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture> { }
```

## Exemplo de Teste

```csharp
[Collection("PostgreSQL")]
public class PostgreSqlStorageIntegrationTests
{
    private readonly PostgreSqlFixture _fixture;
    private IJobStorage Storage => _fixture.Storage;

    public PostgreSqlStorageIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TryAcquireRun_ConcurrentWorkers_OnlyOneSucceeds()
    {
        // Arrange
        var run = new JobRun
        {
            JobTypeId = "test-job",
            Status = JobRunStatus.Pending,
            Queue = "default"
        };
        await Storage.EnqueueAsync(run);

        // Act - Simulate 10 concurrent workers
        var tasks = Enumerable.Range(1, 10)
            .Select(i => Storage.TryAcquireRunAsync(run.Id, $"worker-{i}"))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Count(r => r).Should().Be(1, "only one worker should acquire");
        results.Count(r => !r).Should().Be(9, "others should fail");
    }
}
```

## Critérios de Aceitação

1. [ ] Testes usam Testcontainers (não dependem de PostgreSQL instalado)
2. [ ] Cada teste é isolado (cleanup entre testes)
3. [ ] Testes paralelos não interferem entre si
4. [ ] Todos os testes passam em CI/CD
5. [ ] Tempo total < 2 minutos
6. [ ] Documentação de requisitos (Docker)
