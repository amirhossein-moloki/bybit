using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Dashboard.DTOs;
using TradingBot.Application.Dashboard.Interfaces;
using TradingBot.Application.Exceptions;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Queries;

public class TradingDashboardQueryService : ITradingDashboardQueryService
{
    private readonly TradingDbContext _dbContext;

    public TradingDashboardQueryService(TradingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<TradingDashboardOverviewDto> GetOverviewAsync(
        TradingDashboardQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Date range validation
            if (query.From.HasValue && query.To.HasValue && query.From.Value > query.To.Value)
            {
                throw new ArgumentException("The 'From' date must be less than or equal to the 'To' date.");
            }

            var ordersQuery = _dbContext.Orders.AsNoTracking();
            var positionsQuery = _dbContext.Positions.AsNoTracking();
            var tradesQuery = _dbContext.Trades.AsNoTracking();

            // Apply Symbol filtering
            if (!string.IsNullOrWhiteSpace(query.Symbol))
            {
                var sym = query.Symbol.Trim().ToUpperInvariant();
                ordersQuery = ordersQuery.Where(o => o.Symbol.Value == sym);
                positionsQuery = positionsQuery.Where(p => p.Symbol == sym);
                tradesQuery = tradesQuery.Where(t => t.Symbol == sym);
            }

            // Apply Side filtering
            if (query.Side.HasValue)
            {
                var side = query.Side.Value;
                ordersQuery = ordersQuery.Where(o => o.Side == side);
                positionsQuery = positionsQuery.Where(p => p.Side == side);

                var sigSide = side == OrderSide.Buy ? SignalType.Buy : SignalType.Sell;
                tradesQuery = tradesQuery.Where(t => t.Side == sigSide);
            }

            // Apply Status filtering
            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                var statusStr = query.Status.Trim();
                bool filterApplied = false;

                if (Enum.TryParse<OrderStatus>(statusStr, true, out var orderStatus))
                {
                    ordersQuery = ordersQuery.Where(o => o.Status == orderStatus);
                    filterApplied = true;
                }
                if (Enum.TryParse<PositionStatus>(statusStr, true, out var positionStatus))
                {
                    positionsQuery = positionsQuery.Where(p => p.Status == positionStatus);
                    filterApplied = true;
                }
                if (Enum.TryParse<CloseReason>(statusStr, true, out var closeReason))
                {
                    tradesQuery = tradesQuery.Where(t => t.CloseReason == closeReason);
                    filterApplied = true;
                }

                // If Status string was provided but didn't match any known enum,
                // we still want to apply the empty state logic or filter out records
                if (!filterApplied)
                {
                    ordersQuery = ordersQuery.Where(o => false);
                    positionsQuery = positionsQuery.Where(p => false);
                    tradesQuery = tradesQuery.Where(t => false);
                }
            }

            // Apply date filtering
            if (query.From.HasValue)
            {
                var fromVal = query.From.Value;
                ordersQuery = ordersQuery.Where(o => o.CreatedAt >= fromVal);
                positionsQuery = positionsQuery.Where(p => p.OpenedAt >= fromVal);
                tradesQuery = tradesQuery.Where(t => (t.ClosedAt ?? t.ExecutedAt) >= fromVal);
            }

            if (query.To.HasValue)
            {
                var toVal = query.To.Value;
                ordersQuery = ordersQuery.Where(o => o.CreatedAt <= toVal);
                positionsQuery = positionsQuery.Where(p => p.OpenedAt <= toVal);
                tradesQuery = tradesQuery.Where(t => (t.ClosedAt ?? t.ExecutedAt) <= toVal);
            }

            // 1. Orders Summary
            var orderCounts = await ordersQuery
                .GroupBy(o => o.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var totalOrders = orderCounts.Sum(x => x.Count);
            var openOrders = orderCounts.Where(x => x.Status != OrderStatus.Filled &&
                                                    x.Status != OrderStatus.Cancelled &&
                                                    x.Status != OrderStatus.Rejected &&
                                                    x.Status != OrderStatus.Failed &&
                                                    x.Status != OrderStatus.Expired &&
                                                    x.Status != OrderStatus.ValidationFailed)
                                        .Sum(x => x.Count);
            var filledOrders = orderCounts.Where(x => x.Status == OrderStatus.Filled).Sum(x => x.Count);
            var cancelledOrders = orderCounts.Where(x => x.Status == OrderStatus.Cancelled).Sum(x => x.Count);
            var rejectedOrders = orderCounts.Where(x => x.Status == OrderStatus.Rejected).Sum(x => x.Count);
            var failedOrders = orderCounts.Where(x => x.Status == OrderStatus.Failed || x.Status == OrderStatus.ValidationFailed).Sum(x => x.Count);

            var ordersSummary = new TradingOrderSummaryDto(
                TotalOrders: totalOrders,
                OpenOrders: openOrders,
                FilledOrders: filledOrders,
                CancelledOrders: cancelledOrders,
                RejectedOrders: rejectedOrders,
                FailedOrders: failedOrders
            );

            // 2. Positions Summary (only for Open/PartiallyClosed/Pending positions)
            var openPositionsData = await positionsQuery
                .Where(p => p.Status == PositionStatus.Open ||
                            p.Status == PositionStatus.PartiallyClosed ||
                            p.Status == PositionStatus.Pending)
                .Select(p => new { p.Side, p.RemainingQuantity, p.UnrealizedPnL })
                .ToListAsync(cancellationToken);

            var openPositionCount = openPositionsData.Count;
            var longPositionCount = openPositionsData.Count(p => p.Side == OrderSide.Buy);
            var shortPositionCount = openPositionsData.Count(p => p.Side == OrderSide.Sell);
            var totalOpenQuantity = openPositionsData.Sum(p => p.RemainingQuantity);
            var totalUnrealizedPnL = openPositionsData.Sum(p => p.UnrealizedPnL);

            var positionsSummary = new TradingPositionSummaryDto(
                OpenPositionCount: openPositionCount,
                LongPositionCount: longPositionCount,
                ShortPositionCount: shortPositionCount,
                TotalOpenQuantity: totalOpenQuantity,
                TotalUnrealizedPnL: totalUnrealizedPnL
            );

            // 3. Trades, PnL, Fees & Performance Summary
            var tradesData = await tradesQuery
                .Select(t => new { t.ProfitLoss, t.Fee, t.NetPnL })
                .ToListAsync(cancellationToken);

            var totalTrades = tradesData.Count;
            var winningTrades = tradesData.Count(t => (t.NetPnL != null ? t.NetPnL.Value > 0 : (t.ProfitLoss ?? 0m) > 0));
            var losingTrades = tradesData.Count(t => (t.NetPnL != null ? t.NetPnL.Value < 0 : (t.ProfitLoss ?? 0m) < 0));
            var breakEvenTrades = tradesData.Count(t => (t.NetPnL != null ? t.NetPnL.Value == 0 : (t.ProfitLoss ?? 0m) == 0));
            var winRate = totalTrades > 0 ? (decimal)winningTrades / totalTrades * 100m : 0m;

            var grossPnLValue = tradesData.Sum(t => t.ProfitLoss ?? 0m);
            var totalFeesValue = tradesData.Sum(t => t.Fee);
            var netPnLValue = tradesData.Sum(t => t.NetPnL ?? (t.ProfitLoss ?? 0m) - t.Fee);

            var tradesSummary = new TradingTradeSummaryDto(
                TotalTrades: totalTrades,
                WinningTrades: winningTrades,
                LosingTrades: losingTrades,
                BreakEvenTrades: breakEvenTrades,
                WinRate: winRate
            );

            var performanceSummary = new TradingPerformanceSummaryDto(
                TotalTrades: totalTrades,
                WinningTrades: winningTrades,
                LosingTrades: losingTrades,
                WinRate: winRate,
                GrossPnL: grossPnLValue,
                Fees: totalFeesValue,
                NetPnL: netPnLValue
            );

            var pnlSummary = new TradingPnlSummaryDto(
                GrossPnL: grossPnLValue,
                TotalFees: totalFeesValue,
                NetPnL: netPnLValue
            );

            var feeSummary = new TradingFeeSummaryDto(
                TotalFees: totalFeesValue
            );

            // 4. Pagination limits
            int page = query.Page <= 0 ? 1 : query.Page;
            int pageSize = query.PageSize <= 0 ? 50 : query.PageSize;
            if (pageSize > 100)
            {
                pageSize = 100;
            }

            // 5. Open Positions paginated list
            var openPositionsListQuery = positionsQuery
                .Where(p => p.Status == PositionStatus.Open ||
                            p.Status == PositionStatus.PartiallyClosed ||
                            p.Status == PositionStatus.Pending)
                .OrderByDescending(p => p.OpenedAt);

            var totalOpenPositionsCount = await openPositionsListQuery.CountAsync(cancellationToken);
            var openPositionsList = await openPositionsListQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new TradingPositionDto(
                    p.Id,
                    p.Symbol,
                    p.Side,
                    p.Quantity,
                    p.RemainingQuantity,
                    p.EntryPrice,
                    p.CurrentPrice,
                    p.StopLoss,
                    p.TakeProfit,
                    p.Leverage,
                    p.UnrealizedPnL,
                    p.OpenedAt,
                    p.UpdatedAt,
                    p.Status
                ))
                .ToListAsync(cancellationToken);

            var openPositionsPaged = new PagedResult<TradingPositionDto>(openPositionsList, totalOpenPositionsCount, page, pageSize);

            // 6. Active Orders paginated list
            var activeOrdersListQuery = ordersQuery
                .Where(o => o.Status != OrderStatus.Filled &&
                            o.Status != OrderStatus.Cancelled &&
                            o.Status != OrderStatus.Rejected &&
                            o.Status != OrderStatus.Failed &&
                            o.Status != OrderStatus.Expired &&
                            o.Status != OrderStatus.ValidationFailed)
                .OrderByDescending(o => o.CreatedAt);

            var totalActiveOrdersCount = await activeOrdersListQuery.CountAsync(cancellationToken);
            var activeOrdersList = await activeOrdersListQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(o => new TradingOrderDto(
                    o.Id,
                    o.Symbol.Value,
                    o.Side,
                    o.Type,
                    o.Quantity.Value,
                    o.Price.Amount,
                    o.Status,
                    o.CreatedAt,
                    o.UpdatedAt
                ))
                .ToListAsync(cancellationToken);

            var activeOrdersPaged = new PagedResult<TradingOrderDto>(activeOrdersList, totalActiveOrdersCount, page, pageSize);

            // 7. Recent Trades paginated list
            var recentTradesListQuery = tradesQuery
                .OrderByDescending(t => t.ClosedAt ?? t.ExecutedAt);

            var totalRecentTradesCount = await recentTradesListQuery.CountAsync(cancellationToken);
            var recentTradesList = await recentTradesListQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new TradingTradeDto(
                    t.Id,
                    t.PositionId,
                    t.Symbol,
                    t.Side == SignalType.Buy ? OrderSide.Buy : OrderSide.Sell,
                    t.EntryPrice,
                    t.ExitPrice,
                    t.Quantity,
                    t.ProfitLoss ?? 0m,
                    t.Fee,
                    t.NetPnL ?? (t.ProfitLoss ?? 0m) - t.Fee,
                    t.CloseReason,
                    t.OpenedAt,
                    t.ClosedAt ?? t.ExecutedAt
                ))
                .ToListAsync(cancellationToken);

            var recentTradesPaged = new PagedResult<TradingTradeDto>(recentTradesList, totalRecentTradesCount, page, pageSize);

            return new TradingDashboardOverviewDto(
                Orders: ordersSummary,
                Positions: positionsSummary,
                Trades: tradesSummary,
                Performance: performanceSummary,
                Pnl: pnlSummary,
                Fees: feeSummary,
                OpenPositions: openPositionsPaged,
                ActiveOrders: activeOrdersPaged,
                RecentTrades: recentTradesPaged
            );
        }
        catch (Exception ex) when (ex is not DatabaseException && ex is not ArgumentException)
        {
            throw new DatabaseException("An error occurred while executing the read-only trading dashboard queries. See inner exception for details.", ex);
        }
    }
}
