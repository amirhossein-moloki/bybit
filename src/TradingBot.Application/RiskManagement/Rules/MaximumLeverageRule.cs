using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Application.RiskManagement.Configuration;

namespace TradingBot.Application.RiskManagement.Rules;

public class MaximumLeverageRule : IRiskRule
{
    private readonly RiskManagementOptions _options;

    public MaximumLeverageRule(IOptions<RiskManagementOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<RiskRuleResult> EvaluateAsync(TradeRiskContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        int signalLeverage = context.Leverage ?? 1;
        bool exceeds = signalLeverage > _options.MaximumLeverage;

        if (!exceeds)
        {
            return Task.FromResult(new RiskRuleResult
            {
                RuleName = nameof(MaximumLeverageRule),
                Passed = true,
                Severity = RiskRuleSeverity.Info,
                Message = $"Signal leverage ({signalLeverage}x) is within the maximum limit of {_options.MaximumLeverage}x."
            });
        }

        if (_options.AutoReduceLeverage)
        {
            return Task.FromResult(new RiskRuleResult
            {
                RuleName = nameof(MaximumLeverageRule),
                Passed = true,
                Severity = RiskRuleSeverity.Warning,
                Message = $"Signal leverage ({signalLeverage}x) exceeds limit of {_options.MaximumLeverage}x. Automatically reduced leverage to {_options.MaximumLeverage}x."
            });
        }

        return Task.FromResult(new RiskRuleResult
        {
            RuleName = nameof(MaximumLeverageRule),
            Passed = false,
            Severity = RiskRuleSeverity.Error,
            Message = $"Signal leverage ({signalLeverage}x) exceeds the maximum allowed limit of {_options.MaximumLeverage}x and auto-reduce is disabled."
        });
    }
}
