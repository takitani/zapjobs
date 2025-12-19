using Xunit;
using FluentAssertions;
using ZapJobs.Core;
using ZapJobs.Core.History;
using ZapJobs.History;
using ZapJobs.Storage.InMemory;

namespace ZapJobs.Tests.History;

public class EventStoreTests
{
    private readonly InMemoryJobStorage _storage;
    private readonly EventStore _eventStore;

    public EventStoreTests()
    {
        _storage = new InMemoryJobStorage();
        _eventStore = new EventStore(_storage);
    }

    #region Write Tests

    [Fact]
    public async Task AppendAsync_SingleEvent_AppendSuccessfully()
    {
        // Arrange
        var runId = await CreateTestRun();
        var @event = new JobEvent
        {
            RunId = runId,
            EventType = EventTypes.JobStarted,
            Category = EventCategories.Job,
            PayloadJson = "{\"workerId\":\"worker-1\"}"
        };

        // Act
        await _eventStore.AppendAsync(@event);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be(EventTypes.JobStarted);
        events[0].SequenceNumber.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AppendAsync_MultipleEvents_AssignsSequentialNumbers()
    {
        // Arrange
        var runId = await CreateTestRun();
        var event1 = CreateTestEvent(runId, EventTypes.JobStarted);
        var event2 = CreateTestEvent(runId, EventTypes.ActivityStarted);
        var event3 = CreateTestEvent(runId, EventTypes.ActivityCompleted);

        // Act
        await _eventStore.AppendAsync(event1);
        await _eventStore.AppendAsync(event2);
        await _eventStore.AppendAsync(event3);

        // Assert
        var events = await _eventStore.GetEventsAsync(runId);
        events.Should().HaveCount(3);
        events[0].SequenceNumber.Should().BeLessThan(events[1].SequenceNumber);
        events[1].SequenceNumber.Should().BeLessThan(events[2].SequenceNumber);
    }

    [Fact]
    public async Task AppendBatchAsync_BatchInsert_AppendsAllEvents()
    {
        // Arrange
        var runId = await CreateTestRun();
        var events = new List<JobEvent>
        {
            CreateTestEvent(runId, EventTypes.JobEnqueued),
            CreateTestEvent(runId, EventTypes.JobStarted),
            CreateTestEvent(runId, EventTypes.ProgressUpdated),
            CreateTestEvent(runId, EventTypes.JobCompleted)
        };

        // Act
        await _eventStore.AppendBatchAsync(events);

        // Assert
        var storedEvents = await _eventStore.GetEventsAsync(runId);
        storedEvents.Should().HaveCount(4);
    }

    #endregion

    #region Query Tests

    [Fact]
    public async Task GetEventsAsync_ReturnsAllEventsOrdered()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.JobStarted));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ProgressUpdated));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.JobCompleted));

        // Act
        var events = await _eventStore.GetEventsAsync(runId);

        // Assert
        events.Should().HaveCount(3);
        events.Select(e => e.SequenceNumber).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetEventsAsync_WithEventType_FiltersCorrectly()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.JobStarted));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ProgressUpdated));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ProgressUpdated));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.JobCompleted));

        // Act
        var events = await _eventStore.GetEventsAsync(runId, new EventQueryOptions { EventType = EventTypes.ProgressUpdated });

        // Assert
        events.Should().HaveCount(2);
        events.Should().AllSatisfy(e => e.EventType.Should().Be(EventTypes.ProgressUpdated));
    }

    [Fact]
    public async Task GetEventsAsync_WithCategory_FiltersCorrectly()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.JobStarted, EventCategories.Job));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ActivityStarted, EventCategories.Activity));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ActivityCompleted, EventCategories.Activity));

        // Act
        var events = await _eventStore.GetEventsAsync(runId, new EventQueryOptions { Category = EventCategories.Activity });

        // Assert
        events.Should().HaveCount(2);
        events.Should().AllSatisfy(e => e.Category.Should().Be(EventCategories.Activity));
    }

    [Fact]
    public async Task GetEventsAsync_WithPagination_ReturnsCorrectPage()
    {
        // Arrange
        var runId = await CreateTestRun();
        for (int i = 0; i < 10; i++)
        {
            await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ProgressUpdated));
        }

        // Act
        var page1 = await _eventStore.GetEventsAsync(runId, new EventQueryOptions { Limit = 3, Offset = 0 });
        var page2 = await _eventStore.GetEventsAsync(runId, new EventQueryOptions { Limit = 3, Offset = 3 });

        // Assert
        page1.Should().HaveCount(3);
        page2.Should().HaveCount(3);
        page1.Should().NotIntersectWith(page2);
    }

    [Fact]
    public async Task GetEventAsync_ExistingEvent_ReturnsEvent()
    {
        // Arrange
        var runId = await CreateTestRun();
        var @event = CreateTestEvent(runId, EventTypes.JobStarted);
        await _eventStore.AppendAsync(@event);

        // Act
        var result = await _eventStore.GetEventAsync(@event.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(@event.Id);
    }

    [Fact]
    public async Task GetEventAsync_NonExistent_ReturnsNull()
    {
        // Act
        var result = await _eventStore.GetEventAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestEventAsync_ReturnsLatestOfType()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ProgressUpdated, payload: "{\"progress\":10}"));
        await Task.Delay(10);
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ProgressUpdated, payload: "{\"progress\":50}"));
        await Task.Delay(10);
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ProgressUpdated, payload: "{\"progress\":100}"));

        // Act
        var latest = await _eventStore.GetLatestEventAsync(runId, EventTypes.ProgressUpdated);

        // Assert
        latest.Should().NotBeNull();
        latest!.PayloadJson.Should().Contain("100");
    }

    #endregion

    #region Timeline Tests

    [Fact]
    public async Task GetTimelineAsync_ReturnsEventsInTimeOrder()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.JobStarted));
        await Task.Delay(10);
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ProgressUpdated));
        await Task.Delay(10);
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.JobCompleted));

        // Act
        var timeline = await _eventStore.GetTimelineAsync(runId);

        // Assert
        timeline.Should().HaveCount(3);
        timeline.Select(e => e.Timestamp).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetEventsAfterAsync_ReturnsSubsequentEvents()
    {
        // Arrange
        var runId = await CreateTestRun();
        var event1 = CreateTestEvent(runId, EventTypes.JobStarted);
        var event2 = CreateTestEvent(runId, EventTypes.ProgressUpdated);
        var event3 = CreateTestEvent(runId, EventTypes.JobCompleted);

        await _eventStore.AppendAsync(event1);
        await _eventStore.AppendAsync(event2);
        await _eventStore.AppendAsync(event3);

        // Act
        var events = await _eventStore.GetEventsAfterAsync(runId, event1.SequenceNumber);

        // Assert
        events.Should().HaveCount(2);
        events.Select(e => e.SequenceNumber).Should().AllSatisfy(seq => seq.Should().BeGreaterThan(event1.SequenceNumber));
    }

    #endregion

    #region Aggregation Tests

    [Fact]
    public async Task GetEventCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.JobStarted));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ProgressUpdated));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ProgressUpdated));

        // Act
        var count = await _eventStore.GetEventCountAsync(runId);

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public async Task GetSummaryAsync_ReturnsCorrectSummary()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.JobStarted));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ProgressUpdated));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ProgressUpdated));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.JobCompleted));

        // Act
        var summary = await _eventStore.GetSummaryAsync(runId);

        // Assert
        summary.TotalEvents.Should().Be(4);
        summary.EventsByType.Should().ContainKey(EventTypes.ProgressUpdated);
        summary.EventsByType[EventTypes.ProgressUpdated].Should().Be(2);
    }

    #endregion

    #region Correlation Tests

    [Fact]
    public async Task GetByCorrelationIdAsync_ReturnsCorrelatedEvents()
    {
        // Arrange
        var runId = await CreateTestRun();
        var correlationId = Guid.NewGuid().ToString();

        var event1 = CreateTestEvent(runId, EventTypes.JobStarted);
        event1.CorrelationId = correlationId;

        var event2 = CreateTestEvent(runId, EventTypes.ActivityCompleted);
        event2.CorrelationId = correlationId;

        var event3 = CreateTestEvent(runId, EventTypes.ProgressUpdated);
        event3.CorrelationId = "different-correlation";

        await _eventStore.AppendAsync(event1);
        await _eventStore.AppendAsync(event2);
        await _eventStore.AppendAsync(event3);

        // Act
        var events = await _eventStore.GetByCorrelationIdAsync(correlationId);

        // Assert
        events.Should().HaveCount(2);
        events.Should().AllSatisfy(e => e.CorrelationId.Should().Be(correlationId));
    }

    [Fact]
    public async Task GetWorkflowEventsAsync_ReturnsWorkflowEvents()
    {
        // Arrange
        var runId = await CreateTestRun();
        var workflowId = Guid.NewGuid();

        var event1 = CreateTestEvent(runId, EventTypes.WorkflowStarted);
        event1.WorkflowId = workflowId;

        var event2 = CreateTestEvent(runId, EventTypes.StepCompleted);
        event2.WorkflowId = workflowId;

        var event3 = CreateTestEvent(runId, EventTypes.JobStarted);
        // No workflow ID

        await _eventStore.AppendAsync(event1);
        await _eventStore.AppendAsync(event2);
        await _eventStore.AppendAsync(event3);

        // Act
        var events = await _eventStore.GetWorkflowEventsAsync(workflowId);

        // Assert
        events.Should().HaveCount(2);
        events.Should().AllSatisfy(e => e.WorkflowId.Should().Be(workflowId));
    }

    #endregion

    #region Search Tests

    [Fact]
    public async Task SearchAsync_ByQuery_FindsMatchingEvents()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.BusinessEvent,
            payload: "{\"orderId\":\"ORD-123\",\"status\":\"pending\"}"));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.BusinessEvent,
            payload: "{\"orderId\":\"ORD-456\",\"status\":\"completed\"}"));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ProgressUpdated,
            payload: "{\"progress\":50}"));

        // Act
        var events = await _eventStore.SearchAsync("ORD-123");

        // Assert
        events.Should().HaveCount(1);
        events[0].PayloadJson.Should().Contain("ORD-123");
    }

    [Fact]
    public async Task SearchAsync_ByTimeRange_FindsEventsInRange()
    {
        // Arrange
        var runId = await CreateTestRun();
        var now = DateTimeOffset.UtcNow;

        var event1 = CreateTestEvent(runId, EventTypes.JobStarted);
        event1.Timestamp = now.AddMinutes(-10);

        var event2 = CreateTestEvent(runId, EventTypes.JobCompleted);
        event2.Timestamp = now.AddMinutes(-5);

        var event3 = CreateTestEvent(runId, EventTypes.JobFailed);
        event3.Timestamp = now.AddMinutes(-1);

        await _eventStore.AppendAsync(event1);
        await _eventStore.AppendAsync(event2);
        await _eventStore.AppendAsync(event3);

        // Act
        var events = await _eventStore.SearchAsync(
            query: "",
            new EventSearchOptions
            {
                From = now.AddMinutes(-7),
                To = now.AddMinutes(-3)
            });

        // Assert
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be(EventTypes.JobCompleted);
    }

    [Fact]
    public async Task SearchAsync_ByEventTypes_FindsMatchingTypes()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.JobStarted));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.JobCompleted));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.JobFailed));

        // Act
        var events = await _eventStore.SearchAsync(
            query: "",
            new EventSearchOptions
            {
                EventTypes = [EventTypes.JobStarted, EventTypes.JobFailed]
            });

        // Assert
        events.Should().HaveCount(2);
        events.Should().NotContain(e => e.EventType == EventTypes.JobCompleted);
    }

    #endregion

    #region Cleanup Tests

    [Fact]
    public async Task DeleteEventsAsync_DeletesAllRunEvents()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.JobStarted));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.ProgressUpdated));
        await _eventStore.AppendAsync(CreateTestEvent(runId, EventTypes.JobCompleted));

        // Act
        var deleted = await _eventStore.DeleteEventsAsync(runId);

        // Assert
        deleted.Should().Be(3);
        var events = await _eventStore.GetEventsAsync(runId);
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteOldEventsAsync_DeletesEventsOlderThanThreshold()
    {
        // Arrange
        var runId = await CreateTestRun();

        var oldEvent = CreateTestEvent(runId, EventTypes.JobStarted);
        oldEvent.Timestamp = DateTimeOffset.UtcNow.AddDays(-10);

        var recentEvent = CreateTestEvent(runId, EventTypes.JobCompleted);
        recentEvent.Timestamp = DateTimeOffset.UtcNow;

        await _eventStore.AppendAsync(oldEvent);
        await _eventStore.AppendAsync(recentEvent);

        // Act
        var deleted = await _eventStore.DeleteOldEventsAsync(TimeSpan.FromDays(7));

        // Assert
        deleted.Should().Be(1);
        var events = await _eventStore.GetEventsAsync(runId);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be(EventTypes.JobCompleted);
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

    private static JobEvent CreateTestEvent(
        Guid runId,
        string eventType,
        string category = EventCategories.Job,
        string payload = "{}")
    {
        return new JobEvent
        {
            RunId = runId,
            EventType = eventType,
            Category = category,
            PayloadJson = payload,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
