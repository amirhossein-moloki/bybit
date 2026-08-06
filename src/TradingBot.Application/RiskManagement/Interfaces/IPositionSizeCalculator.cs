using TradingBot.Domain.RiskManagement.ValueObjects;

namespace TradingBot.Application.RiskManagement.Interfaces;

public interface IPositionSizeCalculator
{
    decimal Calculate(TradeRiskContext context);
}
