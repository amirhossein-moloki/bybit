using System;
using System.Collections.Generic;
using System.Linq;
using TradingBot.Application.RiskManagement.Exceptions;
using TradingBot.Application.RiskManagement.Configuration;
using TradingBot.Domain.Enums;
using Microsoft.Extensions.Options;

namespace TradingBot.Application.RiskManagement.Calculators;

public class RiskRewardCalculator
{
    private readonly RiskCalculationOptions _options;

    public RiskRewardCalculator(IOptions<RiskCalculationOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public decimal Calculate(decimal risk, decimal reward)
    {
        if (risk == 0)
        {
            throw new RiskManagementException("Reject Calculation: Risk distance is zero.");
        }

        if (risk < 0)
        {
            throw new RiskManagementException("Reject Calculation: Risk distance is negative.");
        }

        if (reward < 0)
        {
            throw new RiskManagementException("Reject Calculation: Reward distance is negative.");
        }

        decimal rr = reward / risk;
        return decimal.Round(rr, _options.RoundingPrecision, MidpointRounding.AwayFromZero);
    }

    public decimal Calculate(OrderSide side, decimal entryPrice, decimal stopLoss, decimal takeProfit)
    {
        decimal risk;
        decimal reward;

        if (side == OrderSide.Buy)
        {
            risk = entryPrice - stopLoss;
            reward = takeProfit - entryPrice;
        }
        else if (side == OrderSide.Sell)
        {
            risk = stopLoss - entryPrice;
            reward = entryPrice - takeProfit;
        }
        else
        {
            throw new RiskManagementException("Calculation Failed: Unsupported order side.");
        }

        return Calculate(risk, reward);
    }

    public decimal CalculateFirstTp(OrderSide side, decimal entryPrice, decimal stopLoss, IReadOnlyList<decimal> takeProfits)
    {
        if (takeProfits == null || takeProfits.Count == 0)
        {
            throw new RiskManagementException("Cannot Calculate Risk Reward: No take profits provided.");
        }

        decimal firstTp = takeProfits[0];
        return Calculate(side, entryPrice, stopLoss, firstTp);
    }

    public decimal CalculateAverageTp(OrderSide side, decimal entryPrice, decimal stopLoss, IReadOnlyList<decimal> takeProfits)
    {
        if (takeProfits == null || takeProfits.Count == 0)
        {
            throw new RiskManagementException("Cannot Calculate Risk Reward: No take profits provided.");
        }

        decimal averageTp = takeProfits.Average();
        return Calculate(side, entryPrice, stopLoss, averageTp);
    }
}
