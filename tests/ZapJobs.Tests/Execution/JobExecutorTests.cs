using Xunit;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ZapJobs.Core;
using ZapJobs.Execution;
using ZapJobs.Tracking;

namespace ZapJobs.Tests.Execution;

public class JobExecutorTests
{
    private readonly Mock<IJobStorage> _storage;
    private readonly Mock<IJobLoggerFactory> _loggerFactory;
    private readonly Mock<IJobLogger> _jobLogger;
    private readonly Mock<ILogger<JobExecutor>> _logger;
    private readonly Mock<ILogger<RetryHandler>> _retryLogger;
    private readonly ZapJobsOptions _options;
    private readonly ServiceCollection _services;
    private readonly IServiceProvider _serviceProvider;
    private readonly JobExecutor _executor;

    public JobExecutorTests()
    {
        _storage = new Mock<IJobStorage>();
        _loggerFactory = new Mock<IJobLoggerFactory>();
        _jobLogger = new Mock<IJobLogger>();
        _logger = new Mock<ILogger<JobExecutor>>();
        _retryLogger = new Mock<ILogger<RetryHandler>>();
        _options = new ZapJobsOptions();

        _loggerFactory.Setup(f => f.CreateLogger(It.IsAny<Guid>()))
            .Returns(_jobLogger.Object);

        _jobLogger.Setup(l => l.InfoAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);
        _jobLogger.Setup(l => l.ErrorAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<Exception?>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);
        _jobLogger.Setup(l => l.WarningAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);

        _services = new ServiceCollection();
        _services.AddSingleton(_storage.Object);
        _services.AddSingleton(_loggerFactory.Object);
        _services.AddSingleton(Options.Create(_options));

        _serviceProvider = _services.BuildServiceProvider();

        var retryHandler = new RetryHandler(_retryLogger.Object);

        _executor = new JobExecutor(
            _serviceProvider,
            _storage.Object,
            _loggerFactory.Object,
            retryHandler,
            Options.Create(_options),
            _logger.Object);
    }

    [Fact]
    public void RegisterJobType_AddsToRegistry()
    {
        // Act
        _executor.RegisterJobType<TestSuccessJob>();

        // Assert
        _executor.GetRegisteredJobTypes().Should().Contain("test-success-job");
    }

    [Fact]
    public void RegisterJobType_MultipleJobs_AllRegistered()
    {
        // Act
        _executor.RegisterJobType<TestSuccessJob>();
        _executor.RegisterJobType<TestFailingJob>();

        // Assert
        var registered = _executor.GetRegisteredJobTypes();
        registered.Should().HaveCount(2);
        registered.Should().Contain("test-success-job");
        registered.Should().Contain("test-failing-job");
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulJob_SetsCompletedStatus()
    {
        // Arrange
        _executor.RegisterJobType<TestSuccessJob>();

        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobTypeId = "test-success-job",
            Status = JobRunStatus.Pending
        };

        _storage.Setup(s => s.GetJobDefinitionAsync("test-success-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.UpdateRunAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _executor.ExecuteAsync(run);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        run.Status.Should().Be(JobRunStatus.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_FailingJob_SetsFailedStatus()
    {
        // Arrange
        _executor.RegisterJobType<TestFailingJob>();

        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobTypeId = "test-failing-job",
            Status = JobRunStatus.Pending,
            AttemptNumber = 3 // Max retries reached
        };

        _storage.Setup(s => s.GetJobDefinitionAsync("test-failing-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.UpdateRunAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _executor.ExecuteAsync(run);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Job failed intentionally");
        run.Status.Should().Be(JobRunStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_SetsFailedStatus()
    {
        // Arrange
        _executor.RegisterJobType<TestSlowJob>();

        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobTypeId = "test-slow-job",
            Status = JobRunStatus.Pending
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
        var result = await _executor.ExecuteAsync(run);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("timed out");
        run.Status.Should().Be(JobRunStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_RetryableError_SchedulesRetry()
    {
        // Arrange
        _executor.RegisterJobType<TestRetryableErrorJob>();

        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobTypeId = "test-retryable-error-job",
            Status = JobRunStatus.Pending,
            AttemptNumber = 0 // First attempt
        };

        _storage.Setup(s => s.GetJobDefinitionAsync("test-retryable-error-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.UpdateRunAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _executor.ExecuteAsync(run);

        // Assert
        result.Success.Should().BeFalse();
        result.WillRetry.Should().BeTrue();
        result.NextRetryAt.Should().NotBeNull();
        run.Status.Should().Be(JobRunStatus.AwaitingRetry);
    }

    [Fact]
    public async Task ExecuteAsync_MaxRetries_FailsPermanently()
    {
        // Arrange
        _executor.RegisterJobType<TestRetryableErrorJob>();

        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobTypeId = "test-retryable-error-job",
            Status = JobRunStatus.Pending,
            AttemptNumber = 3 // Already at max retries
        };

        _storage.Setup(s => s.GetJobDefinitionAsync("test-retryable-error-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.UpdateRunAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _executor.ExecuteAsync(run);

        // Assert
        result.Success.Should().BeFalse();
        result.WillRetry.Should().BeFalse();
        run.Status.Should().Be(JobRunStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownJobType_ReturnsError()
    {
        // Arrange
        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobTypeId = "unknown-job",
            Status = JobRunStatus.Pending
        };

        // Act
        var result = await _executor.ExecuteAsync(run);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unknown job type");
    }

    [Fact]
    public async Task ExecuteAsync_WithInput_PassesInputToJob()
    {
        // Arrange
        _executor.RegisterJobType<TestInputJob>();

        var input = new TestInput { Message = "Hello World" };
        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobTypeId = "test-input-job",
            Status = JobRunStatus.Pending,
            InputJson = JsonSerializer.Serialize(input)
        };

        _storage.Setup(s => s.GetJobDefinitionAsync("test-input-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.UpdateRunAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _executor.ExecuteAsync(run);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SetsMetricsFromContext()
    {
        // Arrange
        _executor.RegisterJobType<TestMetricsJob>();

        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobTypeId = "test-metrics-job",
            Status = JobRunStatus.Pending
        };

        _storage.Setup(s => s.GetJobDefinitionAsync("test-metrics-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.UpdateRunAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _executor.ExecuteAsync(run);

        // Assert
        run.ItemsProcessed.Should().Be(10);
        run.ItemsSucceeded.Should().Be(8);
        run.ItemsFailed.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_SetsOutputFromContext()
    {
        // Arrange
        _executor.RegisterJobType<TestOutputJob>();

        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobTypeId = "test-output-job",
            Status = JobRunStatus.Pending
        };

        _storage.Setup(s => s.GetJobDefinitionAsync("test-output-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.UpdateRunAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _executor.ExecuteAsync(run);

        // Assert
        result.OutputJson.Should().NotBeNull();
        result.OutputJson.Should().Contain("Success");
    }

    [Fact]
    public async Task ExecuteAsync_IncrementsAttemptNumber()
    {
        // Arrange
        _executor.RegisterJobType<TestSuccessJob>();

        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobTypeId = "test-success-job",
            Status = JobRunStatus.Pending,
            AttemptNumber = 1
        };

        _storage.Setup(s => s.GetJobDefinitionAsync("test-success-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.UpdateRunAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _executor.ExecuteAsync(run);

        // Assert
        run.AttemptNumber.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_SetsProgress100OnCompletion()
    {
        // Arrange
        _executor.RegisterJobType<TestSuccessJob>();

        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobTypeId = "test-success-job",
            Status = JobRunStatus.Pending
        };

        _storage.Setup(s => s.GetJobDefinitionAsync("test-success-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDefinition?)null);
        _storage.Setup(s => s.UpdateRunAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _executor.ExecuteAsync(run);

        // Assert
        run.Progress.Should().Be(100);
    }

    [Fact]
    public void RegisterJobType_ByType_RegistersCorrectly()
    {
        // Act
        _executor.RegisterJobType(typeof(TestSuccessJob));

        // Assert
        _executor.GetRegisteredJobTypes().Should().Contain("test-success-job");
    }

    [Fact]
    public void RegisterJobType_InvalidType_ThrowsArgumentException()
    {
        // Act
        var act = () => _executor.RegisterJobType(typeof(string));

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*does not implement IJob*");
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

    private class TestSlowJob : IJob
    {
        public string JobTypeId => "test-slow-job";

        public async Task ExecuteAsync(JobExecutionContext context, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
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

    private class TestInput
    {
        public string Message { get; set; } = string.Empty;
    }

    private class TestInputJob : IJob
    {
        public string JobTypeId => "test-input-job";

        public Task ExecuteAsync(JobExecutionContext context, CancellationToken ct)
        {
            var input = context.GetInput<TestInput>();
            if (input?.Message != "Hello World")
                throw new Exception("Input not received correctly");
            return Task.CompletedTask;
        }
    }

    private class TestMetricsJob : IJob
    {
        public string JobTypeId => "test-metrics-job";

        public Task ExecuteAsync(JobExecutionContext context, CancellationToken ct)
        {
            context.IncrementProcessed(10);
            context.IncrementSucceeded(8);
            context.IncrementFailed(2);
            return Task.CompletedTask;
        }
    }

    private class TestOutputJob : IJob
    {
        public string JobTypeId => "test-output-job";

        public Task ExecuteAsync(JobExecutionContext context, CancellationToken ct)
        {
            context.SetOutput(new { Result = "Success", Count = 42 });
            return Task.CompletedTask;
        }
    }
}
