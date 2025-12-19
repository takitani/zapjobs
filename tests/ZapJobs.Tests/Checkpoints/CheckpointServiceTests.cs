using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ZapJobs.Checkpoints;
using ZapJobs.Core;
using ZapJobs.Core.Checkpoints;
using ZapJobs.Storage.InMemory;

namespace ZapJobs.Tests.Checkpoints;

public class CheckpointServiceTests
{
    private readonly InMemoryJobStorage _storage;
    private readonly CheckpointOptions _options;
    private readonly Mock<ILogger<CheckpointService>> _loggerMock;
    private readonly CheckpointService _service;

    public CheckpointServiceTests()
    {
        _storage = new InMemoryJobStorage();
        _options = new CheckpointOptions();
        _loggerMock = new Mock<ILogger<CheckpointService>>();
        _service = new CheckpointService(
            _storage,
            Options.Create(_options),
            _loggerMock.Object);
    }

    #region SaveAsync Tests

    [Fact]
    public async Task SaveAsync_SimplObject_SavesSuccessfully()
    {
        // Arrange
        var runId = await CreateTestRun();
        var data = new TestCheckpointData { Progress = 50, LastProcessedId = "item-123" };

        // Act
        var result = await _service.SaveAsync(runId, "progress", data);

        // Assert
        result.Success.Should().BeTrue();
        result.CheckpointId.Should().NotBe(Guid.Empty);
        result.SequenceNumber.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SaveAsync_WithTtl_SetsExpiresAt()
    {
        // Arrange
        var runId = await CreateTestRun();
        var data = new TestCheckpointData { Progress = 50 };
        var options = new CheckpointSaveOptions { Ttl = TimeSpan.FromHours(1) };

        // Act
        var result = await _service.SaveAsync(runId, "progress", data, options);

        // Assert
        result.Success.Should().BeTrue();

        var checkpoint = await _service.GetCheckpointAsync(runId, "progress");
        checkpoint.Should().NotBeNull();
        checkpoint!.ExpiresAt.Should().NotBeNull();
        checkpoint.ExpiresAt!.Value.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddHours(1),
            TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SaveAsync_LargeData_Compresses()
    {
        // Arrange
        var runId = await CreateTestRun();
        // Create data larger than compression threshold (default 1KB)
        var data = new TestCheckpointData
        {
            Progress = 50,
            LastProcessedId = new string('x', 2000)
        };
        _options.CompressionThresholdBytes = 1024;

        // Act
        var result = await _service.SaveAsync(runId, "large-data", data);

        // Assert
        result.Success.Should().BeTrue();
        result.WasCompressed.Should().BeTrue();

        var checkpoint = await _service.GetCheckpointAsync(runId, "large-data");
        checkpoint!.IsCompressed.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_SmallData_DoesNotCompress()
    {
        // Arrange
        var runId = await CreateTestRun();
        var data = new TestCheckpointData { Progress = 50 };

        // Act
        var result = await _service.SaveAsync(runId, "small-data", data);

        // Assert
        result.Success.Should().BeTrue();
        result.WasCompressed.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_CompressionDisabled_DoesNotCompress()
    {
        // Arrange
        _options.EnableCompression = false;
        var runId = await CreateTestRun();
        var data = new TestCheckpointData { LastProcessedId = new string('x', 2000) };

        // Act
        var result = await _service.SaveAsync(runId, "data", data);

        // Assert
        result.Success.Should().BeTrue();
        result.WasCompressed.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_ExceedsMaxSize_Fails()
    {
        // Arrange
        _options.MaxDataSizeBytes = 100;
        var runId = await CreateTestRun();
        var data = new TestCheckpointData { LastProcessedId = new string('x', 200) };

        // Act
        var result = await _service.SaveAsync(runId, "data", data);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("exceeds maximum");
    }

    [Fact]
    public async Task SaveAsync_WithVersion_SetsVersion()
    {
        // Arrange
        var runId = await CreateTestRun();
        var data = new TestCheckpointData { Progress = 50 };
        var options = new CheckpointSaveOptions { Version = 2 };

        // Act
        var result = await _service.SaveAsync(runId, "versioned", data, options);

        // Assert
        result.Success.Should().BeTrue();

        var checkpoint = await _service.GetCheckpointAsync(runId, "versioned");
        checkpoint!.Version.Should().Be(2);
    }

    [Fact]
    public async Task SaveAsync_MultipleSaves_IncrementsSequence()
    {
        // Arrange
        var runId = await CreateTestRun();

        // Act
        var result1 = await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 10 });
        var result2 = await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 20 });
        var result3 = await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 30 });

        // Assert
        result1.SequenceNumber.Should().Be(1);
        result2.SequenceNumber.Should().Be(2);
        result3.SequenceNumber.Should().Be(3);
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_ExistingCheckpoint_ReturnsData()
    {
        // Arrange
        var runId = await CreateTestRun();
        var originalData = new TestCheckpointData { Progress = 75, LastProcessedId = "item-456" };
        await _service.SaveAsync(runId, "progress", originalData);

        // Act
        var result = await _service.GetAsync<TestCheckpointData>(runId, "progress");

        // Assert
        result.Should().NotBeNull();
        result!.Progress.Should().Be(75);
        result.LastProcessedId.Should().Be("item-456");
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        var runId = await CreateTestRun();

        // Act
        var result = await _service.GetAsync<TestCheckpointData>(runId, "non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_CompressedData_DecompressesAndReturns()
    {
        // Arrange
        _options.CompressionThresholdBytes = 100;
        var runId = await CreateTestRun();
        var originalData = new TestCheckpointData
        {
            Progress = 50,
            LastProcessedId = new string('x', 500)
        };
        await _service.SaveAsync(runId, "large", originalData);

        // Act
        var result = await _service.GetAsync<TestCheckpointData>(runId, "large");

        // Assert
        result.Should().NotBeNull();
        result!.Progress.Should().Be(50);
        result.LastProcessedId.Should().Be(new string('x', 500));
    }

    [Fact]
    public async Task GetAsync_MultipleKeys_ReturnsCorrectData()
    {
        // Arrange
        var runId = await CreateTestRun();
        var progressData = new TestCheckpointData { Progress = 50 };
        var cursorData = new CursorCheckpointData { Cursor = "page-5", Offset = 100 };

        await _service.SaveAsync(runId, "progress", progressData);
        await _service.SaveAsync(runId, "cursor", cursorData);

        // Act
        var progressResult = await _service.GetAsync<TestCheckpointData>(runId, "progress");
        var cursorResult = await _service.GetAsync<CursorCheckpointData>(runId, "cursor");

        // Assert
        progressResult!.Progress.Should().Be(50);
        cursorResult!.Cursor.Should().Be("page-5");
        cursorResult.Offset.Should().Be(100);
    }

    [Fact]
    public async Task GetAsync_LatestVersionReturned()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 10 });
        await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 50 });
        await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 100 });

        // Act
        var result = await _service.GetAsync<TestCheckpointData>(runId, "progress");

        // Assert
        result!.Progress.Should().Be(100);
    }

    #endregion

    #region GetAllAsync Tests

    [Fact]
    public async Task GetAllAsync_ReturnsAllCheckpointsForRun()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 50 });
        await _service.SaveAsync(runId, "cursor", new CursorCheckpointData { Cursor = "page-1" });
        await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 75 });

        // Act
        var result = await _service.GetAllAsync(runId);

        // Assert
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetAllAsync_EmptyRun_ReturnsEmptyList()
    {
        // Arrange
        var runId = await CreateTestRun();

        // Act
        var result = await _service.GetAllAsync(runId);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetHistoryAsync Tests

    [Fact]
    public async Task GetHistoryAsync_ReturnsCheckpointHistory()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 25 });
        await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 50 });
        await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 75 });
        await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 100 });

        // Act
        var result = await _service.GetHistoryAsync(runId, "progress", limit: 3);

        // Assert
        result.Should().HaveCount(3);
        result.Select(c => c.SequenceNumber).Should().BeInDescendingOrder();
    }

    #endregion

    #region ExistsAsync Tests

    [Fact]
    public async Task ExistsAsync_ExistingCheckpoint_ReturnsTrue()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 50 });

        // Act
        var result = await _service.ExistsAsync(runId, "progress");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistent_ReturnsFalse()
    {
        // Arrange
        var runId = await CreateTestRun();

        // Act
        var result = await _service.ExistsAsync(runId, "progress");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_ExistingCheckpoint_DeletesSuccessfully()
    {
        // Arrange
        var runId = await CreateTestRun();
        var saveResult = await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 50 });

        // Act
        var result = await _service.DeleteAsync(saveResult.CheckpointId!.Value);

        // Assert
        result.Should().BeTrue();
        var exists = await _service.ExistsAsync(runId, "progress");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_ReturnsFalse()
    {
        // Act
        var result = await _service.DeleteAsync(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region DeleteAllAsync Tests

    [Fact]
    public async Task DeleteAllAsync_DeletesAllForRun()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 50 });
        await _service.SaveAsync(runId, "cursor", new CursorCheckpointData { Cursor = "page-1" });
        await _service.SaveAsync(runId, "state", new TestCheckpointData { Progress = 25 });

        // Act
        var result = await _service.DeleteAllAsync(runId);

        // Assert
        result.Should().Be(3);
        var remaining = await _service.GetAllAsync(runId);
        remaining.Should().BeEmpty();
    }

    #endregion

    #region DeleteByKeyAsync Tests

    [Fact]
    public async Task DeleteByKeyAsync_DeletesAllForKey()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 25 });
        await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 50 });
        await _service.SaveAsync(runId, "progress", new TestCheckpointData { Progress = 75 });
        await _service.SaveAsync(runId, "cursor", new CursorCheckpointData { Cursor = "page-1" });

        // Act
        var result = await _service.DeleteByKeyAsync(runId, "progress");

        // Assert
        result.Should().Be(3);

        var progressExists = await _service.ExistsAsync(runId, "progress");
        progressExists.Should().BeFalse();

        var cursorExists = await _service.ExistsAsync(runId, "cursor");
        cursorExists.Should().BeTrue();
    }

    #endregion

    #region CleanupExpiredAsync Tests

    [Fact]
    public async Task CleanupExpiredAsync_DeletesExpiredCheckpoints()
    {
        // Arrange
        var runId = await CreateTestRun();

        // Create checkpoint with very short TTL
        await _service.SaveAsync(runId, "short-lived", new TestCheckpointData { Progress = 50 },
            new CheckpointSaveOptions { Ttl = TimeSpan.FromMilliseconds(1) });

        // Wait for expiration
        await Task.Delay(50);

        // Create a non-expiring checkpoint
        await _service.SaveAsync(runId, "permanent", new TestCheckpointData { Progress = 100 });

        // Act
        var deleted = await _service.CleanupExpiredAsync();

        // Assert
        deleted.Should().Be(1);

        var shortLivedExists = await _service.ExistsAsync(runId, "short-lived");
        shortLivedExists.Should().BeFalse();

        var permanentExists = await _service.ExistsAsync(runId, "permanent");
        permanentExists.Should().BeTrue();
    }

    #endregion

    #region EnforceLimitAsync Tests

    [Fact]
    public async Task EnforceLimitAsync_DeletesOldestWhenOverLimit()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _service.SaveAsync(runId, "p1", new TestCheckpointData { Progress = 10 });
        await _service.SaveAsync(runId, "p2", new TestCheckpointData { Progress = 20 });
        await _service.SaveAsync(runId, "p3", new TestCheckpointData { Progress = 30 });
        await _service.SaveAsync(runId, "p4", new TestCheckpointData { Progress = 40 });
        await _service.SaveAsync(runId, "p5", new TestCheckpointData { Progress = 50 });

        // Act
        var deleted = await _service.EnforceLimitAsync(runId, maxCheckpoints: 3);

        // Assert
        deleted.Should().Be(2);
        var remaining = await _service.GetAllAsync(runId);
        remaining.Should().HaveCount(3);
    }

    #endregion

    #region Helper Methods

    private async Task<Guid> CreateTestRun()
    {
        var run = new JobRun
        {
            JobTypeId = "test-job",
            Status = JobRunStatus.Running
        };
        return await _storage.EnqueueAsync(run);
    }

    #endregion

    #region Test Data Classes

    private class TestCheckpointData
    {
        public int Progress { get; set; }
        public string? LastProcessedId { get; set; }
    }

    private class CursorCheckpointData
    {
        public string? Cursor { get; set; }
        public int Offset { get; set; }
    }

    #endregion
}
