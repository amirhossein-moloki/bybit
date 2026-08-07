using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Application.RiskManagement.Configuration;

namespace TradingBot.Application.RiskManagement.Rules;

public class DailyLossRule : IRiskRule
{
    private readonly RiskManagementOptions _options;

    public DailyLossRule(IOptions<RiskManagementOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<RiskRuleResult> EvaluateAsync(TradeRiskContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        decimal maxDailyLossAmount = context.AccountBalance * (_options.MaximumDailyLoss / 100m);
        bool passed = context.DailyPnL > -maxDailyLossAmount;

        return Task.FromResult(new RiskRuleResult
        {
            RuleName = nameof(DailyLossRule),
            Passed = passed,
            Severity = RiskRuleSeverity.Critical,
            Message = passed
                ? $"Today's PnL ({context.DailyPnL} USDT) is above the maximum allowed daily loss limit of -{maxDailyLossAmount} USDT ({_options.MaximumDailyLoss}%)."
                : $"Trading Disabled. Today's PnL ({context.DailyPnL} USDT) has hit or exceeded the maximum allowed daily loss limit of -{maxDailyLossAmount} USDT ({_options.MaximumDailyLoss}%)."
        });
    }
}
