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

public class MarginAvailabilityRule : IRiskRule
{
    private readonly RiskManagementOptions _options;
    private readonly RiskCalculationService _calculationService;

    public MarginAvailabilityRule(
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
        decimal availableBalance = context.AccountBalance - context.CurrentExposure;
        bool passed = calcResult.RequiredMargin <= availableBalance;

        return Task.FromResult(new RiskRuleResult
        {
            RuleName = nameof(MarginAvailabilityRule),
            Passed = passed,
            Severity = RiskRuleSeverity.Critical,
            Message = passed
                ? $"Required margin of {calcResult.RequiredMargin} USDT is available within free balance of {availableBalance} USDT (Balance: {context.AccountBalance} - Exposure: {context.CurrentExposure})."
                : $"Insufficient margin. Required margin is {calcResult.RequiredMargin} USDT, but available balance is only {availableBalance} USDT (Balance: {context.AccountBalance} - Exposure: {context.CurrentExposure})."
        });
    }
}
