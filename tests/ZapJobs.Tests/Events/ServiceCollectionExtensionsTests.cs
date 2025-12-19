using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZapJobs.Core.Events;

namespace ZapJobs.Tests.Events;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddZapJobs_RegistersEventDispatcher()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Add required services manually for testing
        services.AddSingleton<ZapJobs.Core.IJobStorage>(new Moq.Mock<ZapJobs.Core.IJobStorage>().Object);

        // Act
        services.AddZapJobs();
        var provider = services.BuildServiceProvider();

        // Assert
        var dispatcher = provider.GetService<IJobEventDispatcher>();
        dispatcher.Should().NotBeNull();
    }

    [Fact]
    public void AddEventHandler_RegistersHandlerForSingleEvent()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ZapJobs.Core.IJobStorage>(new Moq.Mock<ZapJobs.Core.IJobStorage>().Object);

        // Act
        services.AddZapJobs()
            .AddEventHandler<SingleEventHandler>();

        var provider = services.BuildServiceProvider();

        // Assert
        var handlers = provider.GetServices<IJobEventHandler<JobCompletedEvent>>();
        handlers.Should().HaveCount(1);
        handlers.First().Should().BeOfType<SingleEventHandler>();
    }

    [Fact]
    public void AddEventHandler_RegistersHandlerForMultipleEvents()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ZapJobs.Core.IJobStorage>(new Moq.Mock<ZapJobs.Core.IJobStorage>().Object);

        // Act
        services.AddZapJobs()
            .AddEventHandler<MultiEventHandler>();

        var provider = services.BuildServiceProvider();

        // Assert
        var startedHandlers = provider.GetServices<IJobEventHandler<JobStartedEvent>>();
        var completedHandlers = provider.GetServices<IJobEventHandler<JobCompletedEvent>>();
        var failedHandlers = provider.GetServices<IJobEventHandler<JobFailedEvent>>();

        startedHandlers.Should().HaveCount(1);
        completedHandlers.Should().HaveCount(1);
        failedHandlers.Should().HaveCount(1);
    }

    [Fact]
    public void AddEventHandler_InvalidHandler_ThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ZapJobs.Core.IJobStorage>(new Moq.Mock<ZapJobs.Core.IJobStorage>().Object);

        // Act
        var builder = services.AddZapJobs();
        var act = () => builder.AddEventHandler<NotAHandler>();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*does not implement any IJobEventHandler*");
    }

    [Fact]
    public void AddEventHandler_MultipleHandlers_AllRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ZapJobs.Core.IJobStorage>(new Moq.Mock<ZapJobs.Core.IJobStorage>().Object);

        // Act
        services.AddZapJobs()
            .AddEventHandler<SingleEventHandler>()
            .AddEventHandler<AnotherCompletedHandler>();

        var provider = services.BuildServiceProvider();

        // Assert
        var handlers = provider.GetServices<IJobEventHandler<JobCompletedEvent>>();
        handlers.Should().HaveCount(2);
    }

    // Test handler implementations
    private class SingleEventHandler : IJobEventHandler<JobCompletedEvent>
    {
        public Task HandleAsync(JobCompletedEvent @event, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }

    private class AnotherCompletedHandler : IJobEventHandler<JobCompletedEvent>
    {
        public Task HandleAsync(JobCompletedEvent @event, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }

    private class MultiEventHandler :
        IJobEventHandler<JobStartedEvent>,
        IJobEventHandler<JobCompletedEvent>,
        IJobEventHandler<JobFailedEvent>
    {
        public Task HandleAsync(JobStartedEvent @event, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task HandleAsync(JobCompletedEvent @event, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task HandleAsync(JobFailedEvent @event, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }

    private class NotAHandler
    {
        // Does not implement IJobEventHandler
    }
}
