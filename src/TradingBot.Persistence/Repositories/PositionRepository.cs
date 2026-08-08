using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class PositionRepository : RepositoryBase<Position>, IPositionRepository
{
    public PositionRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    public override async Task<Position?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DbContext.Positions
            .Include(p => p.Targets)
            .Include(p => p.Events)
            .Include(p => p.StopLossHistories)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.Positions
            .AsNoTracking()
            .Where(p => p.Status == PositionStatus.Open || p.Status == PositionStatus.PartiallyClosed || p.Status == PositionStatus.Pending)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Position>> GetBySymbolAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        return await DbContext.Positions
            .AsNoTracking()
            .Where(p => p.Symbol == normalizedSymbol)
            .ToListAsync(cancellationToken);
    }

    public async Task ClosePositionAsync(Guid id, decimal exitPrice, CancellationToken cancellationToken = default)
    {
        var position = await GetByIdAsync(id, cancellationToken);
        if (position != null)
        {
            position.Close(exitPrice);
            Update(position);
        }
    }

    public async Task<Position?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Positions
            .Include(p => p.Targets)
            .Include(p => p.Events)
            .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
    }

    public async Task<Position?> GetByExchangePositionIdAsync(string exchangePositionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exchangePositionId)) return null;

        return await DbContext.Positions
            .Include(p => p.Targets)
            .Include(p => p.Events)
            .FirstOrDefaultAsync(p => p.ExchangePositionId == exchangePositionId, cancellationToken);
    }

    public async Task<bool> ExistsAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Positions
            .AnyAsync(p => p.OrderId == orderId, cancellationToken);
    }
}
