using System;
using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.Interfaces;

public interface IResilienceService
{
    Task<T> ExecuteHttpAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
    Task ExecuteWebSocketAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
}
