namespace TradingBot.Application.Dashboard.DTOs;

public sealed record PositionSummaryDto(
    int OpenPositionCount,
    int LongPositionCount,
    int ShortPositionCount
);
