using System;
using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.Interfaces;

public interface IReliabilityService
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        Func<Exception, bool>? isRetryable = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        string operationName,
        Func<Exception, bool>? isRetryable = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
