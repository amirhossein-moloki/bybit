namespace TradingBot.Application.Trading.Execution.Enums;

public enum ExchangeErrorType
{
    InvalidRequest,
    InsufficientBalance,
    AuthenticationFailed,
    RateLimited,
    Unavailable,
    Unknown
}
