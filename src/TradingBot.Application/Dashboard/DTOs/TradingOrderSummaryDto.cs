namespace TradingBot.Application.Dashboard.DTOs;

public sealed record TradingOrderSummaryDto(
    int TotalOrders,
    int OpenOrders,
    int FilledOrders,
    int CancelledOrders,
    int RejectedOrders,
    int FailedOrders
);
