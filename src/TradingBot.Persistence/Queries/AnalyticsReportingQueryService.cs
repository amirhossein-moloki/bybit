using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Analytics.DTOs;
using TradingBot.Application.Analytics.Interfaces;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Queries;

public class AnalyticsReportingQueryService : IAnalyticsReportingQueryService
{
    private readonly TradingDbContext _dbContext;

    public AnalyticsReportingQueryService(TradingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyList<ReportTradeDto>> GetReportTradesAsync(
        ReportFilterDto filters,
        CancellationToken cancellationToken = default)
    {
        var query = BuildBaseQuery(filters, sorted: true);
        return await query.ToListAsync(cancellationToken);
    }

    public async IAsyncEnumerable<ReportTradeDto> StreamReportTradesAsync(
        ReportFilterDto filters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = BuildBaseQuery(filters, sorted: true);

        await foreach (var item in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }

    private IQueryable<ReportTradeDto> BuildBaseQuery(ReportFilterDto filters, bool sorted)
    {
        var dbQuery = _dbContext.Trades.AsNoTracking()
            .Where(t => t.ClosedAt != null && t.PositionId != null);

        // Apply filters directly on Trade fields for index utilization
        if (filters.StartDate.HasValue)
        {
            dbQuery = dbQuery.Where(t => t.ClosedAt >= filters.StartDate.Value);
        }

        if (filters.EndDate.HasValue)
        {
            dbQuery = dbQuery.Where(t => t.ClosedAt <= filters.EndDate.Value);
        }

        if (filters.MinPnL.HasValue)
        {
            dbQuery = dbQuery.Where(t => (t.NetPnL ?? ((t.ProfitLoss ?? 0m) - t.Fee)) >= filters.MinPnL.Value);
        }

        if (filters.MaxPnL.HasValue)
        {
            dbQuery = dbQuery.Where(t => (t.NetPnL ?? ((t.ProfitLoss ?? 0m) - t.Fee)) <= filters.MaxPnL.Value);
        }

        if (filters.CloseReason.HasValue)
        {
            dbQuery = dbQuery.Where(t => t.CloseReason == filters.CloseReason.Value);
        }

        // Apply ordering before projection to ensure database translation
        if (sorted)
        {
            dbQuery = dbQuery.OrderBy(t => t.ClosedAt).ThenBy(t => t.Id);
        }

        // Apply Join and Projection
        var projectedQuery = from t in dbQuery
                             join p in _dbContext.Positions.AsNoTracking() on t.PositionId equals p.Id into joined
                             from p in joined.DefaultIfEmpty()
                             select new ReportTradeDto(
                                 t.Id,
                                 t.PositionId,
                                 p != null ? p.Symbol : t.Symbol,
                                 p != null ? p.Side : (t.Side == SignalType.Buy ? OrderSide.Buy : OrderSide.Sell),
                                 t.EntryPrice,
                                 t.ExitPrice,
                                 t.Quantity,
                                 t.ProfitLoss,
                                 t.Fee,
                                 t.FundingFee,
                                 t.NetPnL ?? ((t.ProfitLoss ?? 0m) - t.Fee),
                                 t.CloseReason,
                                 t.OpenedAt,
                                 t.ClosedAt
                             );

        // Apply fallback/derived query filters
        if (!string.IsNullOrWhiteSpace(filters.Symbol))
        {
            var sym = filters.Symbol.Trim().ToUpperInvariant();
            projectedQuery = projectedQuery.Where(x => x.Symbol == sym);
        }

        if (filters.Side.HasValue)
        {
            projectedQuery = projectedQuery.Where(x => x.Side == filters.Side.Value);
        }

        return projectedQuery;
    }
}
