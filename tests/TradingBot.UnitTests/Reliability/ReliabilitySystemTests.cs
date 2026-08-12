using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Polly.Timeout;
using TradingBot.Application.Configuration;
using TradingBot.Application.Enums;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Services;
using TradingBot.Infrastructure.Resilience;
using Xunit;

namespace TradingBot.UnitTests.Reliability;

public class ReliabilitySystemTests
{
    private readonly Mock<ILogger<ReliabilityService>> _loggerMock = new();

    private ReliabilityService CreateReliabilityService(ReliabilityOptions options, IRetryDelayCalculator delayCalculator, IErrorClassifier errorClassifier)
    {
        var cbMock = new Mock<ICircuitBreaker>();
        cbMock.Setup(c => c.IsAllowed()).Returns(true);
        var cbRegistryMock = new Mock<ICircuitBreakerRegistry>();
        cbRegistryMock.Setup(r => r.GetOrCreate(It.IsAny<string>())).Returns(cbMock.Object);
        return new ReliabilityService(options, delayCalculator, errorClassifier, cbRegistryMock.Object, _loggerMock.Object);
    }

    #region Configuration (ReliabilityOptions) Tests

    [Fact]
    public void Validate_ShouldPass_ForValidConfiguration()
    {
        // Arrange
        var options = new ReliabilityOptions
        {
            Retry = new RetrySettings
            {
                Enabled = true,
                MaxAttempts = 3,
                InitialDelaySeconds = 1.0,
                MaxDelaySeconds = 10.0,
                BackoffMultiplier = 2.0,
                JitterEnabled = true
            },
            Timeout = new TimeoutSettings
            {
                Enabled = true,
                DefaultTimeoutSeconds = 15.0
            }
        };

        // Act & Assert
        Action act = () => options.Validate();
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ShouldThrow_WhenMaxAttemptsIsNegative()
    {
        // Arrange
        var options = new ReliabilityOptions();
        options.Retry.MaxAttempts = -1;

        // Act & Assert
        Action act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
           .WithMessage("MaxAttempts must be non-negative.");
    }

    [Fact]
    public void Validate_ShouldThrow_WhenInitialDelayIsNegative()
    {
        // Arrange
        var options = new ReliabilityOptions();
        options.Retry.InitialDelaySeconds = -0.5;

        // Act & Assert
        Action act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
           .WithMessage("InitialDelay must be non-negative.");
    }

    [Fact]
    public void Validate_ShouldThrow_WhenMaxDelayIsLessThanInitialDelay()
    {
        // Arrange
        var options = new ReliabilityOptions();
        options.Retry.InitialDelaySeconds = 5.0;
        options.Retry.MaxDelaySeconds = 2.0;

        // Act & Assert
        Action act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
           .WithMessage("MaxDelay must be greater than or equal to InitialDelay.");
    }

    [Fact]
    public void Validate_ShouldThrow_WhenBackoffMultiplierIsZeroOrNegative()
    {
        // Arrange
        var options = new ReliabilityOptions();
        options.Retry.BackoffMultiplier = 0;

        // Act & Assert
        Action act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
           .WithMessage("BackoffMultiplier must be greater than zero.");
    }

    [Fact]
    public void Validate_ShouldThrow_WhenDefaultTimeoutIsZeroOrNegative()
    {
        // Arrange
        var options = new ReliabilityOptions();
        options.Timeout.DefaultTimeoutSeconds = 0;

        // Act & Assert
        Action act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
           .WithMessage("DefaultTimeout must be greater than zero.");
    }

    #endregion

    #region Error Classification Tests

    [Theory]
    // HTTP Transient Statuses
    [InlineData(HttpStatusCode.RequestTimeout, ErrorRetryability.Retryable)]
    [InlineData(HttpStatusCode.TooManyRequests, ErrorRetryability.Retryable)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorRetryability.Retryable)]
    [InlineData(HttpStatusCode.BadGateway, ErrorRetryability.Retryable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ErrorRetryability.Retryable)]
    [InlineData(HttpStatusCode.GatewayTimeout, ErrorRetryability.Retryable)]
    // HTTP Non-Transient Statuses
    [InlineData(HttpStatusCode.BadRequest, ErrorRetryability.NonRetryable)]
    [InlineData(HttpStatusCode.Unauthorized, ErrorRetryability.NonRetryable)]
    [InlineData(HttpStatusCode.Forbidden, ErrorRetryability.NonRetryable)]
    [InlineData(HttpStatusCode.NotFound, ErrorRetryability.NonRetryable)]
    public void Classify_ShouldMapHttpStatusCodesCorrectly(HttpStatusCode statusCode, ErrorRetryability expected)
    {
        // Arrange
        var classifier = new ErrorClassifier();
        var httpEx = new HttpRequestException("HTTP Error", null, statusCode);

        // Act
        var result = classifier.Classify(httpEx);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Classify_ShouldMapTimeoutsAndSocketFailuresAsRetryable()
    {
        // Arrange
        var classifier = new ErrorClassifier();
        var timeoutEx = new TimeoutException();
        var httpMsgEx = new HttpRequestException("A socket error occurred connection reset");

        // Act & Assert
        classifier.Classify(timeoutEx).Should().Be(ErrorRetryability.Retryable);
        classifier.Classify(httpMsgEx).Should().Be(ErrorRetryability.Retryable);
    }

    [Fact]
    public void Classify_ShouldMapTelegramExceptionsByTypeNameString()
    {
        // Arrange
        var classifier = new ErrorClassifier();

        // Let's mock a Telegram custom exception by defining nested dummy exceptions with matching names
        var authEx = new TelegramAuthenticationException();
        var connEx = new TelegramConnectionException();

        // Act & Assert
        classifier.Classify(authEx).Should().Be(ErrorRetryability.NonRetryable);
        classifier.Classify(connEx).Should().Be(ErrorRetryability.Retryable);
    }

    private class TelegramAuthenticationException : Exception { }
    private class TelegramConnectionException : Exception { }

    [Fact]
    public void Classify_ShouldMapBybitMessagesCorrectly()
    {
        // Arrange
        var classifier = new ErrorClassifier();
        var rateLimitEx = new Exception("Bybit Error RetCode=33004 RateLimited");
        var balanceEx = new Exception("Bybit Error RetCode=110004 Insufficient Balance");

        // Act & Assert
        classifier.Classify(rateLimitEx).Should().Be(ErrorRetryability.Retryable);
        classifier.Classify(balanceEx).Should().Be(ErrorRetryability.NonRetryable);
    }

    #endregion

    #region Backoff Calculation Tests

    [Fact]
    public void CalculateDelay_ShouldDoubleTheDelay_WhenMultiplierIsTwoAndJitterDisabled()
    {
        // Arrange
        var options = new ReliabilityOptions();
        options.Retry.InitialDelaySeconds = 1.0;
        options.Retry.BackoffMultiplier = 2.0;
        options.Retry.JitterEnabled = false;

        var calculator = new RetryDelayCalculator();

        // Act
        var delay1 = calculator.CalculateDelay(1, options);
        var delay2 = calculator.CalculateDelay(2, options);
        var delay3 = calculator.CalculateDelay(3, options);

        // Assert
        delay1.TotalSeconds.Should().Be(1.0);
        delay2.TotalSeconds.Should().Be(2.0);
        delay3.TotalSeconds.Should().Be(4.0);
    }

    [Fact]
    public void CalculateDelay_ShouldApplyMaxDelayCap()
    {
        // Arrange
        var options = new ReliabilityOptions();
        options.Retry.InitialDelaySeconds = 1.0;
        options.Retry.BackoffMultiplier = 2.0;
        options.Retry.MaxDelaySeconds = 5.0;
        options.Retry.JitterEnabled = false;

        var calculator = new RetryDelayCalculator();

        // Act
        var delay = calculator.CalculateDelay(5, options); // would be 16s without cap

        // Assert
        delay.TotalSeconds.Should().Be(5.0);
    }

    [Fact]
    public void CalculateDelay_ShouldApplyBoundedJitterCorrectly()
    {
        // Arrange
        var options = new ReliabilityOptions();
        options.Retry.InitialDelaySeconds = 10.0;
        options.Retry.JitterEnabled = true;

        // Custom Random stub to return deterministic 0.5 (which maps to (0.8 + 0.5 * 0.4) = 1.0 multiplier)
        var mockRandom = new Mock<Random>();
        mockRandom.Setup(r => r.NextDouble()).Returns(0.5);

        var calculator = new RetryDelayCalculator(mockRandom.Object);

        // Act
        var delay = calculator.CalculateDelay(1, options);

        // Assert
        // 10.0 * 1.0 = 10.0s
        delay.TotalSeconds.Should().Be(10.0);
    }

    #endregion

    #region Retry Execution (ReliabilityService) Tests

    [Fact]
    public async Task ExecuteAsync_ShouldSucceedOnFirstAttempt_WithZeroRetries()
    {
        // Arrange
        var options = new ReliabilityOptions();
        options.Retry.Enabled = true;
        options.Retry.MaxAttempts = 3;

        var delayCalculator = new RetryDelayCalculator();
        var errorClassifier = new ErrorClassifier();
        var service = CreateReliabilityService(options, delayCalculator, errorClassifier);

        int attempts = 0;

        // Act
        var result = await service.ExecuteAsync(ct =>
        {
            attempts++;
            return Task.FromResult("OK");
        }, "FirstSuccess");

        // Assert
        result.Should().Be("OK");
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSucceedAfterTransientFailure_AndEventuallyReturnResult()
    {
        // Arrange
        var options = new ReliabilityOptions();
        options.Retry.Enabled = true;
        options.Retry.MaxAttempts = 3;
        options.Retry.InitialDelaySeconds = 0.001; // super fast

        var delayCalculator = new RetryDelayCalculator();
        var errorClassifier = new ErrorClassifier();
        var service = CreateReliabilityService(options, delayCalculator, errorClassifier);

        int attempts = 0;

        // Act
        var result = await service.ExecuteAsync(ct =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new TimeoutException("Transient network timeout");
            }
            return Task.FromResult("Recovered");
        }, "RetrySuccess");

        // Assert
        result.Should().Be("Recovered");
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPropagateFinalException_WhenRetriesAreExhausted()
    {
        // Arrange
        var options = new ReliabilityOptions();
        options.Retry.Enabled = true;
        options.Retry.MaxAttempts = 3;
        options.Retry.InitialDelaySeconds = 0.001;

        var delayCalculator = new RetryDelayCalculator();
        var errorClassifier = new ErrorClassifier();
        var service = CreateReliabilityService(options, delayCalculator, errorClassifier);

        int attempts = 0;

        // Act
        Func<Task> act = async () =>
        {
            await service.ExecuteAsync<string>(ct =>
            {
                attempts++;
                throw new TimeoutException("Persistent timeout");
            }, "PersistentFailure");
        };

        // Assert
        await act.Should().ThrowAsync<TimeoutException>().WithMessage("Persistent timeout");
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailImmediatelyWithoutRetry_OnNonRetryableException()
    {
        // Arrange
        var options = new ReliabilityOptions();
        options.Retry.Enabled = true;
        options.Retry.MaxAttempts = 3;

        var delayCalculator = new RetryDelayCalculator();
        var errorClassifier = new ErrorClassifier();
        var service = CreateReliabilityService(options, delayCalculator, errorClassifier);

        int attempts = 0;

        // Act
        Func<Task> act = async () =>
        {
            await service.ExecuteAsync<string>(ct =>
            {
                attempts++;
                throw new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden);
            }, "NonRetryableFailure");
        };

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        attempts.Should().Be(1); // failed immediately
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHonorCustomIsRetryablePredicate()
    {
        // Arrange
        var options = new ReliabilityOptions();
        options.Retry.Enabled = true;
        options.Retry.MaxAttempts = 3;
        options.Retry.InitialDelaySeconds = 0.001;

        var delayCalculator = new RetryDelayCalculator();
        var errorClassifier = new ErrorClassifier();
        var service = CreateReliabilityService(options, delayCalculator, errorClassifier);

        int attempts = 0;

        // Custom predicate makes InvalidOperationException retryable, whereas normally it wouldn't be
        Func<Exception, bool> customIsRetryable = ex => ex is InvalidOperationException;

        // Act
        var result = await service.ExecuteAsync(ct =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("Usually non-retryable, but custom is");
            }
            return Task.FromResult("CustomRetrySucceeded");
        }, "CustomPredicateTest", customIsRetryable);

        // Assert
        result.Should().Be("CustomRetrySucceeded");
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldStopImmediately_WhenCancelledDuringExecution()
    {
        // Arrange
        var options = new ReliabilityOptions();
        options.Retry.Enabled = true;
        options.Retry.MaxAttempts = 3;

        var delayCalculator = new RetryDelayCalculator();
        var errorClassifier = new ErrorClassifier();
        var service = CreateReliabilityService(options, delayCalculator, errorClassifier);

        using var cts = new CancellationTokenSource();
        int attempts = 0;

        // Act
        Func<Task> act = async () =>
        {
            await service.ExecuteAsync<string>(ct =>
            {
                attempts++;
                cts.Cancel(); // Cancel token
                ct.ThrowIfCancellationRequested();
                return Task.FromResult("No");
            }, "CancelledDuringExec", cancellationToken: cts.Token);
        };

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(1); // immediately stopped
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHonorDynamicRateLimitRetryAfterHeader()
    {
        // Arrange
        var options = new ReliabilityOptions();
        options.Retry.Enabled = true;
        options.Retry.MaxAttempts = 2;
        options.Retry.InitialDelaySeconds = 1000.0; // huge initial delay

        var delayCalculator = new RetryDelayCalculator();
        var errorClassifier = new ErrorClassifier();
        var service = CreateReliabilityService(options, delayCalculator, errorClassifier);

        int attempts = 0;

        // Act
        var startTime = DateTime.UtcNow;
        var result = await service.ExecuteAsync(ct =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new CustomRateLimitException(TimeSpan.FromMilliseconds(10)); // tiny override
            }
            return Task.FromResult("Honored");
        }, "RetryAfterTest");
        var duration = DateTime.UtcNow - startTime;

        // Assert
        result.Should().Be("Honored");
        attempts.Should().Be(2);
        duration.TotalSeconds.Should().BeLessThan(10.0); // should be almost instant instead of waiting 1000 seconds
    }

    private class CustomRateLimitException : Exception
    {
        public TimeSpan RetryAfter { get; }

        public CustomRateLimitException(TimeSpan retryAfter) : base("Rate limited")
        {
            RetryAfter = retryAfter;
        }
    }

    #endregion
}
