using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using TradingBot.Application.Interfaces;

namespace TradingBot.Infrastructure.Resilience;

public class ResilienceService : IResilienceService
{
    private readonly ResiliencePipeline _httpPipeline;
    private readonly ResiliencePipeline _webSocketPipeline;
    private readonly ILogger<ResilienceService> _logger;

    public ResilienceService(ILogger<ResilienceService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 1. Build HTTP Pipeline: Timeout -> Retry -> Circuit Breaker
        _httpPipeline = new ResiliencePipelineBuilder()
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(10),
                OnTimeout = args =>
                {
                    _logger.LogWarning("Resilience: HTTP operation timed out.");
                    return default;
                }
            })
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>().Handle<Exception>(ex =>
                    ex.Message.Contains("429") || ex.Message.Contains("Too Many Requests")),
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(2),
                OnRetry = args =>
                {
                    _logger.LogWarning("Resilience: Retrying HTTP operation. Attempt {AttemptNumber} after {RetryDelay} due to: {Exception}",
                        args.AttemptNumber, args.RetryDelay, args.Outcome.Exception?.Message);
                    return default;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>(),
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(10),
                MinimumThroughput = 4,
                BreakDuration = TimeSpan.FromSeconds(15),
                OnOpened = args =>
                {
                    _logger.LogError("Resilience: Circuit Breaker OPENED for {BreakDuration} due to exception: {Exception}",
                        args.BreakDuration, args.Outcome.Exception?.Message);
                    return default;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("Resilience: Circuit Breaker CLOSED and healthy.");
                    return default;
                }
            })
            .Build();

        // 2. Build WebSocket Pipeline: Retry
        _webSocketPipeline = new ResiliencePipelineBuilder()
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(15),
                OnTimeout = args =>
                {
                    _logger.LogWarning("Resilience: WebSocket operation timed out.");
                    return default;
                }
            })
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                MaxRetryAttempts = 5,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(1),
                OnRetry = args =>
                {
                    _logger.LogWarning("Resilience: Retrying WebSocket operation. Attempt {AttemptNumber} due to: {Exception}",
                        args.AttemptNumber, args.Outcome.Exception?.Message);
                    return default;
                }
            })
            .Build();
    }

    public async Task<T> ExecuteHttpAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        return await _httpPipeline.ExecuteAsync(async ct => await action(ct), cancellationToken);
    }

    public async Task ExecuteWebSocketAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        await _webSocketPipeline.ExecuteAsync(async ct => await action(ct), cancellationToken);
    }
}
