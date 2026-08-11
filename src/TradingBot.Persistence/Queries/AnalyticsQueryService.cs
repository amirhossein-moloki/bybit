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

public class AnalyticsQueryService : IAnalyticsQueryService
{
    private readonly TradingDbContext _dbContext;

    public AnalyticsQueryService(TradingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<TradeStatisticsDto> GetTradeStatisticsAsync(
        GetTradeStatisticsQuery query,
        CancellationToken cancellationToken = default)
    {
        // 1. Date validation
        if (query.From.HasValue && query.To.HasValue && query.From.Value > query.To.Value)
        {
            throw new ArgumentException("The 'From' date must be less than or equal to the 'To' date.");
        }

        // 2. Query construction
        // Only finalized completed trades participate (must have ClosedAt and PositionId)
        var baseQuery = from t in _dbContext.Trades.AsNoTracking()
                        join p in _dbContext.Positions.AsNoTracking() on t.PositionId equals p.Id into joined
                        from p in joined.DefaultIfEmpty()
                        where t.ClosedAt != null && t.PositionId != null
                        select new {
                            t.Id,
                            t.NetPnL,
                            t.ProfitLoss,
                            t.Fee,
                            t.OpenedAt,
                            t.ClosedAt,
                            Symbol = p != null ? p.Symbol : t.Symbol,
                            Side = p != null ? p.Side : (t.Side == SignalType.Buy ? OrderSide.Buy : OrderSide.Sell)
                        };

        // Filter by symbol if provided
        if (!string.IsNullOrWhiteSpace(query.Symbol))
        {
            var sym = query.Symbol.Trim().ToUpperInvariant();
            baseQuery = baseQuery.Where(t => t.Symbol == sym);
        }

        // Filter by side if provided
        if (query.Side.HasValue)
        {
            var side = query.Side.Value;
            baseQuery = baseQuery.Where(t => t.Side == side);
        }

        // Filter by date range (ClosedAt) [from, to)
        if (query.From.HasValue)
        {
            baseQuery = baseQuery.Where(t => t.ClosedAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            baseQuery = baseQuery.Where(t => t.ClosedAt < query.To.Value);
        }

        // 3. Materialize minimal projected properties
        var rawTrades = await baseQuery
            .Select(t => new
            {
                t.Id,
                t.NetPnL,
                t.ProfitLoss,
                t.Fee,
                t.OpenedAt,
                t.ClosedAt
            })
            .ToListAsync(cancellationToken);

        // 4. Stable chronological sorting
        var orderedTrades = rawTrades
            .OrderBy(t => t.ClosedAt)
            .ThenBy(t => t.Id)
            .ToList();

        // 5. Statistics Calculation
        int totalTrades = orderedTrades.Count;

        if (totalTrades == 0)
        {
            return new TradeStatisticsDto(
                TotalTrades: 0,
                WinningTrades: 0,
                LosingTrades: 0,
                BreakevenTrades: 0,
                WinRate: 0m,
                LossRate: 0m,
                GrossProfit: 0m,
                GrossLoss: 0m,
                NetPnL: 0m,
                AveragePnL: 0m,
                AverageWin: 0m,
                AverageLoss: 0m,
                LargestWin: 0m,
                LargestLoss: 0m,
                ProfitFactor: 0m,
                AverageDuration: null,
                ShortestDuration: null,
                LongestDuration: null,
                CurrentWinStreak: 0,
                CurrentLossStreak: 0,
                MaximumWinStreak: 0,
                MaximumLossStreak: 0
            );
        }

        int winningTrades = 0;
        int losingTrades = 0;
        int breakevenTrades = 0;

        decimal grossProfit = 0m;
        decimal grossLoss = 0m; // Represented as positive magnitude
        decimal netPnL = 0m;

        decimal largestWin = 0m;
        decimal largestLoss = 0m; // Represented as positive magnitude

        // For duration calculation
        var validDurations = new List<TimeSpan>();

        // For streak analysis
        int currentWinStreak = 0;
        int currentLossStreak = 0;
        int maxWinStreak = 0;
        int maxLossStreak = 0;

        foreach (var trade in orderedTrades)
        {
            decimal tradeNetPnL = trade.NetPnL ?? (trade.ProfitLoss ?? 0m) - trade.Fee;
            netPnL += tradeNetPnL;

            if (tradeNetPnL > 0)
            {
                winningTrades++;
                grossProfit += tradeNetPnL;
                if (tradeNetPnL > largestWin)
                {
                    largestWin = tradeNetPnL;
                }

                currentWinStreak++;
                currentLossStreak = 0;
                if (currentWinStreak > maxWinStreak)
                {
                    maxWinStreak = currentWinStreak;
                }
            }
            else if (tradeNetPnL < 0)
            {
                losingTrades++;
                decimal absLoss = Math.Abs(tradeNetPnL);
                grossLoss += absLoss;
                if (absLoss > largestLoss)
                {
                    largestLoss = absLoss;
                }

                currentLossStreak++;
                currentWinStreak = 0;
                if (currentLossStreak > maxLossStreak)
                {
                    maxLossStreak = currentLossStreak;
                }
            }
            else // tradeNetPnL == 0
            {
                breakevenTrades++;

                currentWinStreak = 0;
                currentLossStreak = 0;
            }

            if (trade.OpenedAt.HasValue && trade.ClosedAt.HasValue)
            {
                var duration = trade.ClosedAt.Value - trade.OpenedAt.Value;
                if (duration >= TimeSpan.Zero)
                {
                    validDurations.Add(duration);
                }
            }
        }

        decimal winRate = (decimal)winningTrades / totalTrades * 100m;
        decimal lossRate = (decimal)losingTrades / totalTrades * 100m;

        decimal averagePnL = netPnL / totalTrades;
        decimal averageWin = winningTrades > 0 ? grossProfit / winningTrades : 0m;
        decimal averageLoss = losingTrades > 0 ? grossLoss / losingTrades : 0m;

        decimal profitFactor = grossLoss > 0m ? grossProfit / grossLoss : 0m;

        TimeSpan? averageDuration = null;
        TimeSpan? shortestDuration = null;
        TimeSpan? longestDuration = null;

        if (validDurations.Count > 0)
        {
            double averageTicks = validDurations.Average(d => d.Ticks);
            averageDuration = TimeSpan.FromTicks((long)averageTicks);
            shortestDuration = validDurations.Min();
            longestDuration = validDurations.Max();
        }

        return new TradeStatisticsDto(
            TotalTrades: totalTrades,
            WinningTrades: winningTrades,
            LosingTrades: losingTrades,
            BreakevenTrades: breakevenTrades,
            WinRate: winRate,
            LossRate: lossRate,
            GrossProfit: grossProfit,
            GrossLoss: grossLoss,
            NetPnL: netPnL,
            AveragePnL: averagePnL,
            AverageWin: averageWin,
            AverageLoss: averageLoss,
            LargestWin: largestWin,
            LargestLoss: largestLoss,
            ProfitFactor: profitFactor,
            AverageDuration: averageDuration,
            ShortestDuration: shortestDuration,
            LongestDuration: longestDuration,
            CurrentWinStreak: currentWinStreak,
            CurrentLossStreak: currentLossStreak,
            MaximumWinStreak: maxWinStreak,
            MaximumLossStreak: maxLossStreak
        );
    }
}
