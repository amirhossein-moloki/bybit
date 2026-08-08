using TradingBot.Application.Interfaces;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Services;

public class PnLCalculator : IPnLCalculator
{
    public decimal CalculateGrossPnL(OrderSide side, decimal entryPrice, decimal exitPrice, decimal quantity)
    {
        if (side == OrderSide.Buy) // LONG
        {
            return (exitPrice - entryPrice) * quantity;
        }
        else // SHORT
        {
            return (entryPrice - exitPrice) * quantity;
        }
    }

    public decimal CalculateNetPnL(decimal grossPnL, decimal tradingFee, decimal fundingFee, decimal otherCosts = 0m)
    {
        return grossPnL - tradingFee - fundingFee - otherCosts;
    }
}
