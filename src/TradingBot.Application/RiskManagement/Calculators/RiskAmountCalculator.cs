using System;
using TradingBot.Application.RiskManagement.Exceptions;

namespace TradingBot.Application.RiskManagement.Calculators;

public class RiskAmountCalculator
{
    public decimal Calculate(decimal balance, decimal riskPercent)
    {
        if (balance <= 0)
        {
            throw new RiskManagementException("Calculation Failed: Missing or invalid account balance.");
        }

        if (riskPercent < 0)
        {
            throw new RiskManagementException("Invalid Configuration: Invalid risk percentage.");
        }

        return balance * (riskPercent / 100m);
    }
}
