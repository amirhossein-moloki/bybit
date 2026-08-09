namespace TradingBot.Application.Dashboard.DTOs;

public sealed record OrderSummaryDto(
    int TotalOrders,
    int OpenOrders,
    int FilledOrders,
    int CancelledOrders,
    int FailedOrders
);
