using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using ZapJobs.Core;
using ZapJobs.DeadLetter;
using ZapJobs.Storage.InMemory;

namespace ZapJobs.Tests.DeadLetter;

public class DeadLetterManagerTests
{
    private readonly InMemoryJobStorage _storage;
    private readonly Mock<IJobScheduler> _schedulerMock;
    private readonly Mock<ILogger<DeadLetterManager>> _loggerMock;
    private readonly DeadLetterManager _manager;

    public DeadLetterManagerTests()
    {
        _storage = new InMemoryJobStorage();
        _schedulerMock = new Mock<IJobScheduler>();
        _loggerMock = new Mock<ILogger<DeadLetterManager>>();
        _manager = new DeadLetterManager(_storage, _schedulerMock.Object, _loggerMock.Object);
    }

    #region GetEntriesAsync Tests

    [Fact]
    public async Task GetEntriesAsync_ReturnsEntries()
    {
        // Arrange
        var run = new JobRun { JobTypeId = "test", ErrorMessage = "Error" };
        await _storage.EnqueueAsync(run);
        await _storage.MoveToDeadLetterAsync(run);

        // Act
        var result = await _manager.GetEntriesAsync();

        // Assert
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetEntriesAsync_FiltersByStatus()
    {
        // Arrange
        var run1 = new JobRun { JobTypeId = "test", ErrorMessage = "Error 1" };
        var run2 = new JobRun { JobTypeId = "test", ErrorMessage = "Error 2" };
        await _storage.EnqueueAsync(run1);
        await _storage.EnqueueAsync(run2);
        await _storage.MoveToDeadLetterAsync(run1);
        await _storage.MoveToDeadLetterAsync(run2);

        var entries = await _storage.GetDeadLetterEntriesAsync();
        entries[0].Status = DeadLetterStatus.Requeued;
        await _storage.UpdateDeadLetterEntryAsync(entries[0]);

        // Act
        var result = await _manager.GetEntriesAsync(status: DeadLetterStatus.Pending);

        // Assert
        result.Should().HaveCount(1);
    }

    #endregion

    #region GetEntryAsync Tests

    [Fact]
    public async Task GetEntryAsync_ExistingEntry_ReturnsEntry()
    {
        // Arrange
        var run = new JobRun { JobTypeId = "test", ErrorMessage = "Error" };
        await _storage.EnqueueAsync(run);
        await _storage.MoveToDeadLetterAsync(run);

        var entries = await _storage.GetDeadLetterEntriesAsync();
        var entryId = entries[0].Id;

        // Act
        var result = await _manager.GetEntryAsync(entryId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(entryId);
    }

    [Fact]
    public async Task GetEntryAsync_NonExistent_ReturnsNull()
    {
        // Act
        var result = await _manager.GetEntryAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetCountAsync Tests

    [Fact]
    public async Task GetCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        for (int i = 0; i < 3; i++)
        {
            var run = new JobRun { JobTypeId = "test", ErrorMessage = "Error" };
            await _storage.EnqueueAsync(run);
            await _storage.MoveToDeadLetterAsync(run);
        }

        // Act
        var count = await _manager.GetCountAsync();

        // Assert
        count.Should().Be(3);
    }

    #endregion

    #region RequeueAsync Tests

    [Fact]
    public async Task RequeueAsync_CreatesNewRunAndUpdatesEntry()
    {
        // Arrange
        var run = new JobRun
        {
            JobTypeId = "test-job",
            Queue = "default",
            InputJson = "{\"key\":\"value\"}",
            ErrorMessage = "Error"
        };
        await _storage.EnqueueAsync(run);
        await _storage.MoveToDeadLetterAsync(run);

        var entries = await _storage.GetDeadLetterEntriesAsync();
        var entryId = entries[0].Id;

        var newRunId = Guid.NewGuid();
        _schedulerMock
            .Setup(s => s.EnqueueAsync("test-job", It.IsAny<object?>(), "default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(newRunId);

        // Act
        var result = await _manager.RequeueAsync(entryId);

        // Assert
        result.Should().Be(newRunId);

        var entry = await _storage.GetDeadLetterEntryAsync(entryId);
        entry!.Status.Should().Be(DeadLetterStatus.Requeued);
        entry.RequeuedAt.Should().NotBeNull();
        entry.RequeuedRunId.Should().Be(newRunId);
    }

    [Fact]
    public async Task RequeueAsync_NonExistentEntry_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.RequeueAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RequeueAsync_NotPendingEntry_ThrowsException()
    {
        // Arrange
        var run = new JobRun { JobTypeId = "test", ErrorMessage = "Error" };
        await _storage.EnqueueAsync(run);
        await _storage.MoveToDeadLetterAsync(run);

        var entries = await _storage.GetDeadLetterEntriesAsync();
        entries[0].Status = DeadLetterStatus.Discarded;
        await _storage.UpdateDeadLetterEntryAsync(entries[0]);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.RequeueAsync(entries[0].Id));
    }

    [Fact]
    public async Task RequeueAsync_WithNewInput_UsesNewInput()
    {
        // Arrange
        var run = new JobRun
        {
            JobTypeId = "test-job",
            Queue = "default",
            InputJson = "{\"old\":\"value\"}",
            ErrorMessage = "Error"
        };
        await _storage.EnqueueAsync(run);
        await _storage.MoveToDeadLetterAsync(run);

        var entries = await _storage.GetDeadLetterEntriesAsync();
        var entryId = entries[0].Id;

        var newRunId = Guid.NewGuid();
        object? capturedInput = null;
        _schedulerMock
            .Setup(s => s.EnqueueAsync("test-job", It.IsAny<object?>(), "default", It.IsAny<CancellationToken>()))
            .Callback<string, object?, string?, CancellationToken>((_, input, _, _) => capturedInput = input)
            .ReturnsAsync(newRunId);

        // Act
        await _manager.RequeueAsync(entryId, newInput: "{\"new\":\"value\"}");

        // Assert
        _schedulerMock.Verify(
            s => s.EnqueueAsync("test-job", It.IsAny<object?>(), "default", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region DiscardAsync Tests

    [Fact]
    public async Task DiscardAsync_UpdatesStatusToDiscarded()
    {
        // Arrange
        var run = new JobRun { JobTypeId = "test", ErrorMessage = "Error" };
        await _storage.EnqueueAsync(run);
        await _storage.MoveToDeadLetterAsync(run);

        var entries = await _storage.GetDeadLetterEntriesAsync();
        var entryId = entries[0].Id;

        // Act
        await _manager.DiscardAsync(entryId, "Not needed");

        // Assert
        var entry = await _storage.GetDeadLetterEntryAsync(entryId);
        entry!.Status.Should().Be(DeadLetterStatus.Discarded);
        entry.Notes.Should().Be("Not needed");
    }

    [Fact]
    public async Task DiscardAsync_NonExistentEntry_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.DiscardAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DiscardAsync_NotPendingEntry_ThrowsException()
    {
        // Arrange
        var run = new JobRun { JobTypeId = "test", ErrorMessage = "Error" };
        await _storage.EnqueueAsync(run);
        await _storage.MoveToDeadLetterAsync(run);

        var entries = await _storage.GetDeadLetterEntriesAsync();
        entries[0].Status = DeadLetterStatus.Archived;
        await _storage.UpdateDeadLetterEntryAsync(entries[0]);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.DiscardAsync(entries[0].Id));
    }

    #endregion

    #region ArchiveAsync Tests

    [Fact]
    public async Task ArchiveAsync_UpdatesStatusToArchived()
    {
        // Arrange
        var run = new JobRun { JobTypeId = "test", ErrorMessage = "Error" };
        await _storage.EnqueueAsync(run);
        await _storage.MoveToDeadLetterAsync(run);

        var entries = await _storage.GetDeadLetterEntriesAsync();
        var entryId = entries[0].Id;

        // Act
        await _manager.ArchiveAsync(entryId, "For records");

        // Assert
        var entry = await _storage.GetDeadLetterEntryAsync(entryId);
        entry!.Status.Should().Be(DeadLetterStatus.Archived);
        entry.Notes.Should().Be("For records");
    }

    [Fact]
    public async Task ArchiveAsync_NonExistentEntry_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.ArchiveAsync(Guid.NewGuid()));
    }

    #endregion

    #region RequeueAllAsync Tests

    [Fact]
    public async Task RequeueAllAsync_RequeuesAllPendingForJobType()
    {
        // Arrange
        for (int i = 0; i < 3; i++)
        {
            var run = new JobRun { JobTypeId = "job-a", Queue = "default", ErrorMessage = "Error" };
            await _storage.EnqueueAsync(run);
            await _storage.MoveToDeadLetterAsync(run);
        }

        var otherRun = new JobRun { JobTypeId = "job-b", Queue = "default", ErrorMessage = "Error" };
        await _storage.EnqueueAsync(otherRun);
        await _storage.MoveToDeadLetterAsync(otherRun);

        _schedulerMock
            .Setup(s => s.EnqueueAsync("job-a", It.IsAny<object?>(), "default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var count = await _manager.RequeueAllAsync("job-a");

        // Assert
        count.Should().Be(3);
        _schedulerMock.Verify(
            s => s.EnqueueAsync("job-a", It.IsAny<object?>(), "default", It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    #endregion

    #region DiscardAllAsync Tests

    [Fact]
    public async Task DiscardAllAsync_DiscardsAllPendingForJobType()
    {
        // Arrange
        for (int i = 0; i < 3; i++)
        {
            var run = new JobRun { JobTypeId = "job-a", ErrorMessage = "Error" };
            await _storage.EnqueueAsync(run);
            await _storage.MoveToDeadLetterAsync(run);
        }

        var otherRun = new JobRun { JobTypeId = "job-b", ErrorMessage = "Error" };
        await _storage.EnqueueAsync(otherRun);
        await _storage.MoveToDeadLetterAsync(otherRun);

        // Act
        var count = await _manager.DiscardAllAsync("job-a", "Bulk discard");

        // Assert
        count.Should().Be(3);

        var jobAEntries = await _storage.GetDeadLetterEntriesAsync(jobTypeId: "job-a");
        jobAEntries.Should().AllSatisfy(e => e.Status.Should().Be(DeadLetterStatus.Discarded));

        var jobBEntries = await _storage.GetDeadLetterEntriesAsync(jobTypeId: "job-b");
        jobBEntries[0].Status.Should().Be(DeadLetterStatus.Pending);
    }

    #endregion
}
