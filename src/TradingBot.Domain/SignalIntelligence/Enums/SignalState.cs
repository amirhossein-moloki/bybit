namespace TradingBot.Domain.SignalIntelligence.Enums;

public enum SignalState
{
    RECEIVED = 0,
    ANALYZING = 1,
    VALIDATED = 2,
    WAITING_ENTRY = 3,
    ACTIVE = 4,
    MANAGED = 5,
    RISK_FREE = 6,
    PARTIAL_CLOSE = 7,
    TARGET_REACHED = 8,
    CLOSED = 9,
    CANCELLED = 10
}
