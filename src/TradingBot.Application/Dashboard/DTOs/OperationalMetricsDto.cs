namespace TradingBot.Application.Dashboard.DTOs;

public sealed record OperationalMetricsDto(
    long OrdersSubmitted,
    long OrdersFilled,
    long OrdersFailed,
    long MessagesReceived,
    long MessagesProcessed,
    long MessagesFailed,
    long NotificationsSent,
    long NotificationsFailed,
    long ErrorCount,
    long WarningCount,
    long ApiRequestsCount
);
