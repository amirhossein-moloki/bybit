using System.Threading.Tasks;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Domain.RiskManagement.ValueObjects;

namespace TradingBot.Application.RiskManagement.Interfaces;

public interface IRiskRule
{
    Task<RiskRuleResult> EvaluateAsync(TradeRiskContext context);
}
