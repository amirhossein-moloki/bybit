using System;
using TradingBot.Domain.RiskManagement.Enums;

namespace TradingBot.Domain.RiskManagement.Entities;

public class TradeDecision
{
    public Guid Id { get; set; }
    public Guid SignalId { get; set; }
    public RiskDecisionStatus Decision { get; set; }
    public string DecisionReason { get; set; } = string.Empty;
    public Guid RiskEvaluationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public TradeDecision()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
    }
}
