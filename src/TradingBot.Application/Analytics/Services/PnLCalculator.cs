using System;

namespace TradingBot.Application.Analytics.Services;

public class PnLCalculator
{
    public decimal CalculateProfitFactor(decimal grossProfit, decimal grossLoss)
    {
        return grossLoss > 0m ? grossProfit / grossLoss : 0m;
    }
}
