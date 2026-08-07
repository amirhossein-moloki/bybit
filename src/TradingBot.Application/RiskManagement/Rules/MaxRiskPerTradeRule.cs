using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Application.RiskManagement.Configuration;

namespace TradingBot.Application.RiskManagement.Rules;

public class MaxRiskPerTradeRule : IRiskRule
{
    private readonly RiskManagementOptions _options;
    private readonly RiskCalculationService _calculationService;

    public MaxRiskPerTradeRule(
        IOptions<RiskManagementOptions> options,
        RiskCalculationService calculationService)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _calculationService = calculationService ?? throw new ArgumentNullException(nameof(calculationService));
    }

    public Task<RiskRuleResult> EvaluateAsync(TradeRiskContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var calcResult = _calculationService.Calculate(context);
        decimal maxAllowedRiskAmount = context.AccountBalance * (_options.MaxRiskPerTrade / 100m);

        bool passed = calcResult.RiskAmount <= maxAllowedRiskAmount;

        return Task.FromResult(new RiskRuleResult
        {
            RuleName = nameof(MaxRiskPerTradeRule),
            Passed = passed,
            Severity = RiskRuleSeverity.Error,
            Message = passed
                ? $"Calculated risk of {calcResult.RiskAmount} USDT is within the allowed limit of {maxAllowedRiskAmount} USDT ({_options.MaxRiskPerTrade}%)."
                : $"Calculated risk of {calcResult.RiskAmount} USDT exceeds the maximum allowed limit of {maxAllowedRiskAmount} USDT ({_options.MaxRiskPerTrade}%)."
        });
    }
}
