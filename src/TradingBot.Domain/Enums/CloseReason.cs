namespace TradingBot.Domain.Enums;

public enum CloseReason
{
    StopLoss,
    TakeProfit,
    Manual,
    Signal,
    Liquidation,
    Exchange,
    Unknown
}
