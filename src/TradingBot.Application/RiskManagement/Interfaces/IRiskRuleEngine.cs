using System.Threading.Tasks;
using TradingBot.Domain.RiskManagement.Entities;
using TradingBot.Domain.RiskManagement.ValueObjects;

namespace TradingBot.Application.RiskManagement.Interfaces;

public interface IRiskRuleEngine
{
    Task<RiskEvaluation> EvaluateAsync(TradeRiskContext context);
}
