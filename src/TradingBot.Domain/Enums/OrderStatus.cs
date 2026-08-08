namespace TradingBot.Domain.Enums;

public enum OrderStatus
{
    Created,
    Submitted,
    Accepted,
    PartiallyFilled,
    Filled,
    Cancelled,
    Rejected,
    Pending,
    New,
    Failed,
    ValidationFailed,
    ReadyForExchange,
    Unknown,
    Expired,
    Submitting
}
