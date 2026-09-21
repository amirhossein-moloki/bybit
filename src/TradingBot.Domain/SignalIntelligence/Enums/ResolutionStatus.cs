namespace TradingBot.Domain.SignalIntelligence.Enums;

public enum ResolutionStatus
{
    Resolved = 0,
    Ambiguous = 1,
    InsufficientContext = 2,
    NotTradingRelated = 3,
    Invalid = 4
}
