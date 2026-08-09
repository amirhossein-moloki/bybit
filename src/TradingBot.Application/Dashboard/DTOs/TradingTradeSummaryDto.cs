namespace TradingBot.Application.Dashboard.DTOs;

public sealed record TradingTradeSummaryDto(
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    int BreakEvenTrades,
    decimal WinRate
);
