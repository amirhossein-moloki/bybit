using System.Collections.Generic;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Domain.RiskManagement.ValueObjects;

namespace TradingBot.Application.RiskManagement.Interfaces;

public interface IRiskDecisionService
{
    TradeDecision CreateDecision(IEnumerable<RiskRuleResult> results);
}
