using System;
using TradingBot.Application.RiskManagement.Calculators;
using TradingBot.Application.RiskManagement.Exceptions;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Application.RiskManagement.Configuration;
using TradingBot.Domain.RiskManagement.ValueObjects;
using Microsoft.Extensions.Options;

namespace TradingBot.Application.RiskManagement.Services;

public class RiskCalculationService
{
    private readonly RiskAmountCalculator _riskAmountCalculator;
    private readonly StopLossDistanceCalculator _stopLossDistanceCalculator;
    private readonly PositionSizeCalculator _positionSizeCalculator;
    private readonly RiskRewardCalculator _riskRewardCalculator;
    private readonly RiskCalculationOptions _options;

    public RiskCalculationService(
        RiskAmountCalculator riskAmountCalculator,
        StopLossDistanceCalculator stopLossDistanceCalculator,
        PositionSizeCalculator positionSizeCalculator,
        RiskRewardCalculator riskRewardCalculator,
        IOptions<RiskCalculationOptions> options)
    {
        _riskAmountCalculator = riskAmountCalculator ?? throw new ArgumentNullException(nameof(riskAmountCalculator));
        _stopLossDistanceCalculator = stopLossDistanceCalculator ?? throw new ArgumentNullException(nameof(stopLossDistanceCalculator));
        _positionSizeCalculator = positionSizeCalculator ?? throw new ArgumentNullException(nameof(positionSizeCalculator));
        _riskRewardCalculator = riskRewardCalculator ?? throw new ArgumentNullException(nameof(riskRewardCalculator));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public RiskCalculationResult Calculate(TradeRiskContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        // 1. Validations
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

        // 2. Perform Calculations
        decimal riskAmount = _riskAmountCalculator.Calculate(context.AccountBalance, _options.DefaultRiskPercent);

        decimal stopLossDistance = _stopLossDistanceCalculator.Calculate(context.Side, context.EntryPrice, context.StopLoss.Value);

        decimal positionSize = _positionSizeCalculator.Calculate(context);

        decimal riskReward = 0m;
        if (context.TakeProfits != null && context.TakeProfits.Count > 0)
        {
            riskReward = _riskRewardCalculator.CalculateAverageTp(context.Side, context.EntryPrice, context.StopLoss.Value, context.TakeProfits);
        }

        // Required Margin
        int leverage = context.Leverage ?? 1;
        if (leverage <= 0)
        {
            leverage = 1;
        }
        decimal requiredMargin = (positionSize * context.EntryPrice) / leverage;
        requiredMargin = decimal.Round(requiredMargin, _options.RoundingPrecision, MidpointRounding.AwayFromZero);

        return new RiskCalculationResult
        {
            RiskAmount = decimal.Round(riskAmount, _options.RoundingPrecision, MidpointRounding.AwayFromZero),
            PositionSize = positionSize,
            StopLossDistance = decimal.Round(stopLossDistance, _options.RoundingPrecision, MidpointRounding.AwayFromZero),
            RiskReward = riskReward,
            RequiredMargin = requiredMargin
        };
    }
}
