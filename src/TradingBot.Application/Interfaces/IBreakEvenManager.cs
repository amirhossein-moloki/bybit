using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Trading.Execution.Models;

namespace TradingBot.Application.Interfaces;

public interface IBreakEvenManager
{
    Task<bool> ExecuteBreakEvenCheckAsync(
        Guid positionId,
        decimal currentPrice,
        BreakEvenSettings settings,
        CancellationToken cancellationToken = default);
}
