using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ZapJobs.Core;
using ZapJobs.Scheduling;

namespace ZapJobs.Tests.Scheduling;

public class CronSchedulerTests
{
    private readonly CronScheduler _scheduler;

    public CronSchedulerTests()
    {
        var options = Options.Create(new ZapJobsOptions { DefaultTimeZoneId = null });
        _scheduler = new CronScheduler(options);
    }

    [Theory]
    [InlineData("* * * * *")]           // Every minute
    [InlineData("0 * * * *")]           // Every hour
    [InlineData("0 0 * * *")]           // Every day at midnight
    [InlineData("0 8 * * 1-5")]         // Weekdays at 8 AM
    [InlineData("*/5 * * * *")]         // Every 5 minutes
    [InlineData("0 0 1 * *")]           // First day of month
    [InlineData("0 0 * * 0")]           // Every Sunday
    [InlineData("0 0 0 * * *")]         // Every minute (6-part with seconds)
    public void IsValidExpression_ValidCron_ReturnsTrue(string cronExpression)
    {
        // Act
        var result = _scheduler.IsValidExpression(cronExpression);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("* * *")]               // Too few parts
    [InlineData("60 * * * *")]          // Invalid minute (60)
    [InlineData("* 25 * * *")]          // Invalid hour (25)
    [InlineData("* * 32 * *")]          // Invalid day (32)
    [InlineData("* * * 13 *")]          // Invalid month (13)
    [InlineData("* * * * 8")]           // Invalid day of week (8)
    [InlineData("")]
    public void IsValidExpression_InvalidCron_ReturnsFalse(string cronExpression)
    {
        // Act
        var result = _scheduler.IsValidExpression(cronExpression);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetNextOccurrence_ReturnsCorrectTime()
    {
        // Arrange
        var fromUtc = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var cronExpression = "0 11 * * *"; // Every day at 11:00

        // Act
        var result = _scheduler.GetNextOccurrence(cronExpression, fromUtc);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Hour.Should().Be(11);
        result.Value.Minute.Should().Be(0);
        result.Value.Day.Should().Be(15); // Same day since it's before 11:00
    }

    [Fact]
    public void GetNextOccurrence_AfterTime_ReturnsNextDay()
    {
        // Arrange
        var fromUtc = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var cronExpression = "0 10 * * *"; // Every day at 10:00

        // Act
        var result = _scheduler.GetNextOccurrence(cronExpression, fromUtc);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Hour.Should().Be(10);
        result.Value.Day.Should().Be(16); // Next day since time passed
    }

    [Fact]
    public void GetNextOccurrence_WithTimezone_RespectsTimezone()
    {
        // Arrange
        var fromUtc = new DateTime(2024, 6, 15, 14, 0, 0, DateTimeKind.Utc);
        var cronExpression = "0 12 * * *"; // Every day at 12:00 local time
        var pacificTz = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        // Act
        var result = _scheduler.GetNextOccurrence(cronExpression, fromUtc, pacificTz);

        // Assert
        result.Should().NotBeNull();
        // 12:00 PDT = 19:00 UTC (summer time)
        result!.Value.Hour.Should().Be(19);
    }

    [Fact]
    public void GetNextOccurrence_WeekdaysOnly_SkipsWeekends()
    {
        // Arrange - Saturday Jan 13, 2024
        var fromUtc = new DateTime(2024, 1, 13, 10, 0, 0, DateTimeKind.Utc);
        var cronExpression = "0 9 * * 1-5"; // Weekdays at 9:00

        // Act
        var result = _scheduler.GetNextOccurrence(cronExpression, fromUtc);

        // Assert
        result.Should().NotBeNull();
        result!.Value.DayOfWeek.Should().Be(DayOfWeek.Monday);
        result.Value.Day.Should().Be(15); // Monday Jan 15, 2024
    }

    [Fact]
    public void GetNextOccurrences_ReturnsMultipleOccurrences()
    {
        // Arrange
        var fromUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2024, 1, 31, 23, 59, 59, DateTimeKind.Utc);
        var cronExpression = "0 0 * * 0"; // Every Sunday at midnight

        // Act
        var results = _scheduler.GetNextOccurrences(cronExpression, fromUtc, toUtc).ToList();

        // Assert
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(d => d.DayOfWeek.Should().Be(DayOfWeek.Sunday));
    }

    [Fact]
    public void GetDescription_ReturnsNonEmptyString()
    {
        // Arrange
        var cronExpression = "0 8 * * *";

        // Act
        var description = _scheduler.GetDescription(cronExpression);

        // Assert
        description.Should().NotBeNullOrEmpty();
        description.Should().Contain("Next:");
    }

    [Fact]
    public void GetNextOccurrence_WithSeconds_ParsesCorrectly()
    {
        // Arrange
        var fromUtc = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var cronExpression = "30 * * * * *"; // Every minute at 30 seconds

        // Act
        var result = _scheduler.GetNextOccurrence(cronExpression, fromUtc);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Second.Should().Be(30);
    }

    [Fact]
    public void GetNextOccurrence_EveryFiveMinutes_ReturnsCorrectInterval()
    {
        // Arrange
        var fromUtc = new DateTime(2024, 1, 15, 10, 7, 0, DateTimeKind.Utc);
        var cronExpression = "*/5 * * * *"; // Every 5 minutes

        // Act
        var result = _scheduler.GetNextOccurrence(cronExpression, fromUtc);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Minute.Should().Be(10); // Next 5-minute mark after :07
    }

    [Fact]
    public void GetNextOccurrence_MonthlyOnFirst_ReturnsFirstOfMonth()
    {
        // Arrange
        var fromUtc = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var cronExpression = "0 0 1 * *"; // First of every month at midnight

        // Act
        var result = _scheduler.GetNextOccurrence(cronExpression, fromUtc);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Day.Should().Be(1);
        result.Value.Month.Should().Be(2); // Next month since we're past the 1st
    }

    [Fact]
    public void DefaultTimezone_IsUtc()
    {
        // Arrange
        var options = Options.Create(new ZapJobsOptions());
        var scheduler = new CronScheduler(options);
        var fromUtc = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        // Act
        var result = scheduler.GetNextOccurrence("0 12 * * *", fromUtc);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Hour.Should().Be(12); // Should be UTC
    }
}
