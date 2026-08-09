namespace TradingBot.Domain.Enums;

public enum NotificationStatus
{
    Pending,
    Processing,
    Delivered,
    RetryScheduled,
    Failed,
    Cancelled
}
