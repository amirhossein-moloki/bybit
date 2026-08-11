namespace TradingBot.Domain.SignalIntelligence.Enums;

public enum TradeAction
{
    NONE = 0,
    MOVE_STOP_TO_ENTRY = 1,
    CLOSE_PARTIAL = 2,
    CLOSE_POSITION = 3,
    UPDATE_STOP_LOSS = 4,
    UPDATE_TAKE_PROFIT = 5,
    CANCEL = 6
}
