using Xunit;
using FluentAssertions;
using ZapJobs.Core;
using ZapJobs.Core.Checkpoints;
using ZapJobs.Storage.InMemory;

namespace ZapJobs.Storage.InMemory.Tests;

public class InMemoryCheckpointTests
{
    private readonly InMemoryJobStorage _storage;

    public InMemoryCheckpointTests()
    {
        _storage = new InMemoryJobStorage();
    }

    #region SaveCheckpointAsync Tests

    [Fact]
    public async Task SaveCheckpointAsync_SavesCheckpoint()
    {
        // Arrange
        var runId = await CreateTestRun();
        var checkpoint = new Checkpoint
        {
            RunId = runId,
            Key = "progress",
            DataJson = "{\"progress\":50}",
            SequenceNumber = 1,
            DataSizeBytes = 15
        };

        // Act
        await _storage.SaveCheckpointAsync(checkpoint);

        // Assert
        var result = await _storage.GetLatestCheckpointAsync(runId, "progress");
        result.Should().NotBeNull();
        result!.Key.Should().Be("progress");
        result.DataJson.Should().Be("{\"progress\":50}");
    }

    [Fact]
    public async Task SaveCheckpointAsync_SetsCreatedAt()
    {
        // Arrange
        var runId = await CreateTestRun();
        var checkpoint = new Checkpoint
        {
            RunId = runId,
            Key = "test",
            DataJson = "{}",
            SequenceNumber = 1,
            DataSizeBytes = 2
        };

        // Act
        var before = DateTimeOffset.UtcNow;
        await _storage.SaveCheckpointAsync(checkpoint);
        var after = DateTimeOffset.UtcNow;

        // Assert
        var result = await _storage.GetLatestCheckpointAsync(runId, "test");
        result!.CreatedAt.Should().BeCloseTo(before, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region GetLatestCheckpointAsync Tests

    [Fact]
    public async Task GetLatestCheckpointAsync_ReturnsLatestBySequence()
    {
        // Arrange
        var runId = await CreateTestRun();

        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "progress",
            DataJson = "{\"progress\":10}",
            SequenceNumber = 1,
            DataSizeBytes = 15
        });

        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "progress",
            DataJson = "{\"progress\":50}",
            SequenceNumber = 2,
            DataSizeBytes = 15
        });

        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "progress",
            DataJson = "{\"progress\":100}",
            SequenceNumber = 3,
            DataSizeBytes = 16
        });

        // Act
        var result = await _storage.GetLatestCheckpointAsync(runId, "progress");

        // Assert
        result.Should().NotBeNull();
        result!.SequenceNumber.Should().Be(3);
        result.DataJson.Should().Be("{\"progress\":100}");
    }

    [Fact]
    public async Task GetLatestCheckpointAsync_DifferentKeys_ReturnsCorrectOne()
    {
        // Arrange
        var runId = await CreateTestRun();

        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "progress",
            DataJson = "{\"progress\":50}",
            SequenceNumber = 1,
            DataSizeBytes = 15
        });

        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "cursor",
            DataJson = "{\"cursor\":\"page-5\"}",
            SequenceNumber = 2,
            DataSizeBytes = 18
        });

        // Act
        var progressResult = await _storage.GetLatestCheckpointAsync(runId, "progress");
        var cursorResult = await _storage.GetLatestCheckpointAsync(runId, "cursor");

        // Assert
        progressResult!.DataJson.Should().Be("{\"progress\":50}");
        cursorResult!.DataJson.Should().Be("{\"cursor\":\"page-5\"}");
    }

    [Fact]
    public async Task GetLatestCheckpointAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        var runId = await CreateTestRun();

        // Act
        var result = await _storage.GetLatestCheckpointAsync(runId, "non-existent");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetCheckpointsAsync Tests

    [Fact]
    public async Task GetCheckpointsAsync_ReturnsAllForRun()
    {
        // Arrange
        var runId = await CreateTestRun();
        var otherRunId = await CreateTestRun();

        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "progress",
            DataJson = "{}",
            SequenceNumber = 1,
            DataSizeBytes = 2
        });

        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "cursor",
            DataJson = "{}",
            SequenceNumber = 2,
            DataSizeBytes = 2
        });

        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = otherRunId,
            Key = "other",
            DataJson = "{}",
            SequenceNumber = 1,
            DataSizeBytes = 2
        });

        // Act
        var result = await _storage.GetCheckpointsAsync(runId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(c => c.RunId.Should().Be(runId));
    }

    [Fact]
    public async Task GetCheckpointsAsync_OrderedBySequenceDesc()
    {
        // Arrange
        var runId = await CreateTestRun();

        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "p1",
            DataJson = "{}",
            SequenceNumber = 1,
            DataSizeBytes = 2
        });

        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "p2",
            DataJson = "{}",
            SequenceNumber = 2,
            DataSizeBytes = 2
        });

        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "p3",
            DataJson = "{}",
            SequenceNumber = 3,
            DataSizeBytes = 2
        });

        // Act
        var result = await _storage.GetCheckpointsAsync(runId);

        // Assert
        result.Select(c => c.SequenceNumber).Should().BeInDescendingOrder();
    }

    #endregion

    #region GetCheckpointHistoryAsync Tests

    [Fact]
    public async Task GetCheckpointHistoryAsync_ReturnsHistoryForKey()
    {
        // Arrange
        var runId = await CreateTestRun();

        for (int i = 1; i <= 5; i++)
        {
            await _storage.SaveCheckpointAsync(new Checkpoint
            {
                RunId = runId,
                Key = "progress",
                DataJson = $"{{\"progress\":{i * 20}}}",
                SequenceNumber = i,
                DataSizeBytes = 15
            });
        }

        // Other key
        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "cursor",
            DataJson = "{}",
            SequenceNumber = 6,
            DataSizeBytes = 2
        });

        // Act
        var result = await _storage.GetCheckpointHistoryAsync(runId, "progress", limit: 3);

        // Assert
        result.Should().HaveCount(3);
        result.Should().AllSatisfy(c => c.Key.Should().Be("progress"));
        result.Select(c => c.SequenceNumber).Should().BeInDescendingOrder();
    }

    #endregion

    #region GetNextCheckpointSequenceAsync Tests

    [Fact]
    public async Task GetNextCheckpointSequenceAsync_ReturnsNextSequence()
    {
        // Arrange
        var runId = await CreateTestRun();

        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "p1",
            DataJson = "{}",
            SequenceNumber = 1,
            DataSizeBytes = 2
        });

        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "p2",
            DataJson = "{}",
            SequenceNumber = 2,
            DataSizeBytes = 2
        });

        // Act
        var result = await _storage.GetNextCheckpointSequenceAsync(runId);

        // Assert
        result.Should().Be(3);
    }

    [Fact]
    public async Task GetNextCheckpointSequenceAsync_NoCheckpoints_Returns1()
    {
        // Arrange
        var runId = await CreateTestRun();

        // Act
        var result = await _storage.GetNextCheckpointSequenceAsync(runId);

        // Assert
        result.Should().Be(1);
    }

    #endregion

    #region DeleteCheckpointAsync Tests

    [Fact]
    public async Task DeleteCheckpointAsync_ExistingCheckpoint_ReturnsTrue()
    {
        // Arrange
        var runId = await CreateTestRun();
        var checkpoint = new Checkpoint
        {
            RunId = runId,
            Key = "progress",
            DataJson = "{}",
            SequenceNumber = 1,
            DataSizeBytes = 2
        };
        await _storage.SaveCheckpointAsync(checkpoint);

        // Act
        var result = await _storage.DeleteCheckpointAsync(checkpoint.Id);

        // Assert
        result.Should().BeTrue();
        var remaining = await _storage.GetLatestCheckpointAsync(runId, "progress");
        remaining.Should().BeNull();
    }

    [Fact]
    public async Task DeleteCheckpointAsync_NonExistent_ReturnsFalse()
    {
        // Act
        var result = await _storage.DeleteCheckpointAsync(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region DeleteCheckpointsForRunAsync Tests

    [Fact]
    public async Task DeleteCheckpointsForRunAsync_DeletesAll()
    {
        // Arrange
        var runId = await CreateTestRun();
        var otherRunId = await CreateTestRun();

        await _storage.SaveCheckpointAsync(new Checkpoint { RunId = runId, Key = "p1", DataJson = "{}", SequenceNumber = 1, DataSizeBytes = 2 });
        await _storage.SaveCheckpointAsync(new Checkpoint { RunId = runId, Key = "p2", DataJson = "{}", SequenceNumber = 2, DataSizeBytes = 2 });
        await _storage.SaveCheckpointAsync(new Checkpoint { RunId = otherRunId, Key = "p1", DataJson = "{}", SequenceNumber = 1, DataSizeBytes = 2 });

        // Act
        var result = await _storage.DeleteCheckpointsForRunAsync(runId);

        // Assert
        result.Should().Be(2);

        var remaining = await _storage.GetCheckpointsAsync(runId);
        remaining.Should().BeEmpty();

        var otherRemaining = await _storage.GetCheckpointsAsync(otherRunId);
        otherRemaining.Should().HaveCount(1);
    }

    #endregion

    #region DeleteCheckpointsByKeyAsync Tests

    [Fact]
    public async Task DeleteCheckpointsByKeyAsync_DeletesAllForKey()
    {
        // Arrange
        var runId = await CreateTestRun();

        await _storage.SaveCheckpointAsync(new Checkpoint { RunId = runId, Key = "progress", DataJson = "{}", SequenceNumber = 1, DataSizeBytes = 2 });
        await _storage.SaveCheckpointAsync(new Checkpoint { RunId = runId, Key = "progress", DataJson = "{}", SequenceNumber = 2, DataSizeBytes = 2 });
        await _storage.SaveCheckpointAsync(new Checkpoint { RunId = runId, Key = "cursor", DataJson = "{}", SequenceNumber = 3, DataSizeBytes = 2 });

        // Act
        var result = await _storage.DeleteCheckpointsByKeyAsync(runId, "progress");

        // Assert
        result.Should().Be(2);

        var progressRemaining = await _storage.GetLatestCheckpointAsync(runId, "progress");
        progressRemaining.Should().BeNull();

        var cursorRemaining = await _storage.GetLatestCheckpointAsync(runId, "cursor");
        cursorRemaining.Should().NotBeNull();
    }

    #endregion

    #region DeleteExpiredCheckpointsAsync Tests

    [Fact]
    public async Task DeleteExpiredCheckpointsAsync_DeletesExpired()
    {
        // Arrange
        var runId = await CreateTestRun();

        // Expired checkpoint
        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "expired",
            DataJson = "{}",
            SequenceNumber = 1,
            DataSizeBytes = 2,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        });

        // Not expired checkpoint
        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "valid",
            DataJson = "{}",
            SequenceNumber = 2,
            DataSizeBytes = 2,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });

        // No expiration
        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "permanent",
            DataJson = "{}",
            SequenceNumber = 3,
            DataSizeBytes = 2,
            ExpiresAt = null
        });

        // Act
        var result = await _storage.DeleteExpiredCheckpointsAsync();

        // Assert
        result.Should().Be(1);

        var expiredExists = await _storage.GetLatestCheckpointAsync(runId, "expired");
        expiredExists.Should().BeNull();

        var validExists = await _storage.GetLatestCheckpointAsync(runId, "valid");
        validExists.Should().NotBeNull();

        var permanentExists = await _storage.GetLatestCheckpointAsync(runId, "permanent");
        permanentExists.Should().NotBeNull();
    }

    #endregion

    #region DeleteCheckpointsForCompletedJobsAsync Tests

    [Fact]
    public async Task DeleteCheckpointsForCompletedJobsAsync_DeletesOldCompleted()
    {
        // Arrange
        // Create a completed job (old)
        var completedRun = new JobRun
        {
            JobTypeId = "test",
            Status = JobRunStatus.Completed,
            CompletedAt = DateTime.UtcNow.AddHours(-2)
        };
        var completedRunId = await _storage.EnqueueAsync(completedRun);
        completedRun.Status = JobRunStatus.Completed;
        completedRun.CompletedAt = DateTime.UtcNow.AddHours(-2);
        await _storage.UpdateRunAsync(completedRun);

        // Create a running job
        var runningRun = new JobRun
        {
            JobTypeId = "test",
            Status = JobRunStatus.Running
        };
        var runningRunId = await _storage.EnqueueAsync(runningRun);
        runningRun.Status = JobRunStatus.Running;
        await _storage.UpdateRunAsync(runningRun);

        // Add checkpoints to both
        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = completedRunId,
            Key = "progress",
            DataJson = "{}",
            SequenceNumber = 1,
            DataSizeBytes = 2
        });

        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runningRunId,
            Key = "progress",
            DataJson = "{}",
            SequenceNumber = 1,
            DataSizeBytes = 2
        });

        // Act
        var result = await _storage.DeleteCheckpointsForCompletedJobsAsync(TimeSpan.FromHours(1));

        // Assert
        result.Should().Be(1);

        var completedCheckpoints = await _storage.GetCheckpointsAsync(completedRunId);
        completedCheckpoints.Should().BeEmpty();

        var runningCheckpoints = await _storage.GetCheckpointsAsync(runningRunId);
        runningCheckpoints.Should().HaveCount(1);
    }

    #endregion

    #region Clear Tests

    [Fact]
    public async Task Clear_ClearsCheckpoints()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _storage.SaveCheckpointAsync(new Checkpoint
        {
            RunId = runId,
            Key = "progress",
            DataJson = "{}",
            SequenceNumber = 1,
            DataSizeBytes = 2
        });

        // Act
        _storage.Clear();

        // Assert
        var checkpoints = await _storage.GetCheckpointsAsync(runId);
        checkpoints.Should().BeEmpty();
    }

    #endregion

    #region Helper Methods

    private async Task<Guid> CreateTestRun()
    {
        var run = new JobRun
        {
            JobTypeId = "test-job",
            Status = JobRunStatus.Pending
        };
        return await _storage.EnqueueAsync(run);
    }

    #endregion
}
