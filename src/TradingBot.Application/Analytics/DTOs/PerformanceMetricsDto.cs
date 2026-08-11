using System;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record PerformanceMetricsDto(
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    int BreakevenTrades,
    decimal WinRate,
    decimal LossRate,
    decimal AverageWin,
    decimal AverageLoss,
    decimal LargestWin,
    decimal LargestLoss,
    decimal AverageTradePnL,
    decimal ProfitFactor,
    decimal GrossProfit,
    decimal GrossLoss,
    decimal NetPnL
);
