using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Analytics.DTOs;
using TradingBot.Application.Analytics.Interfaces;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Queries;

public class PerformanceAnalyticsQueryService : IPerformanceAnalyticsQueryService
{
    private readonly TradingDbContext _dbContext;

    public PerformanceAnalyticsQueryService(TradingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyList<AnalyticsTradeDto>> GetCompletedTradesAsync(
        GetAnalyticsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.StartDate.HasValue && query.EndDate.HasValue && query.StartDate.Value > query.EndDate.Value)
        {
            throw new ArgumentException("The 'StartDate' must be less than or equal to the 'EndDate'.");
        }

        var rawQuery = from t in _dbContext.Trades.AsNoTracking()
                       join p in _dbContext.Positions.AsNoTracking() on t.PositionId equals p.Id into joined
                       from p in joined.DefaultIfEmpty()
                       where t.ClosedAt != null && t.PositionId != null
                       select new AnalyticsTradeDto(
                           t.Id,
                           t.NetPnL,
                           t.ProfitLoss,
                           t.Fee,
                           t.OpenedAt,
                           t.ClosedAt,
                           p != null ? p.Symbol : t.Symbol,
                           p != null ? p.Side : (t.Side == SignalType.Buy ? OrderSide.Buy : OrderSide.Sell)
                       );

        if (!string.IsNullOrWhiteSpace(query.Symbol))
        {
            var sym = query.Symbol.Trim().ToUpperInvariant();
            rawQuery = rawQuery.Where(x => x.Symbol == sym);
        }

        if (query.StartDate.HasValue)
        {
            rawQuery = rawQuery.Where(x => x.ClosedAt >= query.StartDate.Value);
        }

        if (query.EndDate.HasValue)
        {
            rawQuery = rawQuery.Where(x => x.ClosedAt <= query.EndDate.Value);
        }

        var list = await rawQuery.ToListAsync(cancellationToken);

        return list
            .OrderBy(t => t.ClosedAt)
            .ThenBy(t => t.Id)
            .ToList();
    }
}
