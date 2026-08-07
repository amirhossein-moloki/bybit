using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Application.RiskManagement.Configuration;

namespace TradingBot.Application.RiskManagement.Rules;

public class DrawdownRule : IRiskRule
{
    private readonly RiskManagementOptions _options;

    public DrawdownRule(IOptions<RiskManagementOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<RiskRuleResult> EvaluateAsync(TradeRiskContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        decimal currentDrawdownPercent = context.DailyPnL < 0
            ? (-context.DailyPnL / context.AccountBalance) * 100m
            : 0m;

        bool passed = currentDrawdownPercent <= _options.MaximumDrawdown;

        return Task.FromResult(new RiskRuleResult
        {
            RuleName = nameof(DrawdownRule),
            Passed = passed,
            Severity = RiskRuleSeverity.Critical,
            Message = passed
                ? $"Current drawdown ({currentDrawdownPercent:F2}%) is within the limit of {_options.MaximumDrawdown}%."
                : $"Current drawdown ({currentDrawdownPercent:F2}%) exceeds the maximum allowed drawdown of {_options.MaximumDrawdown}%."
        });
    }
}
