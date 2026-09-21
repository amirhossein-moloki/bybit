using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Interfaces;

namespace TradingBot.Exchange.Bybit.Services;

/// <summary>
/// Fallback time provider using local system clock (DateTimeOffset.UtcNow) without offset adjustment.
/// </summary>
public class SystemExchangeTimeProvider : IExchangeTimeProvider
{
    public long GetCurrentMilliseconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public Task SyncTimeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
