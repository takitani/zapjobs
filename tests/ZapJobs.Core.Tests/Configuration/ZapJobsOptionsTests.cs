using Xunit;
using FluentAssertions;
using ZapJobs.Core;

namespace ZapJobs.Core.Tests.Configuration;

public class ZapJobsOptionsTests
{
    [Fact]
    public void DefaultOptions_HasExpectedValues()
    {
        // Act
        var options = new ZapJobsOptions();

        // Assert
        options.WorkerCount.Should().Be(Environment.ProcessorCount);
        options.DefaultQueue.Should().Be("default");
        options.Queues.Should().BeEquivalentTo(["critical", "default", "low"]);
        options.PollingInterval.Should().Be(TimeSpan.FromSeconds(15));
        options.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(30));
        options.StaleWorkerThreshold.Should().Be(TimeSpan.FromMinutes(5));
        options.DefaultTimeout.Should().Be(TimeSpan.FromMinutes(60));
        options.JobRetention.Should().Be(TimeSpan.FromDays(30));
        options.LogRetention.Should().Be(TimeSpan.FromDays(7));
        options.EnableScheduler.Should().BeTrue();
        options.EnableProcessing.Should().BeTrue();
        options.StorageRetryCount.Should().Be(3);
        options.StorageRetryDelay.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetDefaultTimeZone_NullTimeZoneId_ReturnsUtc()
    {
        // Arrange
        var options = new ZapJobsOptions { DefaultTimeZoneId = null };

        // Act
        var tz = options.GetDefaultTimeZone();

        // Assert
        tz.Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public void GetDefaultTimeZone_EmptyTimeZoneId_ReturnsUtc()
    {
        // Arrange
        var options = new ZapJobsOptions { DefaultTimeZoneId = "" };

        // Act
        var tz = options.GetDefaultTimeZone();

        // Assert
        tz.Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public void GetDefaultTimeZone_ValidTimeZoneId_ReturnsConfiguredTimeZone()
    {
        // Arrange
        var options = new ZapJobsOptions { DefaultTimeZoneId = "UTC" };

        // Act
        var tz = options.GetDefaultTimeZone();

        // Assert
        tz.Id.Should().Be("UTC");
    }

    [Fact]
    public void GetDefaultTimeZone_InvalidTimeZoneId_ThrowsException()
    {
        // Arrange
        var options = new ZapJobsOptions { DefaultTimeZoneId = "Invalid/TimeZone" };

        // Act
        var act = () => options.GetDefaultTimeZone();

        // Assert
        act.Should().Throw<TimeZoneNotFoundException>();
    }

    [Fact]
    public void SectionName_IsZapJobs()
    {
        // Assert
        ZapJobsOptions.SectionName.Should().Be("ZapJobs");
    }

    [Fact]
    public void DefaultRetryPolicy_IsNotNull()
    {
        // Arrange
        var options = new ZapJobsOptions();

        // Assert
        options.DefaultRetryPolicy.Should().NotBeNull();
        options.DefaultRetryPolicy.MaxRetries.Should().Be(3);
    }

    [Fact]
    public void WorkerId_CanBeConfigured()
    {
        // Arrange
        var options = new ZapJobsOptions { WorkerId = "worker-123" };

        // Assert
        options.WorkerId.Should().Be("worker-123");
    }

    [Fact]
    public void CustomQueues_CanBeConfigured()
    {
        // Arrange
        var options = new ZapJobsOptions
        {
            Queues = ["high", "medium", "low", "background"]
        };

        // Assert
        options.Queues.Should().HaveCount(4);
        options.Queues.Should().BeEquivalentTo(["high", "medium", "low", "background"]);
    }
}
