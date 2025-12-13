using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ZapJobs.Core;
using ZapJobs.Execution;

namespace ZapJobs.Tests.Execution;

public class RetryHandlerTests
{
    private readonly Mock<ILogger<RetryHandler>> _logger;
    private readonly RetryHandler _handler;

    public RetryHandlerTests()
    {
        _logger = new Mock<ILogger<RetryHandler>>();
        _handler = new RetryHandler(_logger.Object);
    }

    [Fact]
    public void ShouldRetry_FirstAttempt_ReturnsTrue()
    {
        // Arrange
        var exception = new Exception("Test error");
        var policy = new RetryPolicy { MaxRetries = 3 };

        // Act
        var result = _handler.ShouldRetry(exception, currentAttempt: 1, policy);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_MaxRetriesExceeded_ReturnsFalse()
    {
        // Arrange
        var exception = new Exception("Test error");
        var policy = new RetryPolicy { MaxRetries = 3 };

        // Act
        var result = _handler.ShouldRetry(exception, currentAttempt: 3, policy);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRetry_NonRetryableException_ReturnsFalse()
    {
        // Arrange
        var exception = new ArgumentException("Invalid argument");
        var policy = new RetryPolicy { MaxRetries = 5 };

        // Act
        var result = _handler.ShouldRetry(exception, currentAttempt: 1, policy);

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
    public void IsRetryableException_NonRetryableTypes_ReturnsFalse(Type exceptionType)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType, "Test")!;
        var policy = new RetryPolicy { MaxRetries = 5 };

        // Act
        var result = _handler.ShouldRetry(exception, currentAttempt: 1, policy);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsRetryableException_HttpException_ReturnsTrue()
    {
        // Arrange
        var exception = new HttpRequestException("Network error");
        var policy = new RetryPolicy { MaxRetries = 5 };

        // Act
        var result = _handler.ShouldRetry(exception, currentAttempt: 1, policy);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsRetryableException_TimeoutException_ReturnsTrue()
    {
        // Arrange
        var exception = new TimeoutException("Operation timed out");
        var policy = new RetryPolicy { MaxRetries = 5 };

        // Act
        var result = _handler.ShouldRetry(exception, currentAttempt: 1, policy);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsRetryableException_IOException_ReturnsTrue()
    {
        // Arrange
        var exception = new IOException("IO error");
        var policy = new RetryPolicy { MaxRetries = 5 };

        // Act
        var result = _handler.ShouldRetry(exception, currentAttempt: 1, policy);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CalculateDelay_DelegatesToPolicy()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(10),
            BackoffMultiplier = 2.0,
            UseJitter = false
        };

        // Act
        var delay = _handler.CalculateDelay(attemptNumber: 1, policy);

        // Assert
        delay.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void CalculateDelay_MultipleCalls_RespectsBackoff()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(5),
            BackoffMultiplier = 2.0,
            UseJitter = false
        };

        // Act & Assert
        _handler.CalculateDelay(1, policy).Should().Be(TimeSpan.FromSeconds(5));
        _handler.CalculateDelay(2, policy).Should().Be(TimeSpan.FromSeconds(10));
        _handler.CalculateDelay(3, policy).Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void ShouldRetry_CustomNonRetryableException_ReturnsFalse()
    {
        // Arrange
        var policy = new RetryPolicy { MaxRetries = 5 };
        policy.NonRetryableExceptions.Add(typeof(CustomNonRetryableException));
        var exception = new CustomNonRetryableException("Custom error");

        // Act
        var result = _handler.ShouldRetry(exception, currentAttempt: 1, policy);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRetry_DerivedNonRetryableException_ReturnsFalse()
    {
        // Arrange
        var policy = new RetryPolicy { MaxRetries = 5 };
        // ArgumentNullException derives from ArgumentException
        var exception = new ArgumentNullException("param", "Value is null");

        // Act
        var result = _handler.ShouldRetry(exception, currentAttempt: 1, policy);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRetry_WithZeroMaxRetries_ReturnsFalse()
    {
        // Arrange
        var exception = new Exception("Test error");
        var policy = RetryPolicy.NoRetry;

        // Act
        var result = _handler.ShouldRetry(exception, currentAttempt: 1, policy);

        // Assert
        result.Should().BeFalse();
    }

    private class CustomNonRetryableException : Exception
    {
        public CustomNonRetryableException(string message) : base(message) { }
    }
}
