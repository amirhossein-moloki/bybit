using System;
using TradingBot.Application.RiskManagement.Exceptions;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Configuration;
using TradingBot.Domain.RiskManagement.ValueObjects;
using Microsoft.Extensions.Options;

namespace TradingBot.Application.RiskManagement.Calculators;

public class PositionSizeCalculator : IPositionSizeCalculator
{
    private readonly RiskAmountCalculator _riskAmountCalculator;
    private readonly StopLossDistanceCalculator _stopLossDistanceCalculator;
    private readonly RiskCalculationOptions _options;

    public PositionSizeCalculator(
        RiskAmountCalculator riskAmountCalculator,
        StopLossDistanceCalculator stopLossDistanceCalculator,
        IOptions<RiskCalculationOptions> options)
    {
        _riskAmountCalculator = riskAmountCalculator ?? throw new ArgumentNullException(nameof(riskAmountCalculator));
        _stopLossDistanceCalculator = stopLossDistanceCalculator ?? throw new ArgumentNullException(nameof(stopLossDistanceCalculator));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public decimal Calculate(TradeRiskContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.AccountBalance <= 0)
        {
            throw new RiskManagementException("Calculation Failed: Missing or invalid account balance.");
        }

        if (_options.DefaultRiskPercent < 0)
        {
            throw new RiskManagementException("Invalid Configuration: Invalid risk percentage.");
        }

        if (!context.StopLoss.HasValue)
        {
            throw new RiskManagementException("Cannot Calculate Risk: Missing stop loss.");
        }

        decimal riskAmount = _riskAmountCalculator.Calculate(context.AccountBalance, _options.DefaultRiskPercent);
        decimal stopLossDistance = _stopLossDistanceCalculator.Calculate(context.Side, context.EntryPrice, context.StopLoss);

        return Calculate(riskAmount, stopLossDistance);
    }

    public decimal Calculate(decimal riskAmount, decimal stopLossDistance)
    {
        if (stopLossDistance == 0)
        {
            throw new RiskManagementException("Reject Calculation: Stop loss distance is zero.");
        }

        if (stopLossDistance < 0)
        {
            throw new RiskManagementException("Reject Calculation: Stop loss distance is negative.");
        }

        decimal positionSize = riskAmount / stopLossDistance;

        return decimal.Round(positionSize, _options.RoundingPrecision, MidpointRounding.AwayFromZero);
    }
}
