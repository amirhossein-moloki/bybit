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

    public async Task<IEnumerable<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.Positions
            .AsNoTracking()
            .Where(p => p.Status == PositionStatus.Open)
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
}
