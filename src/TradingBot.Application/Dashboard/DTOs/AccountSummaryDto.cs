namespace TradingBot.Application.Dashboard.DTOs;

public sealed record AccountSummaryDto(
    decimal? Equity,
    decimal? Balance,
    decimal? AvailableBalance,
    decimal? UsedMargin,
    decimal? UnrealizedPnL
);
