using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class TradeOperationRepository : RepositoryBase<TradeOperation>, ITradeOperationRepository
{
    public TradeOperationRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<TradeOperation?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return null;
        return await DbContext.TradeOperations
            .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
    }
}
