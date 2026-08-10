using System;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record TradeStatisticsDto(
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    int BreakevenTrades,
    decimal WinRate,
    decimal LossRate,
    decimal GrossProfit,
    decimal GrossLoss,
    decimal NetPnL,
    decimal AveragePnL,
    decimal AverageWin,
    decimal AverageLoss,
    decimal LargestWin,
    decimal LargestLoss,
    decimal ProfitFactor,
    TimeSpan? AverageDuration,
    TimeSpan? ShortestDuration,
    TimeSpan? LongestDuration,
    int CurrentWinStreak,
    int CurrentLossStreak,
    int MaximumWinStreak,
    int MaximumLossStreak
);
