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

public class MaximumExposureRule : IRiskRule
{
    private readonly RiskManagementOptions _options;
    private readonly RiskCalculationService _calculationService;

    public MaximumExposureRule(
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
        decimal newPositionExposure = calcResult.PositionSize * context.EntryPrice;
        decimal totalExposure = context.CurrentExposure + newPositionExposure;
        decimal maxAllowedExposure = context.AccountBalance * (_options.MaximumExposure / 100m);

        bool passed = totalExposure <= maxAllowedExposure;

        return Task.FromResult(new RiskRuleResult
        {
            RuleName = nameof(MaximumExposureRule),
            Passed = passed,
            Severity = RiskRuleSeverity.Error,
            Message = passed
                ? $"Total exposure of {totalExposure} USDT is within the maximum limit of {maxAllowedExposure} USDT ({_options.MaximumExposure}%)."
                : $"Total exposure would be {totalExposure} USDT (Current: {context.CurrentExposure} + New: {newPositionExposure}), which exceeds the limit of {maxAllowedExposure} USDT ({_options.MaximumExposure}%)."
        });
    }
}
