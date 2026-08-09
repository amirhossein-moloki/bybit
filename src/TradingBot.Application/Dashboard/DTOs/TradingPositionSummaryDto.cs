namespace TradingBot.Application.Dashboard.DTOs;

public sealed record TradingPositionSummaryDto(
    int OpenPositionCount,
    int LongPositionCount,
    int ShortPositionCount,
    decimal TotalOpenQuantity,
    decimal TotalUnrealizedPnL
);
