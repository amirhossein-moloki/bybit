namespace TradingBot.Domain.Enums;

public enum SignalStatus
{
    Received,
    Parsing,
    Parsed,
    Validated,
    ReadyForRiskEngine,
    Rejected,
    Executed,
    RiskEvaluationStarted,
    RiskEvaluated,
    TradeApproved,
    TradeRejected,
    ManualReview
}
