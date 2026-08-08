using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Trading.Execution.Models;

namespace TradingBot.Application.Interfaces;

public interface ITrailingStopManager
{
    Task<bool> ExecuteTrailingStopCheckAsync(
        Guid positionId,
        decimal currentPrice,
        TrailingStopSettings settings,
        CancellationToken cancellationToken = default);
}
