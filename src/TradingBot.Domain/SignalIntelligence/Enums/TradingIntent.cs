namespace TradingBot.Domain.SignalIntelligence.Enums;

public enum TradingIntent
{
    RISK_FREE,
    RISK_FREE_ALL,
    CANCEL,
    CANCEL_PENDING_ORDERS,
    CLOSE,
    CLOSE_ALL,
    UPDATE_STOP_LOSS,
    UPDATE_TAKE_PROFIT,
    SIGNAL_UPDATE,
    REPLACE_SIGNAL,
    RESTORE_ORDER,
    RE_ENTRY,
    STATUS_REPORT,
    NO_ACTION,
    UNKNOWN
}
