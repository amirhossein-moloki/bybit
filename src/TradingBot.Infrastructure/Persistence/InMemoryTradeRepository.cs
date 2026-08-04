using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Domain.Entities;

namespace TradingBot.Infrastructure.Persistence;

public class InMemoryTradeRepository : ITradeRepository
{
    private static readonly ConcurrentDictionary<Guid, Trade> _trades = new();

    public Task SaveAsync(Trade trade, CancellationToken cancellationToken = default)
    {
        _trades[trade.Id] = trade;
        return Task.CompletedTask;
    }

    public Task<Trade?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _trades.TryGetValue(id, out var trade);
        return Task.FromResult<Trade?>(trade);
    }
}
