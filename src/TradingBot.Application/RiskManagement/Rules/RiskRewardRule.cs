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

public class RiskRewardRule : IRiskRule
{
    private readonly RiskManagementOptions _options;
    private readonly RiskCalculationService _calculationService;

    public RiskRewardRule(
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
        bool passed = calcResult.RiskReward >= _options.MinimumRiskReward;

        return Task.FromResult(new RiskRuleResult
        {
            RuleName = nameof(RiskRewardRule),
            Passed = passed,
            Severity = RiskRuleSeverity.Error,
            Message = passed
                ? $"Calculated risk/reward ratio ({calcResult.RiskReward:F2}) is above or equal to the minimum of {_options.MinimumRiskReward:F2}."
                : $"Calculated risk/reward ratio ({calcResult.RiskReward:F2}) is below the required minimum of {_options.MinimumRiskReward:F2}."
        });
    }
}
