using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class TradeRepository : RepositoryBase<Trade>, ITradeRepository
{
    public TradeRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    // Backward compatibility save method
    public async Task SaveAsync(Trade trade, CancellationToken cancellationToken = default)
    {
        var existing = await DbContext.Trades.FindAsync(new object?[] { trade.Id }, cancellationToken);
        if (existing == null)
        {
            await AddAsync(trade, cancellationToken);
        }
        else
        {
            DbContext.Entry(existing).State = EntityState.Detached;
            Update(trade);
        }
    }

    // New methods from ITradeRepository
    public async Task<IEnumerable<Trade>> GetTradeHistoryAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        return await DbContext.Trades
            .AsNoTracking()
            .Where(t => t.Symbol == normalizedSymbol)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProfitLossReport> GetProfitLossReportAsync(CancellationToken cancellationToken = default)
    {
        var trades = await DbContext.Trades
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var totalProfitLoss = trades.Sum(t => t.ProfitLoss ?? 0m);
        var totalFee = trades.Sum(t => t.Fee);
        var totalTrades = trades.Count;

        var winTrades = trades.Count(t => (t.ProfitLoss ?? 0m) > 0m);
        var lossTrades = trades.Count(t => (t.ProfitLoss ?? 0m) < 0m);
        var winRate = totalTrades > 0 ? (decimal)winTrades / totalTrades * 100m : 0m;

        return new ProfitLossReport
        {
            TotalProfitLoss = totalProfitLoss,
            TotalFee = totalFee,
            TotalTrades = totalTrades,
            WinTrades = winTrades,
            LossTrades = lossTrades,
            WinRate = winRate
        };
    }

    public async Task<PagedResult<Trade>> GetPagedTradesAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        return await GetPagedAsync(pageNumber, pageSize, cancellationToken);
    }
}
