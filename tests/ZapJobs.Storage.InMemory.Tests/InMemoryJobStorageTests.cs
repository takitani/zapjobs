using Xunit;
using FluentAssertions;
using ZapJobs.Core;
using ZapJobs.Storage.InMemory;

namespace ZapJobs.Storage.InMemory.Tests;

public class InMemoryJobStorageTests
{
    private readonly InMemoryJobStorage _storage;

    public InMemoryJobStorageTests()
    {
        _storage = new InMemoryJobStorage();
    }

    #region EnqueueAsync Tests

    [Fact]
    public async Task EnqueueAsync_ReturnsGuid()
    {
        // Arrange
        var run = new JobRun
        {
            JobTypeId = "test-job",
            Status = JobRunStatus.Pending
        };

        // Act
        var result = await _storage.EnqueueAsync(run);

        // Assert
        result.Should().NotBe(Guid.Empty);
        result.Should().Be(run.Id);
    }

    [Fact]
    public async Task EnqueueAsync_SetsCreatedAt()
    {
        // Arrange
        var run = new JobRun
        {
            JobTypeId = "test-job",
            CreatedAt = default
        };

        // Act
        var before = DateTime.UtcNow;
        await _storage.EnqueueAsync(run);
        var after = DateTime.UtcNow;

        // Assert
        run.CreatedAt.Should().BeOnOrAfter(before);
        run.CreatedAt.Should().BeOnOrBefore(after);
    }

    #endregion

    #region GetRunAsync Tests

    [Fact]
    public async Task GetRunAsync_ExistingRun_ReturnsRun()
    {
        // Arrange
        var run = new JobRun
        {
            JobTypeId = "test-job",
            Status = JobRunStatus.Pending,
            InputJson = "{\"test\":\"data\"}"
        };
        await _storage.EnqueueAsync(run);

        // Act
        var result = await _storage.GetRunAsync(run.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(run.Id);
        result.JobTypeId.Should().Be("test-job");
        result.InputJson.Should().Be("{\"test\":\"data\"}");
    }

    [Fact]
    public async Task GetRunAsync_NonExistent_ReturnsNull()
    {
        // Act
        var result = await _storage.GetRunAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetPendingRunsAsync Tests

    [Fact]
    public async Task GetPendingRunsAsync_ReturnsOnlyPending()
    {
        // Arrange
        var pendingRun = new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "default" };
        var runningRun = new JobRun { JobTypeId = "test", Status = JobRunStatus.Running, Queue = "default" };
        var completedRun = new JobRun { JobTypeId = "test", Status = JobRunStatus.Completed, Queue = "default" };

        await _storage.EnqueueAsync(pendingRun);
        await _storage.EnqueueAsync(runningRun);
        await _storage.EnqueueAsync(completedRun);

        // Act
        var result = await _storage.GetPendingRunsAsync(["default"]);

        // Assert
        result.Should().HaveCount(1);
        result.Should().Contain(r => r.Id == pendingRun.Id);
    }

    [Fact]
    public async Task GetPendingRunsAsync_FiltersbyQueue()
    {
        // Arrange
        var defaultRun = new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "default" };
        var criticalRun = new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "critical" };
        var lowRun = new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "low" };

        await _storage.EnqueueAsync(defaultRun);
        await _storage.EnqueueAsync(criticalRun);
        await _storage.EnqueueAsync(lowRun);

        // Act
        var result = await _storage.GetPendingRunsAsync(["critical"]);

        // Assert
        result.Should().HaveCount(1);
        result.Should().Contain(r => r.Id == criticalRun.Id);
    }

    [Fact]
    public async Task GetPendingRunsAsync_OrdersByCreatedAt()
    {
        // Arrange
        var run1 = new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "default" };
        var run2 = new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "default" };
        var run3 = new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "default" };

        await _storage.EnqueueAsync(run1);
        await Task.Delay(10);
        await _storage.EnqueueAsync(run2);
        await Task.Delay(10);
        await _storage.EnqueueAsync(run3);

        // Act
        var result = await _storage.GetPendingRunsAsync(["default"]);

        // Assert
        result.Should().HaveCount(3);
        result[0].Id.Should().Be(run1.Id);
        result[1].Id.Should().Be(run2.Id);
        result[2].Id.Should().Be(run3.Id);
    }

    [Fact]
    public async Task GetPendingRunsAsync_RespectsLimit()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            await _storage.EnqueueAsync(new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "default" });
        }

        // Act
        var result = await _storage.GetPendingRunsAsync(["default"], limit: 5);

        // Assert
        result.Should().HaveCount(5);
    }

    #endregion

    #region TryAcquireRunAsync Tests

    [Fact]
    public async Task TryAcquireRunAsync_FirstWorker_ReturnsTrue()
    {
        // Arrange
        var run = new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "default" };
        await _storage.EnqueueAsync(run);

        // Act
        var result = await _storage.TryAcquireRunAsync(run.Id, "worker-1");

        // Assert
        result.Should().BeTrue();

        var updatedRun = await _storage.GetRunAsync(run.Id);
        updatedRun!.Status.Should().Be(JobRunStatus.Running);
        updatedRun.WorkerId.Should().Be("worker-1");
        updatedRun.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquireRunAsync_SecondWorker_ReturnsFalse()
    {
        // Arrange
        var run = new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "default" };
        await _storage.EnqueueAsync(run);

        // Act
        var result1 = await _storage.TryAcquireRunAsync(run.Id, "worker-1");
        var result2 = await _storage.TryAcquireRunAsync(run.Id, "worker-2");

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireRunAsync_NonExistentRun_ReturnsFalse()
    {
        // Act
        var result = await _storage.TryAcquireRunAsync(Guid.NewGuid(), "worker-1");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireRunAsync_ConcurrentAccess_OnlyOneSucceeds()
    {
        // Arrange
        var run = new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "default" };
        await _storage.EnqueueAsync(run);

        // Act - simulate concurrent access
        var tasks = Enumerable.Range(1, 10)
            .Select(i => _storage.TryAcquireRunAsync(run.Id, $"worker-{i}"))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Count(r => r).Should().Be(1); // Only one should succeed
    }

    #endregion

    #region UpsertDefinitionAsync Tests

    [Fact]
    public async Task UpsertDefinitionAsync_NewDefinition_Creates()
    {
        // Arrange
        var definition = new JobDefinition
        {
            JobTypeId = "new-job",
            DisplayName = "New Job",
            ScheduleType = ScheduleType.Manual
        };

        // Act
        await _storage.UpsertDefinitionAsync(definition);

        // Assert
        var result = await _storage.GetJobDefinitionAsync("new-job");
        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("New Job");
    }

    [Fact]
    public async Task UpsertDefinitionAsync_ExistingDefinition_Updates()
    {
        // Arrange
        var definition = new JobDefinition
        {
            JobTypeId = "existing-job",
            DisplayName = "Original Name"
        };
        await _storage.UpsertDefinitionAsync(definition);

        // Act
        definition.DisplayName = "Updated Name";
        await _storage.UpsertDefinitionAsync(definition);

        // Assert
        var result = await _storage.GetJobDefinitionAsync("existing-job");
        result!.DisplayName.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpsertDefinitionAsync_SetsUpdatedAt()
    {
        // Arrange
        var definition = new JobDefinition { JobTypeId = "test-job" };

        // Act
        var before = DateTime.UtcNow;
        await _storage.UpsertDefinitionAsync(definition);
        var after = DateTime.UtcNow;

        // Assert
        var result = await _storage.GetJobDefinitionAsync("test-job");
        result!.UpdatedAt.Should().BeOnOrAfter(before);
        result.UpdatedAt.Should().BeOnOrBefore(after);
    }

    #endregion

    #region GetDueJobsAsync Tests

    [Fact]
    public async Task GetDueJobsAsync_ReturnsDueJobs()
    {
        // Arrange
        var dueJob = new JobDefinition
        {
            JobTypeId = "due-job",
            IsEnabled = true,
            NextRunAt = DateTime.UtcNow.AddMinutes(-5)
        };
        var futureJob = new JobDefinition
        {
            JobTypeId = "future-job",
            IsEnabled = true,
            NextRunAt = DateTime.UtcNow.AddHours(1)
        };
        var disabledJob = new JobDefinition
        {
            JobTypeId = "disabled-job",
            IsEnabled = false,
            NextRunAt = DateTime.UtcNow.AddMinutes(-5)
        };

        await _storage.UpsertDefinitionAsync(dueJob);
        await _storage.UpsertDefinitionAsync(futureJob);
        await _storage.UpsertDefinitionAsync(disabledJob);

        // Act
        var result = await _storage.GetDueJobsAsync(DateTime.UtcNow);

        // Assert
        result.Should().HaveCount(1);
        result.Should().Contain(d => d.JobTypeId == "due-job");
    }

    [Fact]
    public async Task GetDueJobsAsync_IgnoresJobsWithoutNextRun()
    {
        // Arrange
        var noScheduleJob = new JobDefinition
        {
            JobTypeId = "no-schedule",
            IsEnabled = true,
            NextRunAt = null
        };
        await _storage.UpsertDefinitionAsync(noScheduleJob);

        // Act
        var result = await _storage.GetDueJobsAsync(DateTime.UtcNow);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region CleanupOldRunsAsync Tests

    [Fact]
    public async Task CleanupOldRunsAsync_RemovesOldRuns()
    {
        // Arrange
        var oldRun = new JobRun
        {
            JobTypeId = "test",
            Status = JobRunStatus.Completed,
            CompletedAt = DateTime.UtcNow.AddDays(-10)
        };
        var recentRun = new JobRun
        {
            JobTypeId = "test",
            Status = JobRunStatus.Completed,
            CompletedAt = DateTime.UtcNow.AddHours(-1)
        };
        var runningRun = new JobRun
        {
            JobTypeId = "test",
            Status = JobRunStatus.Running
        };

        await _storage.EnqueueAsync(oldRun);
        await _storage.EnqueueAsync(recentRun);
        await _storage.EnqueueAsync(runningRun);

        // Act
        var count = await _storage.CleanupOldRunsAsync(TimeSpan.FromDays(7));

        // Assert
        count.Should().Be(1);

        var oldResult = await _storage.GetRunAsync(oldRun.Id);
        oldResult.Should().BeNull();

        var recentResult = await _storage.GetRunAsync(recentRun.Id);
        recentResult.Should().NotBeNull();

        var runningResult = await _storage.GetRunAsync(runningRun.Id);
        runningResult.Should().NotBeNull();
    }

    [Fact]
    public async Task CleanupOldRunsAsync_AlsoRemovesLogs()
    {
        // Arrange
        var oldRun = new JobRun
        {
            JobTypeId = "test",
            Status = JobRunStatus.Completed,
            CompletedAt = DateTime.UtcNow.AddDays(-10)
        };
        await _storage.EnqueueAsync(oldRun);

        var log = new JobLog { RunId = oldRun.Id, Message = "Test log" };
        await _storage.AddLogAsync(log);

        // Act
        await _storage.CleanupOldRunsAsync(TimeSpan.FromDays(7));

        // Assert
        var logs = await _storage.GetLogsAsync(oldRun.Id);
        logs.Should().BeEmpty();
    }

    #endregion

    #region Log Tests

    [Fact]
    public async Task AddLogAsync_StoresLog()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var log = new JobLog
        {
            RunId = runId,
            Level = JobLogLevel.Info,
            Message = "Test message"
        };

        // Act
        await _storage.AddLogAsync(log);

        // Assert
        var logs = await _storage.GetLogsAsync(runId);
        logs.Should().HaveCount(1);
        logs[0].Message.Should().Be("Test message");
    }

    [Fact]
    public async Task AddLogsAsync_StoresMultipleLogs()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var logs = new[]
        {
            new JobLog { RunId = runId, Message = "Message 1" },
            new JobLog { RunId = runId, Message = "Message 2" },
            new JobLog { RunId = runId, Message = "Message 3" }
        };

        // Act
        await _storage.AddLogsAsync(logs);

        // Assert
        var result = await _storage.GetLogsAsync(runId);
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetLogsAsync_OrdersByTimestampDescending()
    {
        // Arrange
        var runId = Guid.NewGuid();
        await _storage.AddLogAsync(new JobLog { RunId = runId, Message = "First", Timestamp = DateTime.UtcNow.AddMinutes(-2) });
        await _storage.AddLogAsync(new JobLog { RunId = runId, Message = "Second", Timestamp = DateTime.UtcNow.AddMinutes(-1) });
        await _storage.AddLogAsync(new JobLog { RunId = runId, Message = "Third", Timestamp = DateTime.UtcNow });

        // Act
        var logs = await _storage.GetLogsAsync(runId);

        // Assert
        logs[0].Message.Should().Be("Third");
        logs[1].Message.Should().Be("Second");
        logs[2].Message.Should().Be("First");
    }

    #endregion

    #region Heartbeat Tests

    [Fact]
    public async Task SendHeartbeatAsync_StoresHeartbeat()
    {
        // Arrange
        var heartbeat = new JobHeartbeat
        {
            WorkerId = "worker-1",
            Hostname = "localhost"
        };

        // Act
        await _storage.SendHeartbeatAsync(heartbeat);

        // Assert
        var heartbeats = await _storage.GetHeartbeatsAsync();
        heartbeats.Should().HaveCount(1);
        heartbeats[0].WorkerId.Should().Be("worker-1");
    }

    [Fact]
    public async Task GetStaleHeartbeatsAsync_ReturnsStaleOnly()
    {
        // Arrange
        var fresh = new JobHeartbeat { WorkerId = "fresh", Timestamp = DateTime.UtcNow };
        var stale = new JobHeartbeat { WorkerId = "stale", Timestamp = DateTime.UtcNow.AddMinutes(-10) };

        await _storage.SendHeartbeatAsync(fresh);
        await _storage.SendHeartbeatAsync(stale);

        // Act
        var result = await _storage.GetStaleHeartbeatsAsync(TimeSpan.FromMinutes(5));

        // Assert
        result.Should().HaveCount(1);
        result[0].WorkerId.Should().Be("stale");
    }

    [Fact]
    public async Task CleanupStaleHeartbeatsAsync_RemovesStale()
    {
        // Arrange
        var fresh = new JobHeartbeat { WorkerId = "fresh", Timestamp = DateTime.UtcNow };
        var stale = new JobHeartbeat { WorkerId = "stale", Timestamp = DateTime.UtcNow.AddMinutes(-10) };

        await _storage.SendHeartbeatAsync(fresh);
        await _storage.SendHeartbeatAsync(stale);

        // Act
        await _storage.CleanupStaleHeartbeatsAsync(TimeSpan.FromMinutes(5));

        // Assert
        var heartbeats = await _storage.GetHeartbeatsAsync();
        heartbeats.Should().HaveCount(1);
        heartbeats[0].WorkerId.Should().Be("fresh");
    }

    #endregion

    #region Stats Tests

    [Fact]
    public async Task GetStatsAsync_ReturnsCorrectCounts()
    {
        // Arrange
        await _storage.EnqueueAsync(new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "default" });
        await _storage.EnqueueAsync(new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "default" });
        await _storage.EnqueueAsync(new JobRun { JobTypeId = "test", Status = JobRunStatus.Running, Queue = "default" });
        await _storage.EnqueueAsync(new JobRun { JobTypeId = "test", Status = JobRunStatus.Completed, Queue = "default" });

        await _storage.UpsertDefinitionAsync(new JobDefinition { JobTypeId = "job1" });
        await _storage.UpsertDefinitionAsync(new JobDefinition { JobTypeId = "job2" });

        await _storage.SendHeartbeatAsync(new JobHeartbeat { WorkerId = "worker-1" });

        // Act
        var stats = await _storage.GetStatsAsync();

        // Assert
        stats.TotalJobs.Should().Be(2);
        stats.TotalRuns.Should().Be(4);
        stats.PendingRuns.Should().Be(2);
        stats.RunningRuns.Should().Be(1);
        stats.ActiveWorkers.Should().Be(1);
    }

    #endregion

    #region Clear Tests

    [Fact]
    public async Task Clear_RemovesAllData()
    {
        // Arrange
        await _storage.EnqueueAsync(new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "default" });
        await _storage.UpsertDefinitionAsync(new JobDefinition { JobTypeId = "test" });
        await _storage.AddLogAsync(new JobLog { RunId = Guid.NewGuid(), Message = "test" });
        await _storage.SendHeartbeatAsync(new JobHeartbeat { WorkerId = "worker-1" });

        // Act
        _storage.Clear();

        // Assert
        var stats = await _storage.GetStatsAsync();
        stats.TotalJobs.Should().Be(0);
        stats.TotalRuns.Should().Be(0);
        stats.ActiveWorkers.Should().Be(0);
    }

    #endregion

    #region Additional Tests

    [Fact]
    public async Task GetRunsByStatusAsync_ReturnsFilteredRuns()
    {
        // Arrange
        await _storage.EnqueueAsync(new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "default" });
        await _storage.EnqueueAsync(new JobRun { JobTypeId = "test", Status = JobRunStatus.Completed, Queue = "default" });
        await _storage.EnqueueAsync(new JobRun { JobTypeId = "test", Status = JobRunStatus.Completed, Queue = "default" });

        // Act
        var result = await _storage.GetRunsByStatusAsync(JobRunStatus.Completed);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(r => r.Status.Should().Be(JobRunStatus.Completed));
    }

    [Fact]
    public async Task GetRunsByJobTypeAsync_ReturnsFilteredRuns()
    {
        // Arrange
        await _storage.EnqueueAsync(new JobRun { JobTypeId = "job-a", Status = JobRunStatus.Pending, Queue = "default" });
        await _storage.EnqueueAsync(new JobRun { JobTypeId = "job-a", Status = JobRunStatus.Pending, Queue = "default" });
        await _storage.EnqueueAsync(new JobRun { JobTypeId = "job-b", Status = JobRunStatus.Pending, Queue = "default" });

        // Act
        var result = await _storage.GetRunsByJobTypeAsync("job-a");

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(r => r.JobTypeId.Should().Be("job-a"));
    }

    [Fact]
    public async Task GetRunsForRetryAsync_ReturnsAwaitingRetry()
    {
        // Arrange
        await _storage.EnqueueAsync(new JobRun { JobTypeId = "test", Status = JobRunStatus.Pending, Queue = "default" });
        await _storage.EnqueueAsync(new JobRun { JobTypeId = "test", Status = JobRunStatus.AwaitingRetry, Queue = "default", NextRetryAt = DateTime.UtcNow });

        // Act
        var result = await _storage.GetRunsForRetryAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].Status.Should().Be(JobRunStatus.AwaitingRetry);
    }

    [Fact]
    public async Task UpdateNextRunAsync_UpdatesDefinition()
    {
        // Arrange
        var definition = new JobDefinition
        {
            JobTypeId = "scheduled-job",
            NextRunAt = DateTime.UtcNow
        };
        await _storage.UpsertDefinitionAsync(definition);

        var newNextRun = DateTime.UtcNow.AddHours(1);
        var lastRun = DateTime.UtcNow;

        // Act
        await _storage.UpdateNextRunAsync("scheduled-job", newNextRun, lastRun, JobRunStatus.Completed);

        // Assert
        var result = await _storage.GetJobDefinitionAsync("scheduled-job");
        result!.NextRunAt.Should().BeCloseTo(newNextRun, TimeSpan.FromSeconds(1));
        result.LastRunAt.Should().BeCloseTo(lastRun, TimeSpan.FromSeconds(1));
        result.LastRunStatus.Should().Be(JobRunStatus.Completed);
    }

    [Fact]
    public async Task DeleteDefinitionAsync_RemovesDefinition()
    {
        // Arrange
        await _storage.UpsertDefinitionAsync(new JobDefinition { JobTypeId = "to-delete" });

        // Act
        await _storage.DeleteDefinitionAsync("to-delete");

        // Assert
        var result = await _storage.GetJobDefinitionAsync("to-delete");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllDefinitionsAsync_ReturnsAllDefinitions()
    {
        // Arrange
        await _storage.UpsertDefinitionAsync(new JobDefinition { JobTypeId = "job-1" });
        await _storage.UpsertDefinitionAsync(new JobDefinition { JobTypeId = "job-2" });
        await _storage.UpsertDefinitionAsync(new JobDefinition { JobTypeId = "job-3" });

        // Act
        var result = await _storage.GetAllDefinitionsAsync();

        // Assert
        result.Should().HaveCount(3);
    }

    #endregion
}
