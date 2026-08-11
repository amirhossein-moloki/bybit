namespace TradingBot.Domain.SignalIntelligence.Enums;

public enum MessageType
{
    UNKNOWN = 0,
    SIGNAL = 1,
    TRADE_UPDATE = 2,
    CANCEL_COMMAND = 3,
    ANALYSIS = 4,
    STATUS_UPDATE = 5,
    GENERAL_MESSAGE = 6
}
