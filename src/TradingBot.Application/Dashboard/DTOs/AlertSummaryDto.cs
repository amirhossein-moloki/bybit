namespace TradingBot.Application.Dashboard.DTOs;

public sealed record AlertSummaryDto(
    int ActiveAlertCount,
    int CriticalAlertCount,
    int ErrorAlertCount,
    int WarningAlertCount,
    int InfoAlertCount
);
