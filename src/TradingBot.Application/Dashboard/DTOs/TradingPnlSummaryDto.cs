namespace TradingBot.Application.Dashboard.DTOs;

public sealed record TradingPnlSummaryDto(
    decimal GrossPnL,
    decimal TotalFees,
    decimal NetPnL
);
