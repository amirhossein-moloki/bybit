namespace TradingBot.Application.Dashboard.DTOs;

public sealed record TradeSummaryDto(
    int TotalTrades,
    int WinningTrades,
    int LosingTrades
);
