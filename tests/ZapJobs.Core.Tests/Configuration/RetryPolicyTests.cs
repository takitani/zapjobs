using Xunit;
using FluentAssertions;
using ZapJobs.Core;

namespace ZapJobs.Core.Tests.Configuration;

public class RetryPolicyTests
{
    [Fact]
    public void CalculateDelay_FirstAttempt_ReturnsInitialDelay()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(30),
            BackoffMultiplier = 2.0,
            UseJitter = false
        };

        // Act
        var delay = policy.CalculateDelay(attemptNumber: 1);

        // Assert
        delay.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(2, 60)]   // 30 * 2^1 = 60
    [InlineData(3, 120)]  // 30 * 2^2 = 120
    [InlineData(4, 240)]  // 30 * 2^3 = 240
    public void CalculateDelay_WithBackoff_IncreasesExponentially(int attempt, double expectedSeconds)
    {
        // Arrange
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(30),
            BackoffMultiplier = 2.0,
            UseJitter = false
        };

        // Act
        var delay = policy.CalculateDelay(attempt);

        // Assert
        delay.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void CalculateDelay_WithJitter_VariesWithinRange()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(100),
            BackoffMultiplier = 1.0,
            UseJitter = true,
            JitterFactor = 0.2
        };

        // Act - run multiple times to verify jitter variance
        var delays = Enumerable.Range(0, 100)
            .Select(_ => policy.CalculateDelay(1))
            .ToList();

        // Assert - delays should vary but stay within ±20% of base
        var baseDelay = TimeSpan.FromSeconds(100);
        var minExpected = baseDelay - TimeSpan.FromSeconds(20);
        var maxExpected = baseDelay + TimeSpan.FromSeconds(20);

        delays.Should().AllSatisfy(d =>
        {
            d.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1)); // minimum positive
            d.Should().BeLessThanOrEqualTo(maxExpected);
        });

        // Verify there's actual variance (not all the same)
        delays.Distinct().Count().Should().BeGreaterThan(1);
    }

    [Fact]
    public void CalculateDelay_ExceedsMaxDelay_CapsAtMaxDelay()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromMinutes(10),
            BackoffMultiplier = 10.0,
            MaxDelay = TimeSpan.FromHours(1),
            UseJitter = false
        };

        // Act - attempt 5: 10 * 10^4 = 100,000 minutes, should cap at 1 hour
        var delay = policy.CalculateDelay(5);

        // Assert
        delay.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void NoRetry_ReturnsZeroMaxRetries()
    {
        // Act
        var policy = RetryPolicy.NoRetry;

        // Assert
        policy.MaxRetries.Should().Be(0);
    }

    [Fact]
    public void Fixed_CreatesFixedDelayPolicy()
    {
        // Act
        var policy = RetryPolicy.Fixed(maxRetries: 5, delay: TimeSpan.FromSeconds(10));

        // Assert
        policy.MaxRetries.Should().Be(5);
        policy.InitialDelay.Should().Be(TimeSpan.FromSeconds(10));
        policy.BackoffMultiplier.Should().Be(1.0);
        policy.UseJitter.Should().BeFalse();

        // Verify delays are constant
        policy.CalculateDelay(1).Should().Be(TimeSpan.FromSeconds(10));
        policy.CalculateDelay(3).Should().Be(TimeSpan.FromSeconds(10));
        policy.CalculateDelay(5).Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Exponential_CreatesExponentialBackoffPolicy()
    {
        // Act
        var policy = RetryPolicy.Exponential(
            maxRetries: 5,
            initialDelay: TimeSpan.FromSeconds(2),
            multiplier: 3.0);

        // Assert
        policy.MaxRetries.Should().Be(5);
        policy.InitialDelay.Should().Be(TimeSpan.FromSeconds(2));
        policy.BackoffMultiplier.Should().Be(3.0);
        policy.UseJitter.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_FirstAttempt_ReturnsTrue()
    {
        // Arrange
        var policy = new RetryPolicy { MaxRetries = 3 };
        var exception = new Exception("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_MaxRetriesExceeded_ReturnsFalse()
    {
        // Arrange
        var policy = new RetryPolicy { MaxRetries = 3 };
        var exception = new Exception("Test error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 3);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(ArgumentNullException))]
    [InlineData(typeof(ArgumentOutOfRangeException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(NotImplementedException))]
    public void ShouldRetry_NonRetryableException_ReturnsFalse(Type exceptionType)
    {
        // Arrange
        var policy = new RetryPolicy { MaxRetries = 5 };
        var exception = (Exception)Activator.CreateInstance(exceptionType, "Test")!;

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRetry_RetryableException_ReturnsTrue()
    {
        // Arrange
        var policy = new RetryPolicy { MaxRetries = 5 };
        var exception = new HttpRequestException("Network error");

        // Act
        var result = policy.ShouldRetry(exception, attemptNumber: 1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void DefaultPolicy_HasExpectedValues()
    {
        // Act
        var policy = new RetryPolicy();

        // Assert
        policy.MaxRetries.Should().Be(3);
        policy.InitialDelay.Should().Be(TimeSpan.FromSeconds(30));
        policy.BackoffMultiplier.Should().Be(2.0);
        policy.MaxDelay.Should().Be(TimeSpan.FromHours(1));
        policy.UseJitter.Should().BeTrue();
        policy.JitterFactor.Should().Be(0.2);
        policy.NonRetryableExceptions.Should().Contain(typeof(ArgumentException));
    }
}
