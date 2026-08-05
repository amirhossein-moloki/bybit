using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Infrastructure.Persistence;

public class TradeRepository : ITradeRepository
{
    private readonly TradingDbContext _dbContext;

    public TradeRepository(TradingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task SaveAsync(Trade trade, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.Trades.FindAsync(new object?[] { trade.Id }, cancellationToken);
        if (existing == null)
        {
            await _dbContext.Trades.AddAsync(trade, cancellationToken);
        }
        else
        {
            _dbContext.Trades.Update(trade);
        }
    }

    public async Task<Trade?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Trades.FindAsync(new object?[] { id }, cancellationToken);
    }
}
