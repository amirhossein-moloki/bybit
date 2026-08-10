using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Interfaces;

namespace TradingBot.Infrastructure.Resilience;

public class ResilienceService : IResilienceService
{
    private readonly IReliabilityService _reliabilityService;

    public ResilienceService(IReliabilityService reliabilityService)
    {
        _reliabilityService = reliabilityService ?? throw new ArgumentNullException(nameof(reliabilityService));
    }

    public async Task<T> ExecuteHttpAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        return await _reliabilityService.ExecuteAsync(action, "HTTP Operation", cancellationToken: cancellationToken);
    }

    public async Task<T> ExecuteHttpAsync<T>(Func<CancellationToken, Task<T>> action, Func<Exception, bool>? isRetryable, CancellationToken cancellationToken = default)
    {
        return await _reliabilityService.ExecuteAsync(action, "HTTP Operation", isRetryable, cancellationToken: cancellationToken);
    }

    public async Task ExecuteWebSocketAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        await _reliabilityService.ExecuteAsync(action, "WebSocket Operation", cancellationToken: cancellationToken);
    }
}
