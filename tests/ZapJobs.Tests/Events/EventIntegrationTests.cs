using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using ZapJobs.Core;
using ZapJobs.Core.Events;
using ZapJobs.Events;
using ZapJobs.Execution;
using ZapJobs.Tracking;

namespace ZapJobs.Tests.Events;

public class EventIntegrationTests
{
    private readonly Mock<IJobStorage> _storage;
    private readonly Mock<IJobLoggerFactory> _loggerFactory;
    private readonly Mock<IJobLogger> _jobLogger;
    private readonly Mock<ILogger<JobExecutor>> _executorLogger;
    private readonly Mock<ILogger<RetryHandler>> _retryLogger;
    private readonly Mock<ILogger<JobEventDispatcher>> _dispatcherLogger;
    private readonly ZapJobsOptions _options;
    private readonly TestEventCollector _eventCollector;
    private readonly JobExecutor _executor;
    private readonly JobEventDispatcher _dispatcher;
    private readonly ServiceProvider _serviceProvider;

    public EventIntegrationTests()
    {
        _storage = new Mock<IJobStorage>();
        _loggerFactory = new Mock<IJobLoggerFactory>();
        _jobLogger = new Mock<IJobLogger>();
        _executorLogger = new Mock<ILogger<JobExecutor>>();
        _retryLogger = new Mock<ILogger<RetryHandler>>();
        _dispatcherLogger = new Mock<ILogger<JobEventDispatcher>>();
        _options = new ZapJobsOptions();
        _eventCollector = new TestEventCollector();

        _loggerFactory.Setup(f => f.CreateLogger(It.IsAny<Guid>()))
            .Returns(_jobLogger.Object);

        _jobLogger.Setup(l => l.InfoAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);
        _jobLogger.Setup(l => l.ErrorAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<Exception?>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);
        _jobLogger.Setup(l => l.WarningAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);

        _storage.Setup(s => s.GetContinuationsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JobContinuation>());

        var services = new ServiceCollection();
        services.AddSingleton(_storage.Object);
        services.AddSingleton(_loggerFactory.Object);
        services.AddSingleton(Options.Create(_options));
        services.AddSingleton<IJobEventHandler<JobStartedEvent>>(_eventCollector);
        services.AddSingleton<IJobEventHandler<JobCompletedEvent>>(_eventCollector);
        services.AddSingleton<IJobEventHandler<JobFailedEvent>>(_eventCollector);
        services.AddSingleton<IJobEventHandler<JobRetryingEvent>>(_eventCollector);

        _serviceProvider = services.BuildServiceProvider();

        _dispatcher = new JobEventDispatcher(_serviceProvider, _dispatcherLogger.Object);

        var retryHandler = new RetryHandler(_retryLogger.Object);

        _executor = new JobExecutor(
            _serviceProvider,
            _storage.Object,
            _loggerFactory.Object,
            retryHandler,
            Options.Create(_options),
            _executorLogger.Object,
            _dispatcher);
    }

    private async Task ProcessEventsAsync(int maxEvents = 10)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var processed = 0;

        try
        {
            while (processed < maxEvents)
            {
                // Wait for data to be available (with timeout)
                if (!await _dispatcher.Reader.WaitToReadAsync(cts.Token))
                    break; // Channel completed

                // Drain all available items
                while (processed < maxEvents && _dispatcher.Reader.TryRead(out var task))
                {
                    using var scope = _serviceProvider.CreateScope();
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
    public async Task ExecuteAsync_SuccessfulJob_DispatchesStartedAndCompletedEvents()
    {
        // Arrange
        _executor.RegisterJobType<TestSuccessJob>();

        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobTypeId = "test-success-job",
            Status = JobRunStatus.Pending,
            Queue = "default"
        };

        _storage.Setup(s => s.GetJobDefinitionAsync("test-success-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.UpdateRunAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _executor.ExecuteAsync(run);
        await ProcessEventsAsync();

        // Assert
        _eventCollector.StartedEvents.Should().HaveCount(1);
        _eventCollector.StartedEvents[0].RunId.Should().Be(run.Id);
        _eventCollector.StartedEvents[0].JobTypeId.Should().Be("test-success-job");

        _eventCollector.CompletedEvents.Should().HaveCount(1);
        _eventCollector.CompletedEvents[0].RunId.Should().Be(run.Id);
        _eventCollector.CompletedEvents[0].JobTypeId.Should().Be("test-success-job");

        _eventCollector.FailedEvents.Should().BeEmpty();
        _eventCollector.RetryingEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_FailingJob_DispatchesStartedAndFailedEvents()
    {
        // Arrange
        _executor.RegisterJobType<TestFailingJob>();

        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobTypeId = "test-failing-job",
            Status = JobRunStatus.Pending,
            Queue = "default",
            AttemptNumber = 3 // Max retries reached
        };

        _storage.Setup(s => s.GetJobDefinitionAsync("test-failing-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.UpdateRunAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _executor.ExecuteAsync(run);
        await ProcessEventsAsync();

        // Assert
        _eventCollector.StartedEvents.Should().HaveCount(1);

        _eventCollector.FailedEvents.Should().HaveCount(1);
        _eventCollector.FailedEvents[0].RunId.Should().Be(run.Id);
        _eventCollector.FailedEvents[0].WillRetry.Should().BeFalse();
        _eventCollector.FailedEvents[0].MovedToDeadLetter.Should().BeTrue();
        _eventCollector.FailedEvents[0].ErrorMessage.Should().Contain("Job failed intentionally");

        _eventCollector.CompletedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_RetryableError_DispatchesRetryingEvent()
    {
        // Arrange
        _executor.RegisterJobType<TestRetryableErrorJob>();

        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobTypeId = "test-retryable-error-job",
            Status = JobRunStatus.Pending,
            Queue = "default",
            AttemptNumber = 0 // First attempt
        };

        _storage.Setup(s => s.GetJobDefinitionAsync("test-retryable-error-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.UpdateRunAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _executor.ExecuteAsync(run);
        await ProcessEventsAsync();

        // Assert
        _eventCollector.StartedEvents.Should().HaveCount(1);

        _eventCollector.RetryingEvents.Should().HaveCount(1);
        _eventCollector.RetryingEvents[0].RunId.Should().Be(run.Id);
        _eventCollector.RetryingEvents[0].FailedAttempt.Should().Be(1);
        _eventCollector.RetryingEvents[0].NextAttempt.Should().Be(2);
        _eventCollector.RetryingEvents[0].Delay.Should().BeGreaterThan(TimeSpan.Zero);

        _eventCollector.CompletedEvents.Should().BeEmpty();
        _eventCollector.FailedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_DispatchesFailedEvent()
    {
        // Arrange
        _executor.RegisterJobType<TestSlowJob>();

        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobTypeId = "test-slow-job",
            Status = JobRunStatus.Pending,
            Queue = "default"
        };

        var definition = new JobDefinition
        {
            JobTypeId = "test-slow-job",
            TimeoutSeconds = 1 // 1 second timeout
        };

        _storage.Setup(s => s.GetJobDefinitionAsync("test-slow-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);
        _storage.Setup(s => s.UpdateRunAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _executor.ExecuteAsync(run);
        await ProcessEventsAsync();

        // Assert
        _eventCollector.StartedEvents.Should().HaveCount(1);

        _eventCollector.FailedEvents.Should().HaveCount(1);
        _eventCollector.FailedEvents[0].ErrorMessage.Should().Contain("timed out");
        _eventCollector.FailedEvents[0].MovedToDeadLetter.Should().BeTrue();

        _eventCollector.CompletedEvents.Should().BeEmpty();
    }

    // Event collector for testing
    private class TestEventCollector :
        IJobEventHandler<JobStartedEvent>,
        IJobEventHandler<JobCompletedEvent>,
        IJobEventHandler<JobFailedEvent>,
        IJobEventHandler<JobRetryingEvent>
    {
        public List<JobStartedEvent> StartedEvents { get; } = new();
        public List<JobCompletedEvent> CompletedEvents { get; } = new();
        public List<JobFailedEvent> FailedEvents { get; } = new();
        public List<JobRetryingEvent> RetryingEvents { get; } = new();

        public Task HandleAsync(JobStartedEvent @event, CancellationToken ct = default)
        {
            StartedEvents.Add(@event);
            return Task.CompletedTask;
        }

        public Task HandleAsync(JobCompletedEvent @event, CancellationToken ct = default)
        {
            CompletedEvents.Add(@event);
            return Task.CompletedTask;
        }

        public Task HandleAsync(JobFailedEvent @event, CancellationToken ct = default)
        {
            FailedEvents.Add(@event);
            return Task.CompletedTask;
        }

        public Task HandleAsync(JobRetryingEvent @event, CancellationToken ct = default)
        {
            RetryingEvents.Add(@event);
            return Task.CompletedTask;
        }
    }

    // Test job implementations
    private class TestSuccessJob : IJob
    {
        public string JobTypeId => "test-success-job";

        public Task ExecuteAsync(JobExecutionContext context, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private class TestFailingJob : IJob
    {
        public string JobTypeId => "test-failing-job";

        public Task ExecuteAsync(JobExecutionContext context, CancellationToken ct)
        {
            throw new InvalidOperationException("Job failed intentionally");
        }
    }

    private class TestRetryableErrorJob : IJob
    {
        public string JobTypeId => "test-retryable-error-job";

        public Task ExecuteAsync(JobExecutionContext context, CancellationToken ct)
        {
            throw new HttpRequestException("Network error - should retry");
        }
    }

    private class TestSlowJob : IJob
    {
        public string JobTypeId => "test-slow-job";

        public async Task ExecuteAsync(JobExecutionContext context, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
