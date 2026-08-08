using System;
using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.Interfaces;

public interface IPartialCloseManager
{
    Task<bool> ExecutePartialCloseAsync(
        Guid positionId,
        decimal quantity,
        decimal? price = null,
        string reason = "Partial Close",
        string source = "System",
        CancellationToken cancellationToken = default);

    Task<bool> ProcessTakeProfitHitAsync(
        string exchangeOrderId,
        decimal executedQuantity,
        decimal executedPrice,
        CancellationToken cancellationToken = default);
}
