using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Analytics.DTOs;
using TradingBot.Application.Analytics.Interfaces;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Analytics.Services;

public class PerformanceAnalyticsService : IPerformanceAnalyticsService
{
    private readonly IPerformanceAnalyticsQueryService _queryService;
    private readonly DrawdownCalculator _drawdownCalculator;
    private readonly StreakCalculator _streakCalculator;
    private readonly PnLCalculator _pnlCalculator;

    public PerformanceAnalyticsService(
        IPerformanceAnalyticsQueryService queryService,
        DrawdownCalculator drawdownCalculator,
        StreakCalculator streakCalculator,
        PnLCalculator pnlCalculator)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _drawdownCalculator = drawdownCalculator ?? throw new ArgumentNullException(nameof(drawdownCalculator));
        _streakCalculator = streakCalculator ?? throw new ArgumentNullException(nameof(streakCalculator));
        _pnlCalculator = pnlCalculator ?? throw new ArgumentNullException(nameof(pnlCalculator));
    }

    public async Task<PerformanceMetricsDto> GetPerformanceMetricsAsync(
        GetAnalyticsQuery query,
        CancellationToken cancellationToken = default)
    {
        var orderedTrades = await _queryService.GetCompletedTradesAsync(query, cancellationToken);
        int totalTrades = orderedTrades.Count;

        if (totalTrades == 0)
        {
            return new PerformanceMetricsDto(
                TotalTrades: 0,
                WinningTrades: 0,
                LosingTrades: 0,
                BreakevenTrades: 0,
                WinRate: 0m,
                LossRate: 0m,
                AverageWin: 0m,
                AverageLoss: 0m,
                LargestWin: 0m,
                LargestLoss: 0m,
                AverageTradePnL: 0m,
                ProfitFactor: 0m,
                GrossProfit: 0m,
                GrossLoss: 0m,
                NetPnL: 0m
            );
        }

        var netPnLs = orderedTrades.Select(t => t.NetPnL ?? (t.ProfitLoss ?? 0m) - t.Fee).ToList();

        int winningTrades = 0;
        int losingTrades = 0;
        int breakevenTrades = 0;

        decimal grossProfit = 0m;
        decimal grossLoss = 0m;
        decimal netPnLSum = 0m;

        decimal largestWin = 0m;
        decimal largestLoss = 0m;

        foreach (var pnl in netPnLs)
        {
            netPnLSum += pnl;

            if (pnl > 0)
            {
                winningTrades++;
                grossProfit += pnl;
                if (pnl > largestWin)
                {
                    largestWin = pnl;
                }
            }
            else if (pnl < 0)
            {
                losingTrades++;
                decimal absLoss = Math.Abs(pnl);
                grossLoss += absLoss;
                if (absLoss > largestLoss)
                {
                    largestLoss = absLoss;
                }
            }
            else
            {
                breakevenTrades++;
            }
        }

        decimal winRate = (decimal)winningTrades / totalTrades * 100m;
        decimal lossRate = (decimal)losingTrades / totalTrades * 100m;

        decimal averagePnL = netPnLSum / totalTrades;
        decimal averageWin = winningTrades > 0 ? grossProfit / winningTrades : 0m;
        decimal averageLoss = losingTrades > 0 ? grossLoss / losingTrades : 0m;

        decimal profitFactor = _pnlCalculator.CalculateProfitFactor(grossProfit, grossLoss);

        return new PerformanceMetricsDto(
            TotalTrades: totalTrades,
            WinningTrades: winningTrades,
            LosingTrades: losingTrades,
            BreakevenTrades: breakevenTrades,
            WinRate: winRate,
            LossRate: lossRate,
            AverageWin: averageWin,
            AverageLoss: averageLoss,
            LargestWin: largestWin,
            LargestLoss: largestLoss,
            AverageTradePnL: averagePnL,
            ProfitFactor: profitFactor,
            GrossProfit: grossProfit,
            GrossLoss: grossLoss,
            NetPnL: netPnLSum
        );
    }

    public async Task<DrawdownMetricsDto> GetDrawdownMetricsAsync(
        GetAnalyticsQuery query,
        CancellationToken cancellationToken = default)
    {
        var orderedTrades = await _queryService.GetCompletedTradesAsync(query, cancellationToken);
        var netPnLs = orderedTrades.Select(t => t.NetPnL ?? (t.ProfitLoss ?? 0m) - t.Fee).ToList();
        decimal initialBalance = query.InitialBalance ?? 10000m;

        return _drawdownCalculator.Calculate(netPnLs, initialBalance);
    }

    public async Task<StreakMetricsDto> GetStreakMetricsAsync(
        GetAnalyticsQuery query,
        CancellationToken cancellationToken = default)
    {
        var orderedTrades = await _queryService.GetCompletedTradesAsync(query, cancellationToken);
        var netPnLs = orderedTrades.Select(t => t.NetPnL ?? (t.ProfitLoss ?? 0m) - t.Fee).ToList();

        return _streakCalculator.Calculate(netPnLs);
    }

    public async Task<DurationMetricsDto> GetDurationMetricsAsync(
        GetAnalyticsQuery query,
        CancellationToken cancellationToken = default)
    {
        var orderedTrades = await _queryService.GetCompletedTradesAsync(query, cancellationToken);

        var validDurations = new List<TimeSpan>();
        var winningDurations = new List<TimeSpan>();
        var losingDurations = new List<TimeSpan>();

        foreach (var trade in orderedTrades)
        {
            if (trade.OpenedAt.HasValue && trade.ClosedAt.HasValue)
            {
                var duration = trade.ClosedAt.Value - trade.OpenedAt.Value;
                if (duration >= TimeSpan.Zero)
                {
                    validDurations.Add(duration);
                    decimal pnl = trade.NetPnL ?? (trade.ProfitLoss ?? 0m) - trade.Fee;
                    if (pnl > 0)
                    {
                        winningDurations.Add(duration);
                    }
                    else if (pnl < 0)
                    {
                        losingDurations.Add(duration);
                    }
                }
            }
        }

        TimeSpan? averageDuration = validDurations.Count > 0
            ? TimeSpan.FromTicks((long)validDurations.Average(d => d.Ticks))
            : null;

        TimeSpan? shortestDuration = validDurations.Count > 0
            ? validDurations.Min()
            : null;

        TimeSpan? longestDuration = validDurations.Count > 0
            ? validDurations.Max()
            : null;

        TimeSpan? averageWinningDuration = winningDurations.Count > 0
            ? TimeSpan.FromTicks((long)winningDurations.Average(d => d.Ticks))
            : null;

        TimeSpan? averageLosingDuration = losingDurations.Count > 0
            ? TimeSpan.FromTicks((long)losingDurations.Average(d => d.Ticks))
            : null;

        return new DurationMetricsDto(
            AverageDuration: averageDuration,
            ShortestDuration: shortestDuration,
            LongestDuration: longestDuration,
            AverageWinningDuration: averageWinningDuration,
            AverageLosingDuration: averageLosingDuration
        );
    }

    public async Task<LongShortPerformanceDto> GetLongShortPerformanceAsync(
        GetAnalyticsQuery query,
        CancellationToken cancellationToken = default)
    {
        var orderedTrades = await _queryService.GetCompletedTradesAsync(query, cancellationToken);

        var longTrades = orderedTrades.Where(t => t.Side == OrderSide.Buy).ToList();
        var shortTrades = orderedTrades.Where(t => t.Side == OrderSide.Sell).ToList();

        var longDto = CalculateSidePerformance(longTrades);
        var shortDto = CalculateSidePerformance(shortTrades);

        return new LongShortPerformanceDto(Long: longDto, Short: shortDto);
    }

    private SidePerformanceDto CalculateSidePerformance(List<AnalyticsTradeDto> trades)
    {
        int total = trades.Count;
        if (total == 0)
        {
            return new SidePerformanceDto(
                Trades: 0,
                Wins: 0,
                Losses: 0,
                WinRate: 0m,
                TotalPnL: 0m,
                AveragePnL: 0m
            );
        }

        int wins = 0;
        int losses = 0;
        decimal totalPnL = 0m;

        foreach (var t in trades)
        {
            decimal pnl = t.NetPnL ?? (t.ProfitLoss ?? 0m) - t.Fee;
            totalPnL += pnl;

            if (pnl > 0)
            {
                wins++;
            }
            else if (pnl < 0)
            {
                losses++;
            }
        }

        decimal winRate = (decimal)wins / total * 100m;
        decimal averagePnL = totalPnL / total;

        return new SidePerformanceDto(
            Trades: total,
            Wins: wins,
            Losses: losses,
            WinRate: winRate,
            TotalPnL: totalPnL,
            AveragePnL: averagePnL
        );
    }
}
