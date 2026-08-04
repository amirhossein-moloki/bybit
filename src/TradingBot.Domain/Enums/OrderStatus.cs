namespace TradingBot.Domain.Enums;

public enum OrderStatus
{
    Created,
    Submitted,
    Accepted,
    PartiallyFilled,
    Filled,
    Cancelled,
    Rejected
}
