using System;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.Enums;

namespace TradingBot.Application.Trading.Execution.Models;

public class TradeExecutionRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SignalId { get; set; }
    public Guid RiskEvaluationId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public OrderSide Side { get; set; }
    public OrderType OrderType { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Leverage { get; set; }
    public RiskDecisionStatus RiskDecision { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Direct mappings for Section 5 Contracts
    public Guid RiskApprovalId
    {
        get => RiskEvaluationId;
        set => RiskEvaluationId = value;
    }

    public Guid ExecutionId
    {
        get => Id;
        set => Id = value;
    }
}
