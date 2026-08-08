using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Interfaces;

public interface IPositionCloseManager
{
    Task<bool> ClosePositionAsync(
        Guid positionId,
        CloseReason reason,
        decimal? exitPrice = null,
        string source = "System",
        CancellationToken cancellationToken = default);

    Task<bool> HandleExchangePositionUpdateAsync(
        string symbol,
        decimal exchangeQuantity,
        decimal exitPrice,
        decimal fee,
        CloseReason reason,
        string? rawEventDetails = null,
        CancellationToken cancellationToken = default);
}
