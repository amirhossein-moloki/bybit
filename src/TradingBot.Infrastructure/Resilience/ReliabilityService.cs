using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Timeout;
using TradingBot.Application.Configuration;
using TradingBot.Application.Enums;
using TradingBot.Application.Exceptions;
using TradingBot.Application.Interfaces;

namespace TradingBot.Infrastructure.Resilience;

public class ReliabilityService : IReliabilityService
{
    private readonly ReliabilityOptions _options;
    private readonly IRetryDelayCalculator _delayCalculator;
    private readonly IErrorClassifier _errorClassifier;
    private readonly ICircuitBreakerRegistry _circuitBreakerRegistry;
    private readonly ILogger<ReliabilityService> _logger;
    private readonly ResiliencePipeline _pipeline;

    private static readonly ResiliencePropertyKey<string> OperationNameKey = new("OperationName");
    private static readonly ResiliencePropertyKey<string> CorrelationIdKey = new("CorrelationId");
    private static readonly ResiliencePropertyKey<Func<Exception, bool>?> IsRetryableKey = new("IsRetryable");
    private static readonly ResiliencePropertyKey<int> AttemptCountKey = new("AttemptCount");

    public ReliabilityService(
        ReliabilityOptions options,
        IRetryDelayCalculator delayCalculator,
        IErrorClassifier errorClassifier,
        ICircuitBreakerRegistry circuitBreakerRegistry,
        ILogger<ReliabilityService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _delayCalculator = delayCalculator ?? throw new ArgumentNullException(nameof(delayCalculator));
        _errorClassifier = errorClassifier ?? throw new ArgumentNullException(nameof(errorClassifier));
        _circuitBreakerRegistry = circuitBreakerRegistry ?? throw new ArgumentNullException(nameof(circuitBreakerRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var builder = new ResiliencePipelineBuilder();

        // 1. Add Retry Strategy if enabled
        if (_options.Retry.Enabled)
        {
            builder.AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                ShouldHandle = args =>
                {
                    var ex = args.Outcome.Exception;
                    if (ex == null) return new ValueTask<bool>(false);

                    // CircuitOpenedException is NOT retryable
                    if (ex is CircuitOpenedException)
                    {
                        return new ValueTask<bool>(false);
                    }

                    // OperationCanceledException (excluding TimeoutRejectedException) is NOT retryable
                    if (ex is OperationCanceledException && ex.GetType().FullName != "Polly.Timeout.TimeoutRejectedException")
                    {
                        return new ValueTask<bool>(false);
                    }

                    // Check if custom isRetryable delegate was passed via Context
                    var customIsRetryable = args.Context.Properties.GetValue(IsRetryableKey, null);
                    if (customIsRetryable != null)
                    {
                        try
                        {
                            return new ValueTask<bool>(customIsRetryable(ex));
                        }
                        catch
                        {
                            return new ValueTask<bool>(false);
                        }
                    }

                    var retryability = _errorClassifier.Classify(ex);
                    return new ValueTask<bool>(retryability == ErrorRetryability.Retryable);
                },
                MaxRetryAttempts = _options.Retry.MaxAttempts > 0 ? _options.Retry.MaxAttempts - 1 : 0,
                DelayGenerator = args =>
                {
                    var ex = args.Outcome.Exception;
                    if (ex != null)
                    {
                        // Dynamic Rate Limit timing: check if the exception has a "RetryAfter" property using reflection
                        var retryAfterProp = ex.GetType().GetProperty("RetryAfter");
                        if (retryAfterProp != null)
                        {
                            var val = retryAfterProp.GetValue(ex);
                            if (val is TimeSpan ts && ts > TimeSpan.Zero)
                            {
                                _logger.LogInformation("RateLimitRetryAfter: Exception specifies explicit Retry-After of {RetryAfter}s. Using it.", ts.TotalSeconds);
                                return new ValueTask<TimeSpan?>(ts);
                            }
                        }
                    }

                    int attempt = args.AttemptNumber + 1; // 1-based index for delay calculation
                    var delay = _delayCalculator.CalculateDelay(attempt, _options);
                    return new ValueTask<TimeSpan?>(delay);
                },
                OnRetry = args =>
                {
                    var operationName = args.Context.Properties.GetValue(OperationNameKey, "Unknown Operation");
                    var correlationId = args.Context.Properties.GetValue(CorrelationIdKey, "N/A");

                    // Store attempt count in Context for final failure logging
                    args.Context.Properties.Set(AttemptCountKey, args.AttemptNumber + 2);

                    // Record failure on circuit breaker for each individual failed retry attempt
                    var breaker = _circuitBreakerRegistry.GetOrCreate(operationName);
                    if (args.Outcome.Exception != null)
                    {
                        breaker.RecordFailure(args.Outcome.Exception);
                    }

                    _logger.LogWarning("RetryAttempt: Operation: {OperationName} | Attempt: {Attempt} | MaxAttempts: {MaxAttempts} | ErrorType: {ErrorType} | Delay: {Delay}s | CorrelationId: {CorrelationId}",
                        operationName,
                        args.AttemptNumber + 2,
                        _options.Retry.MaxAttempts,
                        args.Outcome.Exception?.GetType().Name ?? "UnknownError",
                        args.RetryDelay.TotalSeconds,
                        correlationId);

                    return default;
                }
            });
        }

        // 2. Add Timeout Strategy if enabled (Timeout wrapped inside Retry, so each attempt gets its own timeout)
        if (_options.Timeout.Enabled)
        {
            builder.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = _options.Timeout.DefaultTimeout,
                OnTimeout = args =>
                {
                    var operationName = args.Context.Properties.GetValue(OperationNameKey, "Unknown Operation");
                    var correlationId = args.Context.Properties.GetValue(CorrelationIdKey, "N/A");
                    _logger.LogWarning("TimeoutOccurred: Operation {OperationName} timed out after {Timeout}s. CorrelationId: {CorrelationId}",
                        operationName, _options.Timeout.DefaultTimeout.TotalSeconds, correlationId);
                    return default;
                }
            });
        }

        _pipeline = builder.Build();
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        Func<Exception, bool>? isRetryable = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));

        var resolvedCorrId = correlationId ?? Guid.NewGuid().ToString();
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(OperationNameKey, operationName);
        context.Properties.Set(CorrelationIdKey, resolvedCorrId);
        context.Properties.Set(IsRetryableKey, isRetryable);
        context.Properties.Set(AttemptCountKey, 1); // defaults to 1 attempt

        var breaker = _circuitBreakerRegistry.GetOrCreate(operationName);

        try
        {
            if (!breaker.IsAllowed())
            {
                throw new CircuitOpenedException($"Circuit breaker '{operationName}' is open.");
            }

            var result = await _pipeline.ExecuteAsync(async (ctx) =>
            {
                if (!breaker.IsAllowed())
                {
                    throw new CircuitOpenedException($"Circuit breaker '{operationName}' is open.");
                }
                return await operation(ctx.CancellationToken);
            }, context);

            breaker.RecordSuccess();
            return result;
        }
        catch (Exception ex)
        {
            if (ex is not CircuitOpenedException)
            {
                breaker.RecordFailure(ex);
            }

            var finalAttempts = context.Properties.GetValue(AttemptCountKey, 1);
            _logger.LogError(ex, "FinalFailure: Operation: {OperationName} | Attempts: {Attempts} | Final Error: {ErrorMessage} | CorrelationId: {CorrelationId}",
                operationName,
                finalAttempts,
                ex.Message,
                resolvedCorrId);
            throw;
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        string operationName,
        Func<Exception, bool>? isRetryable = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));

        var resolvedCorrId = correlationId ?? Guid.NewGuid().ToString();
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(OperationNameKey, operationName);
        context.Properties.Set(CorrelationIdKey, resolvedCorrId);
        context.Properties.Set(IsRetryableKey, isRetryable);
        context.Properties.Set(AttemptCountKey, 1);

        var breaker = _circuitBreakerRegistry.GetOrCreate(operationName);

        try
        {
            if (!breaker.IsAllowed())
            {
                throw new CircuitOpenedException($"Circuit breaker '{operationName}' is open.");
            }

            await _pipeline.ExecuteAsync(async (ctx) =>
            {
                if (!breaker.IsAllowed())
                {
                    throw new CircuitOpenedException($"Circuit breaker '{operationName}' is open.");
                }
                await operation(ctx.CancellationToken);
            }, context);

            breaker.RecordSuccess();
        }
        catch (Exception ex)
        {
            if (ex is not CircuitOpenedException)
            {
                breaker.RecordFailure(ex);
            }

            var finalAttempts = context.Properties.GetValue(AttemptCountKey, 1);
            _logger.LogError(ex, "FinalFailure: Operation: {OperationName} | Attempts: {Attempts} | Final Error: {ErrorMessage} | CorrelationId: {CorrelationId}",
                operationName,
                finalAttempts,
                ex.Message,
                resolvedCorrId);
            throw;
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
