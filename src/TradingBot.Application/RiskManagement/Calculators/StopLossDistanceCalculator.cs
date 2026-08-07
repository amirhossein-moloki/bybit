using System;
using TradingBot.Application.RiskManagement.Exceptions;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.RiskManagement.Calculators;

public class StopLossDistanceCalculator
{
    public decimal Calculate(OrderSide side, decimal entryPrice, decimal? stopLoss)
    {
        if (!stopLoss.HasValue)
        {
            throw new RiskManagementException("Cannot Calculate Risk: Missing stop loss.");
        }

        if (entryPrice <= 0)
        {
            throw new RiskManagementException("Calculation Failed: Invalid entry price.");
        }

        decimal distance;
        if (side == OrderSide.Buy)
        {
            distance = entryPrice - stopLoss.Value;
        }
        else if (side == OrderSide.Sell)
        {
            distance = stopLoss.Value - entryPrice;
        }
        else
        {
            throw new RiskManagementException("Calculation Failed: Unsupported order side.");
        }

        if (distance == 0)
        {
            throw new RiskManagementException("Reject Calculation: Stop loss distance is zero.");
        }

        if (distance < 0)
        {
            throw new RiskManagementException("Reject Calculation: Stop loss distance is negative.");
        }

        return distance;
    }
}
