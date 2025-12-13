using System.Text.Json;
using FluentAssertions;
using Moq;
using ZapJobs.Core;

namespace ZapJobs.Core.Tests.Context;

public class JobExecutionContextTests
{
    private readonly Mock<IServiceProvider> _serviceProvider;
    private readonly Mock<IJobLogger> _jobLogger;

    public JobExecutionContextTests()
    {
        _serviceProvider = new Mock<IServiceProvider>();
        _jobLogger = new Mock<IJobLogger>();
    }

    private JobExecutionContext CreateContext(JsonDocument? inputDocument = null)
    {
        return new JobExecutionContext(
            runId: Guid.NewGuid(),
            jobTypeId: "test-job",
            triggerType: JobTriggerType.Manual,
            triggeredBy: "test-user",
            services: _serviceProvider.Object,
            logger: _jobLogger.Object,
            inputDocument: inputDocument);
    }

    [Fact]
    public void IncrementProcessed_IncreasesCount()
    {
        // Arrange
        var context = CreateContext();

        // Act
        context.IncrementProcessed();
        context.IncrementProcessed(5);

        // Assert
        var metrics = context.GetMetrics();
        metrics.ItemsProcessed.Should().Be(6);
    }

    [Fact]
    public void IncrementSucceeded_IncreasesCount()
    {
        // Arrange
        var context = CreateContext();

        // Act
        context.IncrementSucceeded();
        context.IncrementSucceeded(3);

        // Assert
        var metrics = context.GetMetrics();
        metrics.ItemsSucceeded.Should().Be(4);
    }

    [Fact]
    public void IncrementFailed_IncreasesCount()
    {
        // Arrange
        var context = CreateContext();

        // Act
        context.IncrementFailed();
        context.IncrementFailed(2);

        // Assert
        var metrics = context.GetMetrics();
        metrics.ItemsFailed.Should().Be(3);
    }

    [Fact]
    public void GetMetrics_ReturnsCorrectValues()
    {
        // Arrange
        var context = CreateContext();
        context.IncrementProcessed(10);
        context.IncrementSucceeded(8);
        context.IncrementFailed(2);

        // Act
        var metrics = context.GetMetrics();

        // Assert
        metrics.ItemsProcessed.Should().Be(10);
        metrics.ItemsSucceeded.Should().Be(8);
        metrics.ItemsFailed.Should().Be(2);
    }

    [Fact]
    public void SetOutput_StoresValue()
    {
        // Arrange
        var context = CreateContext();
        var output = new { Result = "Success", Count = 42 };

        // Act
        context.SetOutput(output);

        // Assert
        context.GetOutput().Should().BeEquivalentTo(output);
    }

    [Fact]
    public void UpdateMetrics_SetsAllValues()
    {
        // Arrange
        var context = CreateContext();
        context.IncrementProcessed(100); // Initial value

        // Act
        context.UpdateMetrics(processed: 50, succeeded: 45, failed: 5);

        // Assert
        var metrics = context.GetMetrics();
        metrics.ItemsProcessed.Should().Be(50);
        metrics.ItemsSucceeded.Should().Be(45);
        metrics.ItemsFailed.Should().Be(5);
    }

    [Fact]
    public void GetInput_WithValidJson_DeserializesCorrectly()
    {
        // Arrange
        var input = new TestInput { Name = "Test", Value = 123 };
        var json = JsonSerializer.Serialize(input);
        var doc = JsonDocument.Parse(json);
        var context = CreateContext(doc);

        // Act
        var result = context.GetInput<TestInput>();

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
        result.Value.Should().Be(123);
    }

    [Fact]
    public void GetInput_WithNullDocument_ReturnsNull()
    {
        // Arrange
        var context = CreateContext(null);

        // Act
        var result = context.GetInput<TestInput>();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void As_ReturnsTypedContext()
    {
        // Arrange
        var input = new TestInput { Name = "Test", Value = 456 };
        var json = JsonSerializer.Serialize(input);
        var doc = JsonDocument.Parse(json);
        var context = CreateContext(doc);

        // Act
        var typedContext = context.As<TestInput>();

        // Assert
        typedContext.Should().NotBeNull();
        typedContext.Input.Should().NotBeNull();
        typedContext.Input!.Name.Should().Be("Test");
        typedContext.Input.Value.Should().Be(456);
    }

    [Fact]
    public void TypedContext_ExposesAllProperties()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var context = new JobExecutionContext(
            runId: runId,
            jobTypeId: "test-job-type",
            triggerType: JobTriggerType.Scheduled,
            triggeredBy: "scheduler",
            services: _serviceProvider.Object,
            logger: _jobLogger.Object);
        var typedContext = context.As<TestInput>();

        // Assert
        typedContext.RunId.Should().Be(runId);
        typedContext.JobTypeId.Should().Be("test-job-type");
        typedContext.TriggerType.Should().Be(JobTriggerType.Scheduled);
        typedContext.TriggeredBy.Should().Be("scheduler");
        typedContext.Services.Should().Be(_serviceProvider.Object);
        typedContext.Logger.Should().Be(_jobLogger.Object);
    }

    [Fact]
    public void TypedContext_MetricsOperationsAffectInnerContext()
    {
        // Arrange
        var context = CreateContext();
        var typedContext = context.As<TestInput>();

        // Act
        typedContext.IncrementProcessed(10);
        typedContext.IncrementSucceeded(8);
        typedContext.IncrementFailed(2);

        // Assert - inner context should reflect changes
        var innerMetrics = context.GetMetrics();
        innerMetrics.ItemsProcessed.Should().Be(10);
        innerMetrics.ItemsSucceeded.Should().Be(8);
        innerMetrics.ItemsFailed.Should().Be(2);

        // And typed context should also see them
        var typedMetrics = typedContext.GetMetrics();
        typedMetrics.Should().BeEquivalentTo(innerMetrics);
    }

    [Fact]
    public void TypedContext_SetOutput_AffectsInnerContext()
    {
        // Arrange
        var context = CreateContext();
        var typedContext = context.As<TestInput>();
        var output = "test output";

        // Act
        typedContext.SetOutput(output);

        // Assert
        context.GetOutput().Should().Be(output);
    }

    [Fact]
    public void ConcurrentIncrements_AreThreadSafe()
    {
        // Arrange
        var context = CreateContext();
        var iterations = 1000;

        // Act
        Parallel.For(0, iterations, _ =>
        {
            context.IncrementProcessed();
            context.IncrementSucceeded();
            context.IncrementFailed();
        });

        // Assert
        var metrics = context.GetMetrics();
        metrics.ItemsProcessed.Should().Be(iterations);
        metrics.ItemsSucceeded.Should().Be(iterations);
        metrics.ItemsFailed.Should().Be(iterations);
    }

    private class TestInput
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
