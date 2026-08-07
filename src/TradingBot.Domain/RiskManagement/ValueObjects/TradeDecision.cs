using System;
using TradingBot.Domain.RiskManagement.Enums;

namespace TradingBot.Domain.RiskManagement.ValueObjects;

public record TradeDecision
{
    public RiskDecisionStatus Decision { get; init; }
    public bool Approved => Decision == RiskDecisionStatus.Approved;
    public bool Rejected => Decision == RiskDecisionStatus.Rejected;
    public bool NeedsReview => Decision == RiskDecisionStatus.NeedsReview || Decision == RiskDecisionStatus.NeedsManualReview;
    public bool NeedsManualReview => Decision == RiskDecisionStatus.NeedsManualReview;
    public string Reason { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
