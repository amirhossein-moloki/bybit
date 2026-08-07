using System;
using TradingBot.Domain.Entities;
using TradingBot.Domain.RiskManagement.ValueObjects;

namespace TradingBot.Application.RiskManagement.Workflow;

public class RiskWorkflowContext
{
    public Signal Signal { get; }
    public TradeRiskContext TradeRiskContext { get; }

    public RiskWorkflowContext(Signal signal, TradeRiskContext tradeRiskContext)
    {
        Signal = signal ?? throw new ArgumentNullException(nameof(signal));
        TradeRiskContext = tradeRiskContext ?? throw new ArgumentNullException(nameof(tradeRiskContext));
    }
}
