using TradingBot.Domain.Enums;

namespace TradingBot.Application.Interfaces;

public interface IPnLCalculator
{
    decimal CalculateGrossPnL(OrderSide side, decimal entryPrice, decimal exitPrice, decimal quantity);
    decimal CalculateNetPnL(decimal grossPnL, decimal tradingFee, decimal fundingFee, decimal otherCosts = 0m);
}
