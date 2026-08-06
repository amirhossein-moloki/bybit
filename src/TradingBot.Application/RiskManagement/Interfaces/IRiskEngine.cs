using System.Threading.Tasks;
using TradingBot.Domain.RiskManagement.ValueObjects;

namespace TradingBot.Application.RiskManagement.Interfaces;

public interface IRiskEngine
{
    Task<TradeDecision> EvaluateAsync(TradeRiskContext context);
}
