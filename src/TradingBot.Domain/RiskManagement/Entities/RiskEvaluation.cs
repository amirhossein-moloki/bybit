using System;
using TradingBot.Domain.RiskManagement.Enums;

namespace TradingBot.Domain.RiskManagement.Entities;

public class RiskEvaluation
{
    public Guid Id { get; set; }
    public Guid SignalId { get; set; }
    public decimal RiskAmount { get; set; }
    public decimal PositionSize { get; set; }
    public decimal RiskReward { get; set; }
    public decimal Exposure { get; set; }
    public RiskDecisionStatus Decision { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public RiskEvaluation()
    {
        Id = Guid.NewGuid();
    }
}
