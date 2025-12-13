using Xunit;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ZapJobs.Core;
using ZapJobs.Scheduling;

namespace ZapJobs.Tests.Scheduling;

public class JobSchedulerServiceTests
{
    private readonly Mock<IJobStorage> _storage;
    private readonly Mock<ICronScheduler> _cronScheduler;
    private readonly Mock<ILogger<JobSchedulerService>> _logger;
    private readonly ZapJobsOptions _options;
    private readonly JobSchedulerService _scheduler;

    public JobSchedulerServiceTests()
    {
        _storage = new Mock<IJobStorage>();
        _cronScheduler = new Mock<ICronScheduler>();
        _logger = new Mock<ILogger<JobSchedulerService>>();
        _options = new ZapJobsOptions();

        _scheduler = new JobSchedulerService(
            _storage.Object,
            _cronScheduler.Object,
            Options.Create(_options),
            _logger.Object);
    }

    [Fact]
    public async Task EnqueueAsync_CreatesRunWithPendingStatus()
    {
        // Arrange
        var runId = Guid.NewGuid();
        _storage.Setup(s => s.EnqueueAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(runId);

        // Act
        var result = await _scheduler.EnqueueAsync("test-job");

        // Assert
        result.Should().Be(runId);
        _storage.Verify(s => s.EnqueueAsync(
            It.Is<JobRun>(r =>
                r.JobTypeId == "test-job" &&
                r.Status == JobRunStatus.Pending &&
                r.TriggerType == JobTriggerType.Api),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnqueueAsync_WithInput_SerializesInput()
    {
        // Arrange
        var runId = Guid.NewGuid();
        JobRun? capturedRun = null;
        _storage.Setup(s => s.EnqueueAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Callback<JobRun, CancellationToken>((run, _) => capturedRun = run)
            .ReturnsAsync(runId);

        var input = new { Email = "test@example.com", Subject = "Hello" };

        // Act
        await _scheduler.EnqueueAsync("send-email", input);

        // Assert
        capturedRun.Should().NotBeNull();
        capturedRun!.InputJson.Should().NotBeNullOrEmpty();

        var deserialized = JsonSerializer.Deserialize<JsonElement>(capturedRun.InputJson!);
        deserialized.GetProperty("Email").GetString().Should().Be("test@example.com");
        deserialized.GetProperty("Subject").GetString().Should().Be("Hello");
    }

    [Fact]
    public async Task EnqueueAsync_WithQueue_UsesSpecifiedQueue()
    {
        // Arrange
        var runId = Guid.NewGuid();
        _storage.Setup(s => s.EnqueueAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(runId);

        // Act
        await _scheduler.EnqueueAsync("test-job", queue: "critical");

        // Assert
        _storage.Verify(s => s.EnqueueAsync(
            It.Is<JobRun>(r => r.Queue == "critical"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnqueueAsync_WithoutQueue_UsesDefaultQueue()
    {
        // Arrange
        var runId = Guid.NewGuid();
        _storage.Setup(s => s.EnqueueAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(runId);

        // Act
        await _scheduler.EnqueueAsync("test-job");

        // Assert
        _storage.Verify(s => s.EnqueueAsync(
            It.Is<JobRun>(r => r.Queue == "default"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScheduleAsync_WithDelay_SetsScheduledAt()
    {
        // Arrange
        var runId = Guid.NewGuid();
        JobRun? capturedRun = null;
        _storage.Setup(s => s.EnqueueAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Callback<JobRun, CancellationToken>((run, _) => capturedRun = run)
            .ReturnsAsync(runId);

        var delay = TimeSpan.FromMinutes(30);

        // Act
        var beforeSchedule = DateTimeOffset.UtcNow;
        await _scheduler.ScheduleAsync("test-job", delay);
        var afterSchedule = DateTimeOffset.UtcNow;

        // Assert
        capturedRun.Should().NotBeNull();
        capturedRun!.Status.Should().Be(JobRunStatus.Scheduled);
        capturedRun.TriggerType.Should().Be(JobTriggerType.Scheduled);
        capturedRun.ScheduledAt.Should().NotBeNull();
        capturedRun.ScheduledAt!.Value.Should().BeCloseTo(
            beforeSchedule.Add(delay).UtcDateTime,
            precision: TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ScheduleAsync_WithDateTimeOffset_SetsExactTime()
    {
        // Arrange
        var runId = Guid.NewGuid();
        JobRun? capturedRun = null;
        _storage.Setup(s => s.EnqueueAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Callback<JobRun, CancellationToken>((run, _) => capturedRun = run)
            .ReturnsAsync(runId);

        var scheduledTime = DateTimeOffset.UtcNow.AddHours(2);

        // Act
        await _scheduler.ScheduleAsync("test-job", scheduledTime);

        // Assert
        capturedRun.Should().NotBeNull();
        capturedRun!.ScheduledAt.Should().BeCloseTo(
            scheduledTime.UtcDateTime,
            precision: TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RecurringAsync_WithInterval_SetsNextRun()
    {
        // Arrange
        _storage.Setup(s => s.GetJobDefinitionAsync("test-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.UpsertDefinitionAsync(It.IsAny<JobDefinition>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var interval = TimeSpan.FromHours(1);

        // Act
        var beforeSchedule = DateTime.UtcNow;
        var result = await _scheduler.RecurringAsync("test-job", interval);
        var afterSchedule = DateTime.UtcNow;

        // Assert
        result.Should().Be("test-job");
        _storage.Verify(s => s.UpsertDefinitionAsync(
            It.Is<JobDefinition>(d =>
                d.JobTypeId == "test-job" &&
                d.ScheduleType == ScheduleType.Interval &&
                d.IntervalMinutes == 60 &&
                d.IsEnabled == true &&
                d.NextRunAt.HasValue &&
                d.NextRunAt.Value >= beforeSchedule.Add(interval) &&
                d.NextRunAt.Value <= afterSchedule.Add(interval)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecurringAsync_WithCron_ValidatesExpression()
    {
        // Arrange
        var cronExpression = "0 8 * * *";
        _cronScheduler.Setup(c => c.IsValidExpression(cronExpression)).Returns(true);
        _cronScheduler.Setup(c => c.GetNextOccurrence(cronExpression, It.IsAny<DateTime>(), It.IsAny<TimeZoneInfo?>()))
            .Returns(DateTime.UtcNow.AddDays(1));
        _storage.Setup(s => s.GetJobDefinitionAsync("test-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.UpsertDefinitionAsync(It.IsAny<JobDefinition>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _scheduler.RecurringAsync("test-job", cronExpression);

        // Assert
        result.Should().Be("test-job");
        _cronScheduler.Verify(c => c.IsValidExpression(cronExpression), Times.Once);
    }

    [Fact]
    public async Task RecurringAsync_WithInvalidCron_ThrowsArgumentException()
    {
        // Arrange
        var invalidCron = "invalid cron";
        _cronScheduler.Setup(c => c.IsValidExpression(invalidCron)).Returns(false);

        // Act
        var act = () => _scheduler.RecurringAsync("test-job", invalidCron);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid CRON expression*");
    }

    [Fact]
    public async Task RecurringAsync_WithCron_SetsCorrectNextRun()
    {
        // Arrange
        var cronExpression = "0 9 * * *";
        var nextRun = DateTime.UtcNow.AddDays(1).Date.AddHours(9);
        _cronScheduler.Setup(c => c.IsValidExpression(cronExpression)).Returns(true);
        _cronScheduler.Setup(c => c.GetNextOccurrence(cronExpression, It.IsAny<DateTime>(), It.IsAny<TimeZoneInfo?>()))
            .Returns(nextRun);
        _storage.Setup(s => s.GetJobDefinitionAsync("test-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);

        JobDefinition? capturedDef = null;
        _storage.Setup(s => s.UpsertDefinitionAsync(It.IsAny<JobDefinition>(), It.IsAny<CancellationToken>()))
            .Callback<JobDefinition, CancellationToken>((def, _) => capturedDef = def)
            .Returns(Task.CompletedTask);

        // Act
        await _scheduler.RecurringAsync("test-job", cronExpression);

        // Assert
        capturedDef.Should().NotBeNull();
        capturedDef!.ScheduleType.Should().Be(ScheduleType.Cron);
        capturedDef.CronExpression.Should().Be(cronExpression);
        capturedDef.NextRunAt.Should().Be(nextRun);
    }

    [Fact]
    public async Task CancelAsync_PendingJob_SetsCancelledStatus()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run = new JobRun
        {
            Id = runId,
            Status = JobRunStatus.Pending
        };
        _storage.Setup(s => s.GetRunAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);
        _storage.Setup(s => s.UpdateRunAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _scheduler.CancelAsync(runId);

        // Assert
        result.Should().BeTrue();
        _storage.Verify(s => s.UpdateRunAsync(
            It.Is<JobRun>(r => r.Status == JobRunStatus.Cancelled && r.CompletedAt.HasValue),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_CompletedJob_ReturnsFalse()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run = new JobRun
        {
            Id = runId,
            Status = JobRunStatus.Completed
        };
        _storage.Setup(s => s.GetRunAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        // Act
        var result = await _scheduler.CancelAsync(runId);

        // Assert
        result.Should().BeFalse();
        _storage.Verify(s => s.UpdateRunAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelAsync_FailedJob_ReturnsFalse()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run = new JobRun
        {
            Id = runId,
            Status = JobRunStatus.Failed
        };
        _storage.Setup(s => s.GetRunAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        // Act
        var result = await _scheduler.CancelAsync(runId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CancelAsync_NonExistentJob_ReturnsFalse()
    {
        // Arrange
        var runId = Guid.NewGuid();
        _storage.Setup(s => s.GetRunAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobRun?)null);

        // Act
        var result = await _scheduler.CancelAsync(runId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TriggerAsync_CreatesManualRun()
    {
        // Arrange
        var runId = Guid.NewGuid();
        _storage.Setup(s => s.GetJobDefinitionAsync("test-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.EnqueueAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(runId);

        // Act
        var result = await _scheduler.TriggerAsync("test-job");

        // Assert
        result.Should().Be(runId);
        _storage.Verify(s => s.EnqueueAsync(
            It.Is<JobRun>(r =>
                r.JobTypeId == "test-job" &&
                r.Status == JobRunStatus.Pending &&
                r.TriggerType == JobTriggerType.Manual &&
                r.TriggeredBy == "manual"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TriggerAsync_WithInput_UsesProvidedInput()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var input = new { Data = "test" };
        JobRun? capturedRun = null;

        _storage.Setup(s => s.GetJobDefinitionAsync("test-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.EnqueueAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Callback<JobRun, CancellationToken>((run, _) => capturedRun = run)
            .ReturnsAsync(runId);

        // Act
        await _scheduler.TriggerAsync("test-job", input);

        // Assert
        capturedRun.Should().NotBeNull();
        capturedRun!.InputJson.Should().Contain("test");
    }

    [Fact]
    public async Task TriggerAsync_WithoutInput_UsesDefinitionConfigJson()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var definition = new JobDefinition
        {
            JobTypeId = "test-job",
            ConfigJson = "{\"Default\":\"config\"}"
        };
        JobRun? capturedRun = null;

        _storage.Setup(s => s.GetJobDefinitionAsync("test-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);
        _storage.Setup(s => s.EnqueueAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Callback<JobRun, CancellationToken>((run, _) => capturedRun = run)
            .ReturnsAsync(runId);

        // Act
        await _scheduler.TriggerAsync("test-job");

        // Assert
        capturedRun.Should().NotBeNull();
        capturedRun!.InputJson.Should().Be("{\"Default\":\"config\"}");
    }

    [Fact]
    public async Task RemoveRecurringAsync_ExistingJob_DisablesSchedule()
    {
        // Arrange
        var definition = new JobDefinition
        {
            JobTypeId = "test-job",
            ScheduleType = ScheduleType.Cron,
            CronExpression = "0 * * * *",
            IsEnabled = true,
            NextRunAt = DateTime.UtcNow.AddHours(1)
        };
        _storage.Setup(s => s.GetJobDefinitionAsync("test-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);
        _storage.Setup(s => s.UpsertDefinitionAsync(It.IsAny<JobDefinition>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _scheduler.RemoveRecurringAsync("test-job");

        // Assert
        result.Should().BeTrue();
        _storage.Verify(s => s.UpsertDefinitionAsync(
            It.Is<JobDefinition>(d =>
                d.IsEnabled == false &&
                d.ScheduleType == ScheduleType.Manual &&
                d.NextRunAt == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveRecurringAsync_NonExistentJob_ReturnsFalse()
    {
        // Arrange
        _storage.Setup(s => s.GetJobDefinitionAsync("test-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);

        // Act
        var result = await _scheduler.RemoveRecurringAsync("test-job");

        // Assert
        result.Should().BeFalse();
    }

    // Continuation tests

    [Fact]
    public async Task ContinueWithAsync_CreatesContinuationWithCorrectProperties()
    {
        // Arrange
        var parentRunId = Guid.NewGuid();
        JobContinuation? capturedContinuation = null;

        _storage.Setup(s => s.AddContinuationAsync(It.IsAny<JobContinuation>(), It.IsAny<CancellationToken>()))
            .Callback<JobContinuation, CancellationToken>((c, _) => capturedContinuation = c)
            .Returns(Task.CompletedTask);

        // Act
        var continuationId = await _scheduler.ContinueWithAsync(
            parentRunId,
            "continuation-job",
            condition: ContinuationCondition.OnSuccess);

        // Assert
        capturedContinuation.Should().NotBeNull();
        capturedContinuation!.ParentRunId.Should().Be(parentRunId);
        capturedContinuation.ContinuationJobTypeId.Should().Be("continuation-job");
        capturedContinuation.Condition.Should().Be(ContinuationCondition.OnSuccess);
        capturedContinuation.Status.Should().Be(ContinuationStatus.Pending);
        capturedContinuation.PassParentOutput.Should().BeFalse();
    }

    [Fact]
    public async Task ContinueWithAsync_WithInput_SerializesInput()
    {
        // Arrange
        var parentRunId = Guid.NewGuid();
        JobContinuation? capturedContinuation = null;

        _storage.Setup(s => s.AddContinuationAsync(It.IsAny<JobContinuation>(), It.IsAny<CancellationToken>()))
            .Callback<JobContinuation, CancellationToken>((c, _) => capturedContinuation = c)
            .Returns(Task.CompletedTask);

        var input = new { Data = "test-data", Count = 42 };

        // Act
        await _scheduler.ContinueWithAsync(parentRunId, "continuation-job", input);

        // Assert
        capturedContinuation.Should().NotBeNull();
        capturedContinuation!.InputJson.Should().NotBeNullOrEmpty();

        var deserialized = JsonSerializer.Deserialize<JsonElement>(capturedContinuation.InputJson!);
        deserialized.GetProperty("Data").GetString().Should().Be("test-data");
        deserialized.GetProperty("Count").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task ContinueWithAsync_WithPassParentOutput_SetsFlag()
    {
        // Arrange
        var parentRunId = Guid.NewGuid();
        JobContinuation? capturedContinuation = null;

        _storage.Setup(s => s.AddContinuationAsync(It.IsAny<JobContinuation>(), It.IsAny<CancellationToken>()))
            .Callback<JobContinuation, CancellationToken>((c, _) => capturedContinuation = c)
            .Returns(Task.CompletedTask);

        // Act
        await _scheduler.ContinueWithAsync(
            parentRunId,
            "continuation-job",
            passParentOutput: true);

        // Assert
        capturedContinuation.Should().NotBeNull();
        capturedContinuation!.PassParentOutput.Should().BeTrue();
        capturedContinuation.InputJson.Should().BeNull();
    }

    [Fact]
    public async Task ContinueWithAsync_WithQueue_SetsQueue()
    {
        // Arrange
        var parentRunId = Guid.NewGuid();
        JobContinuation? capturedContinuation = null;

        _storage.Setup(s => s.AddContinuationAsync(It.IsAny<JobContinuation>(), It.IsAny<CancellationToken>()))
            .Callback<JobContinuation, CancellationToken>((c, _) => capturedContinuation = c)
            .Returns(Task.CompletedTask);

        // Act
        await _scheduler.ContinueWithAsync(
            parentRunId,
            "continuation-job",
            queue: "critical");

        // Assert
        capturedContinuation.Should().NotBeNull();
        capturedContinuation!.Queue.Should().Be("critical");
    }

    [Theory]
    [InlineData(ContinuationCondition.OnSuccess)]
    [InlineData(ContinuationCondition.OnFailure)]
    [InlineData(ContinuationCondition.Always)]
    public async Task ContinueWithAsync_WithDifferentConditions_SetsCorrectCondition(ContinuationCondition condition)
    {
        // Arrange
        var parentRunId = Guid.NewGuid();
        JobContinuation? capturedContinuation = null;

        _storage.Setup(s => s.AddContinuationAsync(It.IsAny<JobContinuation>(), It.IsAny<CancellationToken>()))
            .Callback<JobContinuation, CancellationToken>((c, _) => capturedContinuation = c)
            .Returns(Task.CompletedTask);

        // Act
        await _scheduler.ContinueWithAsync(
            parentRunId,
            "continuation-job",
            condition: condition);

        // Assert
        capturedContinuation.Should().NotBeNull();
        capturedContinuation!.Condition.Should().Be(condition);
    }

    [Fact]
    public async Task ContinueWithAsync_ReturnsContinuationId()
    {
        // Arrange
        var parentRunId = Guid.NewGuid();
        Guid capturedId = Guid.Empty;

        _storage.Setup(s => s.AddContinuationAsync(It.IsAny<JobContinuation>(), It.IsAny<CancellationToken>()))
            .Callback<JobContinuation, CancellationToken>((c, _) => capturedId = c.Id)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _scheduler.ContinueWithAsync(parentRunId, "continuation-job");

        // Assert
        result.Should().NotBe(Guid.Empty);
        result.Should().Be(capturedId);
    }
}
