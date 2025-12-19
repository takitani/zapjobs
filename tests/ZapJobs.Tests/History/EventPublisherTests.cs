using Xunit;
using FluentAssertions;
using System.Text.Json;
using ZapJobs.Core;
using ZapJobs.Core.History;
using ZapJobs.History;
using ZapJobs.Storage.InMemory;

namespace ZapJobs.Tests.History;

public class EventPublisherTests
{
    private readonly InMemoryJobStorage _storage;
    private readonly EventStore _eventStore;
    private readonly EventPublisher _publisher;

    public EventPublisherTests()
    {
        _storage = new InMemoryJobStorage();
        _eventStore = new EventStore(_storage);
        _publisher = new EventPublisher(_eventStore);
    }

    #region Job Lifecycle Events

    [Fact]
    public async Task PublishJobEnqueuedAsync_PublishesCorrectEvent()
    {
        // Arrange
        var runId = await CreateTestRun();
        var payload = new JobEnqueuedPayload("test-job", "default", "{}", null);

        // Act
        await _publisher.PublishJobEnqueuedAsync(runId, payload);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be(EventTypes.JobEnqueued);
        events[0].Category.Should().Be(EventCategories.Job);

        var stored = JsonSerializer.Deserialize<JobEnqueuedPayload>(events[0].PayloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        stored!.JobTypeId.Should().Be("test-job");
    }

    [Fact]
    public async Task PublishJobStartedAsync_PublishesCorrectEvent()
    {
        // Arrange
        var runId = await CreateTestRun();
        var payload = new JobStartedPayload("worker-1", 1, false, false, null);

        // Act
        await _publisher.PublishJobStartedAsync(runId, payload);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be(EventTypes.JobStarted);

        var stored = JsonSerializer.Deserialize<JobStartedPayload>(events[0].PayloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        stored!.WorkerId.Should().Be("worker-1");
        stored.AttemptNumber.Should().Be(1);
    }

    [Fact]
    public async Task PublishJobCompletedAsync_PublishesWithDurationAndSuccess()
    {
        // Arrange
        var runId = await CreateTestRun();
        var payload = new JobCompletedPayload(1500, "{\"result\":\"success\"}", 10, 0, 10);

        // Act
        await _publisher.PublishJobCompletedAsync(runId, payload);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be(EventTypes.JobCompleted);
        events[0].DurationMs.Should().Be(1500);
        events[0].IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PublishJobFailedAsync_PublishesWithErrorDetails()
    {
        // Arrange
        var runId = await CreateTestRun();
        var payload = new JobFailedPayload(
            "Something went wrong",
            "InvalidOperationException",
            "at Test.Method()",
            3,
            3,
            false,
            null,
            true);

        // Act
        await _publisher.PublishJobFailedAsync(runId, payload);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be(EventTypes.JobFailed);
        events[0].IsSuccess.Should().BeFalse();

        var stored = JsonSerializer.Deserialize<JobFailedPayload>(events[0].PayloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        stored!.ErrorMessage.Should().Be("Something went wrong");
        stored.MovedToDeadLetter.Should().BeTrue();
    }

    [Fact]
    public async Task PublishJobRetryingAsync_PublishesRetryInfo()
    {
        // Arrange
        var runId = await CreateTestRun();
        var retryAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var payload = new JobRetryingPayload(1, 2, 3, 30000, retryAt, "Connection failed");

        // Act
        await _publisher.PublishJobRetryingAsync(runId, payload);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be(EventTypes.JobRetrying);

        var stored = JsonSerializer.Deserialize<JobRetryingPayload>(events[0].PayloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        stored!.FailedAttempt.Should().Be(1);
        stored.NextAttempt.Should().Be(2);
        stored.DelayMs.Should().Be(30000);
    }

    #endregion

    #region Progress Events

    [Fact]
    public async Task PublishProgressAsync_PublishesProgress()
    {
        // Arrange
        var runId = await CreateTestRun();

        // Act
        await _publisher.PublishProgressAsync(runId, 50, 100, "Processing items...");

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be(EventTypes.ProgressUpdated);
        events[0].Category.Should().Be(EventCategories.Progress);

        var stored = JsonSerializer.Deserialize<ProgressUpdatedPayload>(events[0].PayloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        stored!.Current.Should().Be(50);
        stored.Total.Should().Be(100);
        stored.Message.Should().Be("Processing items...");
    }

    [Fact]
    public async Task PublishMilestoneAsync_PublishesMilestone()
    {
        // Arrange
        var runId = await CreateTestRun();

        // Act
        await _publisher.PublishMilestoneAsync(runId, "data-validation", 25, "Data Validation Complete");

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be(EventTypes.MilestoneReached);

        var stored = JsonSerializer.Deserialize<MilestoneReachedPayload>(events[0].PayloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        stored!.MilestoneName.Should().Be("data-validation");
    }

    #endregion

    #region Context Management

    [Fact]
    public async Task SetCorrelationId_SetsCorrelationIdOnEvents()
    {
        // Arrange
        var runId = await CreateTestRun();
        var correlationId = Guid.NewGuid().ToString();
        _publisher.SetCorrelationId(correlationId);
        var payload = new JobStartedPayload("worker-1", 1, false, false, null);

        // Act
        await _publisher.PublishJobStartedAsync(runId, payload);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events[0].CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public async Task SetActor_SetsActorOnEvents()
    {
        // Arrange
        var runId = await CreateTestRun();
        var actor = "api-user-123";
        _publisher.SetActor(actor);
        var payload = new JobStartedPayload("worker-1", 1, false, false, null);

        // Act
        await _publisher.PublishJobStartedAsync(runId, payload);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events[0].Actor.Should().Be(actor);
    }

    [Fact]
    public async Task SetCausationId_SetsCausationIdOnEvents()
    {
        // Arrange
        var runId = await CreateTestRun();
        var causationId = Guid.NewGuid().ToString();
        _publisher.SetCausationId(causationId);
        var payload = new JobStartedPayload("worker-1", 1, false, false, null);

        // Act
        await _publisher.PublishJobStartedAsync(runId, payload);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events[0].CausationId.Should().Be(causationId);
    }

    [Fact]
    public async Task SetWorkflowId_SetsWorkflowIdOnEvents()
    {
        // Arrange
        var runId = await CreateTestRun();
        var workflowId = Guid.NewGuid();
        _publisher.SetWorkflowId(workflowId);
        var payload = new JobStartedPayload("worker-1", 1, false, false, null);

        // Act
        await _publisher.PublishJobStartedAsync(runId, payload);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events[0].WorkflowId.Should().Be(workflowId);
    }

    [Fact]
    public async Task ClearContext_ClearsAllContext()
    {
        // Arrange
        var runId = await CreateTestRun();
        _publisher.SetCorrelationId("correlation-1");
        _publisher.SetActor("actor-1");
        _publisher.ClearContext();
        var payload = new JobStartedPayload("worker-1", 1, false, false, null);

        // Act
        await _publisher.PublishJobStartedAsync(runId, payload);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events[0].CorrelationId.Should().BeNull();
        events[0].Actor.Should().BeNull();
    }

    #endregion

    #region Custom Events

    [Fact]
    public async Task PublishBusinessEventAsync_PublishesWithCustomType()
    {
        // Arrange
        var runId = await CreateTestRun();
        var customData = new Dictionary<string, object?> { ["orderId"] = "ORD-123", ["status"] = "shipped" };

        // Act
        await _publisher.PublishBusinessEventAsync(runId, "order.shipped", "Order", "ORD-123", customData);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be(EventTypes.BusinessEvent);
        events[0].Category.Should().Be(EventCategories.Custom);
        events[0].PayloadJson.Should().Contain("ORD-123");
    }

    [Fact]
    public async Task PublishMetricAsync_PublishesMetricCategory()
    {
        // Arrange
        var runId = await CreateTestRun();

        // Act
        await _publisher.PublishMetricAsync(runId, "api_calls", 150);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be(EventTypes.MetricRecorded);
        events[0].Category.Should().Be(EventCategories.Custom);
    }

    [Fact]
    public async Task PublishLogAsync_PublishesLogMessage()
    {
        // Arrange
        var runId = await CreateTestRun();

        // Act
        await _publisher.PublishLogAsync(runId, "Processing started", EventSeverity.Info);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be(EventTypes.LogMessage);
        events[0].Category.Should().Be(EventCategories.Custom);
    }

    #endregion

    #region EventPublisherFactory Tests

    [Fact]
    public async Task EventPublisherFactory_CreateWithContext_SetsContext()
    {
        // Arrange
        var factory = new EventPublisherFactory(_eventStore);
        var correlationId = Guid.NewGuid().ToString();
        var actor = "test-actor";
        var publisher = factory.Create(correlationId: correlationId, actor: actor);
        var runId = await CreateTestRun();
        var payload = new JobStartedPayload("worker-1", 1, false, false, null);

        // Act
        await publisher.PublishJobStartedAsync(runId, payload);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events[0].CorrelationId.Should().Be(correlationId);
        events[0].Actor.Should().Be(actor);
    }

    [Fact]
    public async Task EventPublisherFactory_CreateForWorkflow_SetsWorkflowId()
    {
        // Arrange
        var factory = new EventPublisherFactory(_eventStore);
        var workflowId = Guid.NewGuid();
        var publisher = factory.CreateForWorkflow(workflowId);
        var runId = await CreateTestRun();
        var payload = new JobStartedPayload("worker-1", 1, false, false, null);

        // Act
        await publisher.PublishJobStartedAsync(runId, payload);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events[0].WorkflowId.Should().Be(workflowId);
        events[0].CorrelationId.Should().Be(workflowId.ToString());
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
}
