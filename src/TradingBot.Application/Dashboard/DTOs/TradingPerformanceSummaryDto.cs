namespace TradingBot.Application.Dashboard.DTOs;

public sealed record TradingPerformanceSummaryDto(
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    decimal WinRate,
    decimal GrossPnL,
    decimal Fees,
    decimal NetPnL
);
