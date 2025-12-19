using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ZapJobs.Core.Events;
using ZapJobs.Events;

namespace ZapJobs.Tests.Events;

public class JobEventDispatcherTests
{
    private readonly Mock<ILogger<JobEventDispatcher>> _logger;
    private readonly Mock<ILogger<JobEventBackgroundService>> _bgLogger;
    private readonly ServiceCollection _services;

    public JobEventDispatcherTests()
    {
        _logger = new Mock<ILogger<JobEventDispatcher>>();
        _bgLogger = new Mock<ILogger<JobEventBackgroundService>>();
        _services = new ServiceCollection();
    }

    private async Task ProcessEventsAsync(JobEventDispatcher dispatcher, IServiceProvider serviceProvider, int maxEvents = 10)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var processed = 0;

        try
        {
            while (processed < maxEvents)
            {
                // Wait for data to be available (with timeout)
                if (!await dispatcher.Reader.WaitToReadAsync(cts.Token))
                    break; // Channel completed

                // Drain all available items
                while (processed < maxEvents && dispatcher.Reader.TryRead(out var task))
                {
                    using var scope = serviceProvider.CreateScope();
                    await task(scope.ServiceProvider, CancellationToken.None);
                    processed++;
                }

                // Exit if we processed at least one event
                if (processed > 0)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout - that's OK, we'll check assertions
        }
    }

    [Fact]
    public async Task DispatchAsync_WithRegisteredHandler_CallsHandler()
    {
        // Arrange
        var handler = new TestJobStartedHandler();
        _services.AddSingleton<IJobEventHandler<JobStartedEvent>>(handler);
        var serviceProvider = _services.BuildServiceProvider();

        var dispatcher = new JobEventDispatcher(serviceProvider, _logger.Object);

        var @event = new JobStartedEvent
        {
            RunId = Guid.NewGuid(),
            JobTypeId = "test-job",
            Timestamp = DateTimeOffset.UtcNow,
            AttemptNumber = 1,
            Queue = "default"
        };

        // Act
        await dispatcher.DispatchAsync(@event);
        await ProcessEventsAsync(dispatcher, serviceProvider);

        // Assert
        handler.ReceivedEvents.Should().HaveCount(1);
        handler.ReceivedEvents[0].JobTypeId.Should().Be("test-job");
    }

    [Fact]
    public async Task DispatchAsync_WithMultipleHandlers_CallsAllHandlers()
    {
        // Arrange
        var handler1 = new TestJobStartedHandler();
        var handler2 = new TestJobStartedHandler();
        _services.AddSingleton<IJobEventHandler<JobStartedEvent>>(handler1);
        _services.AddSingleton<IJobEventHandler<JobStartedEvent>>(handler2);
        var serviceProvider = _services.BuildServiceProvider();

        var dispatcher = new JobEventDispatcher(serviceProvider, _logger.Object);

        var @event = new JobStartedEvent
        {
            RunId = Guid.NewGuid(),
            JobTypeId = "test-job",
            Timestamp = DateTimeOffset.UtcNow,
            AttemptNumber = 1,
            Queue = "default"
        };

        // Act
        await dispatcher.DispatchAsync(@event);
        await ProcessEventsAsync(dispatcher, serviceProvider);

        // Assert
        handler1.ReceivedEvents.Should().HaveCount(1);
        handler2.ReceivedEvents.Should().HaveCount(1);
    }

    [Fact]
    public async Task DispatchAsync_NoHandlers_DoesNotThrow()
    {
        // Arrange
        var serviceProvider = _services.BuildServiceProvider();
        var dispatcher = new JobEventDispatcher(serviceProvider, _logger.Object);

        var @event = new JobStartedEvent
        {
            RunId = Guid.NewGuid(),
            JobTypeId = "test-job",
            Timestamp = DateTimeOffset.UtcNow,
            AttemptNumber = 1,
            Queue = "default"
        };

        // Act
        var act = () => dispatcher.DispatchAsync(@event);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DispatchAsync_HandlerThrows_DoesNotAffectOtherHandlers()
    {
        // Arrange
        var throwingHandler = new ThrowingJobStartedHandler();
        var normalHandler = new TestJobStartedHandler();
        _services.AddSingleton<IJobEventHandler<JobStartedEvent>>(throwingHandler);
        _services.AddSingleton<IJobEventHandler<JobStartedEvent>>(normalHandler);
        var serviceProvider = _services.BuildServiceProvider();

        var dispatcher = new JobEventDispatcher(serviceProvider, _logger.Object);

        var @event = new JobStartedEvent
        {
            RunId = Guid.NewGuid(),
            JobTypeId = "test-job",
            Timestamp = DateTimeOffset.UtcNow,
            AttemptNumber = 1,
            Queue = "default"
        };

        // Act
        await dispatcher.DispatchAsync(@event);
        await ProcessEventsAsync(dispatcher, serviceProvider);

        // Assert - normal handler should still receive the event
        normalHandler.ReceivedEvents.Should().HaveCount(1);
    }

    [Fact]
    public async Task DispatchAsync_ReturnsImmediately_FireAndForget()
    {
        // Arrange
        var slowHandler = new SlowJobStartedHandler(TimeSpan.FromSeconds(1));
        _services.AddSingleton<IJobEventHandler<JobStartedEvent>>(slowHandler);
        var serviceProvider = _services.BuildServiceProvider();

        var dispatcher = new JobEventDispatcher(serviceProvider, _logger.Object);

        var @event = new JobStartedEvent
        {
            RunId = Guid.NewGuid(),
            JobTypeId = "test-job",
            Timestamp = DateTimeOffset.UtcNow,
            AttemptNumber = 1,
            Queue = "default"
        };

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await dispatcher.DispatchAsync(@event);
        stopwatch.Stop();

        // Assert - dispatch should return quickly (fire and forget)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Fact]
    public async Task DispatchAsync_DifferentEventTypes_OnlyMatchingHandlersCalled()
    {
        // Arrange
        var startedHandler = new TestJobStartedHandler();
        var completedHandler = new TestJobCompletedHandler();
        _services.AddSingleton<IJobEventHandler<JobStartedEvent>>(startedHandler);
        _services.AddSingleton<IJobEventHandler<JobCompletedEvent>>(completedHandler);
        var serviceProvider = _services.BuildServiceProvider();

        var dispatcher = new JobEventDispatcher(serviceProvider, _logger.Object);

        var startedEvent = new JobStartedEvent
        {
            RunId = Guid.NewGuid(),
            JobTypeId = "test-job",
            Timestamp = DateTimeOffset.UtcNow,
            AttemptNumber = 1,
            Queue = "default"
        };

        // Act
        await dispatcher.DispatchAsync(startedEvent);
        await ProcessEventsAsync(dispatcher, serviceProvider);

        // Assert
        startedHandler.ReceivedEvents.Should().HaveCount(1);
        completedHandler.ReceivedEvents.Should().BeEmpty();
    }

    // Test handler implementations
    private class TestJobStartedHandler : IJobEventHandler<JobStartedEvent>
    {
        public List<JobStartedEvent> ReceivedEvents { get; } = new();

        public Task HandleAsync(JobStartedEvent @event, CancellationToken ct = default)
        {
            ReceivedEvents.Add(@event);
            return Task.CompletedTask;
        }
    }

    private class TestJobCompletedHandler : IJobEventHandler<JobCompletedEvent>
    {
        public List<JobCompletedEvent> ReceivedEvents { get; } = new();

        public Task HandleAsync(JobCompletedEvent @event, CancellationToken ct = default)
        {
            ReceivedEvents.Add(@event);
            return Task.CompletedTask;
        }
    }

    private class ThrowingJobStartedHandler : IJobEventHandler<JobStartedEvent>
    {
        public Task HandleAsync(JobStartedEvent @event, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Handler failed");
        }
    }

    private class SlowJobStartedHandler : IJobEventHandler<JobStartedEvent>
    {
        private readonly TimeSpan _delay;

        public SlowJobStartedHandler(TimeSpan delay)
        {
            _delay = delay;
        }

        public async Task HandleAsync(JobStartedEvent @event, CancellationToken ct = default)
        {
            await Task.Delay(_delay, ct);
        }
    }
}
