namespace TradingBot.Application.Dashboard.DTOs;

public sealed record PnlSummaryDto(
    decimal RealizedPnL,
    decimal TotalFees,
    decimal NetPnL
);
