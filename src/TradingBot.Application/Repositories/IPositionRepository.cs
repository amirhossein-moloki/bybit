using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Repositories;

public interface IPositionRepository : IRepository<Position>
{
    Task<IEnumerable<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Position>> GetBySymbolAsync(string symbol, CancellationToken cancellationToken = default);
    Task ClosePositionAsync(Guid id, decimal exitPrice, CancellationToken cancellationToken = default);
    Task<Position?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<Position?> GetByExchangePositionIdAsync(string exchangePositionId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid orderId, CancellationToken cancellationToken = default);
}
