using Xunit;
using FluentAssertions;
using ZapJobs.Core;
using ZapJobs.Core.History;
using ZapJobs.Storage.InMemory;

namespace ZapJobs.Storage.InMemory.Tests;

public class InMemoryEventStorageTests
{
    private readonly InMemoryJobStorage _storage;

    public InMemoryEventStorageTests()
    {
        _storage = new InMemoryJobStorage();
    }

    #region AppendEventAsync Tests

    [Fact]
    public async Task AppendEventAsync_SingleEvent_SetsSequenceNumber()
    {
        // Arrange
        var runId = await CreateTestRun();
        var @event = new JobEvent
        {
            RunId = runId,
            EventType = EventTypes.JobStarted,
            PayloadJson = "{}"
        };

        // Act
        await _storage.AppendEventAsync(@event);

        // Assert
        @event.SequenceNumber.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AppendEventAsync_MultipleEvents_IncrementsSequence()
    {
        // Arrange
        var runId = await CreateTestRun();
        var event1 = new JobEvent { RunId = runId, EventType = EventTypes.JobStarted };
        var event2 = new JobEvent { RunId = runId, EventType = EventTypes.ProgressUpdated };
        var event3 = new JobEvent { RunId = runId, EventType = EventTypes.JobCompleted };

        // Act
        await _storage.AppendEventAsync(event1);
        await _storage.AppendEventAsync(event2);
        await _storage.AppendEventAsync(event3);

        // Assert
        event1.SequenceNumber.Should().Be(1);
        event2.SequenceNumber.Should().Be(2);
        event3.SequenceNumber.Should().Be(3);
    }

    #endregion

    #region AppendEventsAsync Tests

    [Fact]
    public async Task AppendEventsAsync_BatchInsert_SetsSequenceNumbers()
    {
        // Arrange
        var runId = await CreateTestRun();
        var events = new List<JobEvent>
        {
            new() { RunId = runId, EventType = EventTypes.JobStarted },
            new() { RunId = runId, EventType = EventTypes.ProgressUpdated },
            new() { RunId = runId, EventType = EventTypes.JobCompleted }
        };

        // Act
        await _storage.AppendEventsAsync(events);

        // Assert
        events[0].SequenceNumber.Should().Be(1);
        events[1].SequenceNumber.Should().Be(2);
        events[2].SequenceNumber.Should().Be(3);
    }

    [Fact]
    public async Task AppendEventsAsync_EmptyList_DoesNothing()
    {
        // Arrange
        var events = new List<JobEvent>();

        // Act & Assert - should not throw
        await _storage.AppendEventsAsync(events);
    }

    #endregion

    #region GetEventsAsync Tests

    [Fact]
    public async Task GetEventsAsync_ReturnsAllEventsForRun()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = EventTypes.JobStarted });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = EventTypes.ProgressUpdated });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = EventTypes.JobCompleted });

        // Act
        var events = await _storage.GetEventsAsync(runId);

        // Assert
        events.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetEventsAsync_OrdersBySequence()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = "event-1" });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = "event-2" });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = "event-3" });

        // Act
        var events = await _storage.GetEventsAsync(runId);

        // Assert
        events.Select(e => e.SequenceNumber).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetEventsAsync_NonExistentRun_ReturnsEmpty()
    {
        // Act
        var events = await _storage.GetEventsAsync(Guid.NewGuid());

        // Assert
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEventsAsync_WithEventType_FiltersCorrectly()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = EventTypes.JobStarted });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = EventTypes.ProgressUpdated });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = EventTypes.ProgressUpdated });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = EventTypes.JobCompleted });

        // Act
        var events = await _storage.GetEventsAsync(runId, EventTypes.ProgressUpdated, null, 100, 0);

        // Assert
        events.Should().HaveCount(2);
        events.Should().AllSatisfy(e => e.EventType.Should().Be(EventTypes.ProgressUpdated));
    }

    [Fact]
    public async Task GetEventsAsync_WithCategory_FiltersCorrectly()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = EventTypes.JobStarted, Category = EventCategories.Job });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = EventTypes.ActivityStarted, Category = EventCategories.Activity });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = EventTypes.ActivityCompleted, Category = EventCategories.Activity });

        // Act
        var events = await _storage.GetEventsAsync(runId, null, EventCategories.Activity, 100, 0);

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
            await _storage.AppendEventAsync(new JobEvent
            {
                RunId = runId,
                EventType = $"event-{i}"
            });
        }

        // Act
        var page1 = await _storage.GetEventsAsync(runId, null, null, 3, 0);
        var page2 = await _storage.GetEventsAsync(runId, null, null, 3, 3);
        var page3 = await _storage.GetEventsAsync(runId, null, null, 3, 6);

        // Assert
        page1.Should().HaveCount(3);
        page2.Should().HaveCount(3);
        page3.Should().HaveCount(3);

        page1.Select(e => e.EventType).Should().BeEquivalentTo(["event-0", "event-1", "event-2"]);
        page2.Select(e => e.EventType).Should().BeEquivalentTo(["event-3", "event-4", "event-5"]);
        page3.Select(e => e.EventType).Should().BeEquivalentTo(["event-6", "event-7", "event-8"]);
    }

    #endregion

    #region GetEventsAfterSequenceAsync Tests

    [Fact]
    public async Task GetEventsAfterSequenceAsync_ReturnsEventsAfterSequence()
    {
        // Arrange
        var runId = await CreateTestRun();
        var event1 = new JobEvent { RunId = runId, EventType = "event-1" };
        var event2 = new JobEvent { RunId = runId, EventType = "event-2" };
        var event3 = new JobEvent { RunId = runId, EventType = "event-3" };

        await _storage.AppendEventAsync(event1);
        await _storage.AppendEventAsync(event2);
        await _storage.AppendEventAsync(event3);

        // Act
        var events = await _storage.GetEventsAfterSequenceAsync(runId, event1.SequenceNumber);

        // Assert
        events.Should().HaveCount(2);
        events[0].SequenceNumber.Should().BeGreaterThan(event1.SequenceNumber);
        events[1].SequenceNumber.Should().BeGreaterThan(event1.SequenceNumber);
    }

    #endregion

    #region GetEventAsync Tests

    [Fact]
    public async Task GetEventAsync_ExistingEvent_ReturnsEvent()
    {
        // Arrange
        var runId = await CreateTestRun();
        var @event = new JobEvent
        {
            RunId = runId,
            EventType = EventTypes.JobStarted,
            PayloadJson = "{\"test\":true}"
        };
        await _storage.AppendEventAsync(@event);

        // Act
        var result = await _storage.GetEventAsync(@event.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(@event.Id);
        result.PayloadJson.Should().Contain("test");
    }

    [Fact]
    public async Task GetEventAsync_NonExistent_ReturnsNull()
    {
        // Act
        var result = await _storage.GetEventAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetLatestEventAsync Tests

    [Fact]
    public async Task GetLatestEventAsync_ReturnsLatestOfType()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = EventTypes.ProgressUpdated,
            PayloadJson = "{\"progress\":10}"
        });
        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = EventTypes.ProgressUpdated,
            PayloadJson = "{\"progress\":50}"
        });
        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = EventTypes.ProgressUpdated,
            PayloadJson = "{\"progress\":100}"
        });

        // Act
        var latest = await _storage.GetLatestEventAsync(runId, EventTypes.ProgressUpdated);

        // Assert
        latest.Should().NotBeNull();
        latest!.PayloadJson.Should().Contain("100");
    }

    [Fact]
    public async Task GetLatestEventAsync_NoMatchingType_ReturnsNull()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = EventTypes.JobStarted
        });

        // Act
        var latest = await _storage.GetLatestEventAsync(runId, EventTypes.JobCompleted);

        // Assert
        latest.Should().BeNull();
    }

    #endregion

    #region GetEventCountAsync Tests

    [Fact]
    public async Task GetEventCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = "event-1" });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = "event-2" });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = "event-3" });

        // Act
        var count = await _storage.GetEventCountAsync(runId);

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public async Task GetEventCountAsync_NoEvents_ReturnsZero()
    {
        // Arrange
        var runId = await CreateTestRun();

        // Act
        var count = await _storage.GetEventCountAsync(runId);

        // Assert
        count.Should().Be(0);
    }

    #endregion

    #region GetNextEventSequenceAsync Tests

    [Fact]
    public async Task GetNextEventSequenceAsync_NoEvents_ReturnsOne()
    {
        // Act
        var nextSeq = await _storage.GetNextEventSequenceAsync();

        // Assert
        nextSeq.Should().Be(1);
    }

    [Fact]
    public async Task GetNextEventSequenceAsync_WithEvents_ReturnsNextSequence()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = "event-1" });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = "event-2" });

        // Act
        var nextSeq = await _storage.GetNextEventSequenceAsync();

        // Assert
        nextSeq.Should().Be(3);
    }

    #endregion

    #region GetEventsByCorrelationIdAsync Tests

    [Fact]
    public async Task GetEventsByCorrelationIdAsync_ReturnsCorrelatedEvents()
    {
        // Arrange
        var runId = await CreateTestRun();
        var correlationId = Guid.NewGuid().ToString();

        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = EventTypes.JobStarted,
            CorrelationId = correlationId
        });
        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = EventTypes.JobCompleted,
            CorrelationId = correlationId
        });
        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = EventTypes.JobFailed,
            CorrelationId = "different"
        });

        // Act
        var events = await _storage.GetEventsByCorrelationIdAsync(correlationId);

        // Assert
        events.Should().HaveCount(2);
        events.Should().AllSatisfy(e => e.CorrelationId.Should().Be(correlationId));
    }

    #endregion

    #region GetEventsByWorkflowIdAsync Tests

    [Fact]
    public async Task GetEventsByWorkflowIdAsync_ReturnsWorkflowEvents()
    {
        // Arrange
        var runId = await CreateTestRun();
        var workflowId = Guid.NewGuid();

        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = EventTypes.WorkflowStarted,
            WorkflowId = workflowId
        });
        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = EventTypes.WorkflowCompleted,
            WorkflowId = workflowId
        });
        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = EventTypes.JobStarted,
            WorkflowId = null
        });

        // Act
        var events = await _storage.GetEventsByWorkflowIdAsync(workflowId);

        // Assert
        events.Should().HaveCount(2);
        events.Should().AllSatisfy(e => e.WorkflowId.Should().Be(workflowId));
    }

    #endregion

    #region SearchEventsAsync Tests

    [Fact]
    public async Task SearchEventsAsync_ByQuery_FindsMatchingEvents()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = "order.created",
            PayloadJson = "{\"orderId\":\"ORD-123\",\"total\":99.99}"
        });
        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = "order.created",
            PayloadJson = "{\"orderId\":\"ORD-456\",\"total\":149.99}"
        });

        // Act
        var events = await _storage.SearchEventsAsync("ORD-123");

        // Assert
        events.Should().HaveCount(1);
        events[0].PayloadJson.Should().Contain("ORD-123");
    }

    [Fact]
    public async Task SearchEventsAsync_ByTimeRange_FindsEventsInRange()
    {
        // Arrange
        var runId = await CreateTestRun();
        var now = DateTimeOffset.UtcNow;

        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = "old-event",
            Timestamp = now.AddDays(-5)
        });
        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = "recent-event",
            Timestamp = now.AddDays(-1)
        });

        // Act
        var events = await _storage.SearchEventsAsync(
            query: "",
            runId: null,
            workflowId: null,
            eventTypes: null,
            from: now.AddDays(-3),
            to: now);

        // Assert
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("recent-event");
    }

    [Fact]
    public async Task SearchEventsAsync_ByEventTypes_FiltersCorrectly()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = EventTypes.JobStarted });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = EventTypes.JobCompleted });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = EventTypes.JobFailed });

        // Act
        var events = await _storage.SearchEventsAsync(
            query: "",
            runId: null,
            workflowId: null,
            eventTypes: [EventTypes.JobStarted, EventTypes.JobFailed]);

        // Assert
        events.Should().HaveCount(2);
        events.Should().NotContain(e => e.EventType == EventTypes.JobCompleted);
    }

    #endregion

    #region DeleteEventsForRunAsync Tests

    [Fact]
    public async Task DeleteEventsForRunAsync_DeletesAllRunEvents()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = "event-1" });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = "event-2" });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = "event-3" });

        // Act
        var deleted = await _storage.DeleteEventsForRunAsync(runId);

        // Assert
        deleted.Should().Be(3);
        var events = await _storage.GetEventsAsync(runId);
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteEventsForRunAsync_DoesNotAffectOtherRuns()
    {
        // Arrange
        var runId1 = await CreateTestRun();
        var runId2 = await CreateTestRun();

        await _storage.AppendEventAsync(new JobEvent { RunId = runId1, EventType = "event-1" });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId2, EventType = "event-2" });

        // Act
        await _storage.DeleteEventsForRunAsync(runId1);

        // Assert
        var eventsRun1 = await _storage.GetEventsAsync(runId1);
        var eventsRun2 = await _storage.GetEventsAsync(runId2);

        eventsRun1.Should().BeEmpty();
        eventsRun2.Should().HaveCount(1);
    }

    #endregion

    #region DeleteOldEventsAsync Tests

    [Fact]
    public async Task DeleteOldEventsAsync_DeletesEventsOlderThanMaxAge()
    {
        // Arrange
        var runId = await CreateTestRun();
        var now = DateTimeOffset.UtcNow;

        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = "old-event",
            Timestamp = now.AddDays(-10)
        });
        await _storage.AppendEventAsync(new JobEvent
        {
            RunId = runId,
            EventType = "recent-event",
            Timestamp = now
        });

        // Act
        var deleted = await _storage.DeleteOldEventsAsync(TimeSpan.FromDays(5));

        // Assert
        deleted.Should().Be(1);
        var events = await _storage.GetEventsAsync(runId);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("recent-event");
    }

    #endregion

    #region Clear Tests

    [Fact]
    public async Task Clear_ClearsAllEvents()
    {
        // Arrange
        var runId = await CreateTestRun();
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = "event-1" });
        await _storage.AppendEventAsync(new JobEvent { RunId = runId, EventType = "event-2" });

        // Act
        _storage.Clear();

        // Assert
        var nextSeq = await _storage.GetNextEventSequenceAsync();
        nextSeq.Should().Be(1); // Reset to 1
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
