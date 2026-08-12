using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TradingBot.Application.Configuration;
using TradingBot.Application.Enums;
using TradingBot.Application.Exceptions;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Services;
using Xunit;

namespace TradingBot.UnitTests.Reliability;

public class CircuitBreakerTests
{
    private readonly ReliabilityOptions _options;
    private readonly Mock<IErrorClassifier> _errorClassifierMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly Mock<ILogger<CircuitBreaker>> _loggerMock = new();

    public CircuitBreakerTests()
    {
        _options = new ReliabilityOptions
        {
            CircuitBreaker = new CircuitBreakerSettings
            {
                Enabled = true,
                FailureThreshold = 3,
                BreakDurationSeconds = 0.1, // fast break duration
                HalfOpenProbeCount = 2
            }
        };

        // By default, classify TimeoutException as retryable (transient)
        _errorClassifierMock
            .Setup(c => c.Classify(It.IsAny<TimeoutException>()))
            .Returns(ErrorRetryability.Retryable);

        // By default, classify InvalidOperationException as non-retryable (business)
        _errorClassifierMock
            .Setup(c => c.Classify(It.IsAny<InvalidOperationException>()))
            .Returns(ErrorRetryability.NonRetryable);
    }

    [Fact]
    public void ClosedState_ShouldAllowRequests()
    {
        // Arrange
        var breaker = new CircuitBreaker(
            "TestCircuit",
            _options,
            _errorClassifierMock.Object,
            _serviceProviderMock.Object,
            _loggerMock.Object);

        // Act & Assert
        breaker.State.Should().Be(CircuitState.Closed);
        breaker.IsAllowed().Should().BeTrue();
    }

    [Fact]
    public void FailureThresholdReached_ShouldOpenCircuit_WhenFailuresAreTransient()
    {
        // Arrange
        var breaker = new CircuitBreaker(
            "TestCircuit",
            _options,
            _errorClassifierMock.Object,
            _serviceProviderMock.Object,
            _loggerMock.Object);

        // Act - record 2 failures (below threshold of 3)
        breaker.RecordFailure(new TimeoutException("Timeout 1"));
        breaker.RecordFailure(new TimeoutException("Timeout 2"));

        breaker.State.Should().Be(CircuitState.Closed);
        breaker.IsAllowed().Should().BeTrue();

        // Record 3rd failure (hits threshold of 3)
        breaker.RecordFailure(new TimeoutException("Timeout 3"));

        // Assert
        breaker.State.Should().Be(CircuitState.Open);
        breaker.IsAllowed().Should().BeFalse();
    }

    [Fact]
    public void NonTransientExceptions_ShouldNotCountAsFailures()
    {
        // Arrange
        var breaker = new CircuitBreaker(
            "TestCircuit",
            _options,
            _errorClassifierMock.Object,
            _serviceProviderMock.Object,
            _loggerMock.Object);

        // Act - record 5 non-transient failures
        for (int i = 0; i < 5; i++)
        {
            breaker.RecordFailure(new InvalidOperationException("Business Error"));
        }

        // Assert
        breaker.State.Should().Be(CircuitState.Closed);
        breaker.IsAllowed().Should().BeTrue();
    }

    [Fact]
    public async Task OpenState_ShouldTransitionToHalfOpen_AfterBreakDurationElapses()
    {
        // Arrange
        var breaker = new CircuitBreaker(
            "TestCircuit",
            _options,
            _errorClassifierMock.Object,
            _serviceProviderMock.Object,
            _loggerMock.Object);

        breaker.RecordFailure(new TimeoutException("Timeout 1"));
        breaker.RecordFailure(new TimeoutException("Timeout 2"));
        breaker.RecordFailure(new TimeoutException("Timeout 3"));

        breaker.State.Should().Be(CircuitState.Open);
        breaker.IsAllowed().Should().BeFalse();

        // Act - wait for break duration to elapse (0.1 seconds + 100ms margin)
        await Task.Delay(200);

        // Assert
        breaker.IsAllowed().Should().BeTrue(); // Checks and transitions to HalfOpen
        breaker.State.Should().Be(CircuitState.HalfOpen);
    }

    [Fact]
    public async Task HalfOpenState_ShouldLimitConcurrentProbeRequests()
    {
        // Arrange
        var options = new ReliabilityOptions
        {
            CircuitBreaker = new CircuitBreakerSettings
            {
                Enabled = true,
                FailureThreshold = 1,
                BreakDurationSeconds = 0.05,
                HalfOpenProbeCount = 2
            }
        };

        var fastBreaker = new CircuitBreaker(
            "FastCircuit",
            options,
            _errorClassifierMock.Object,
            _serviceProviderMock.Object,
            _loggerMock.Object);

        fastBreaker.RecordFailure(new TimeoutException("Timeout"));
        fastBreaker.State.Should().Be(CircuitState.Open);

        // Wait for break duration to pass
        await Task.Delay(100);

        // Act - first probe allowed
        fastBreaker.IsAllowed().Should().BeTrue();
        fastBreaker.State.Should().Be(CircuitState.HalfOpen);

        // Second probe allowed (HalfOpenProbeCount is 2)
        fastBreaker.IsAllowed().Should().BeTrue();

        // Third probe should be rejected/fail-fast to avoid concurrent storm
        fastBreaker.IsAllowed().Should().BeFalse();
    }

    [Fact]
    public async Task SuccessfulProbe_ShouldCloseCircuit()
    {
        // Arrange
        var options = new ReliabilityOptions
        {
            CircuitBreaker = new CircuitBreakerSettings
            {
                Enabled = true,
                FailureThreshold = 1,
                BreakDurationSeconds = 0.05,
                HalfOpenProbeCount = 1
            }
        };

        var breaker = new CircuitBreaker(
            "TestCircuit",
            options,
            _errorClassifierMock.Object,
            _serviceProviderMock.Object,
            _loggerMock.Object);

        breaker.RecordFailure(new TimeoutException("Timeout"));
        breaker.State.Should().Be(CircuitState.Open);

        // Wait for break duration to pass
        await Task.Delay(100);

        // Move to HalfOpen
        breaker.IsAllowed().Should().BeTrue();
        breaker.State.Should().Be(CircuitState.HalfOpen);

        // Act - record success
        breaker.RecordSuccess();

        // Assert
        breaker.State.Should().Be(CircuitState.Closed);
        breaker.IsAllowed().Should().BeTrue();
    }

    [Fact]
    public async Task FailedProbe_ShouldReopenCircuit()
    {
        // Arrange
        var options = new ReliabilityOptions
        {
            CircuitBreaker = new CircuitBreakerSettings
            {
                Enabled = true,
                FailureThreshold = 1,
                BreakDurationSeconds = 0.05,
                HalfOpenProbeCount = 1
            }
        };

        var breaker = new CircuitBreaker(
            "TestCircuit",
            options,
            _errorClassifierMock.Object,
            _serviceProviderMock.Object,
            _loggerMock.Object);

        breaker.RecordFailure(new TimeoutException("Timeout"));
        breaker.State.Should().Be(CircuitState.Open);

        // Wait for break duration to pass
        await Task.Delay(100);

        // Move to HalfOpen
        breaker.IsAllowed().Should().BeTrue();
        breaker.State.Should().Be(CircuitState.HalfOpen);

        // Act - record failure
        breaker.RecordFailure(new TimeoutException("Probe Failed"));

        // Assert
        breaker.State.Should().Be(CircuitState.Open);
        breaker.IsAllowed().Should().BeFalse();
    }
}
