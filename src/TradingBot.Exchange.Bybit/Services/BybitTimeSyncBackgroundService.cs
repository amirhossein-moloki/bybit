using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace TradingBot.Exchange.Bybit.Services;

/// <summary>
/// Hosted background service that triggers initial and periodic time synchronization with Bybit.
/// </summary>
public class BybitTimeSyncBackgroundService : BackgroundService
{
    private readonly BybitTimeProvider _timeProvider;

    public BybitTimeSyncBackgroundService(BybitTimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _timeProvider.StartBackgroundSyncAsync(stoppingToken);
    }
}
