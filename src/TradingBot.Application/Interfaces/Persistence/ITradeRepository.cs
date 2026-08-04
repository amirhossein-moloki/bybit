using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Interfaces.Persistence;

public interface ITradeRepository
{
    Task SaveAsync(Trade trade, CancellationToken cancellationToken = default);
    Task<Trade?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
