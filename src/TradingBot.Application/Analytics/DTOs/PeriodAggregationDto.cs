using System;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record PeriodAggregationDto(
    string PeriodLabel,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    decimal WinRate,
    decimal GrossProfit,
    decimal GrossLoss,
    decimal NetPnL,
    decimal TotalFees
);
