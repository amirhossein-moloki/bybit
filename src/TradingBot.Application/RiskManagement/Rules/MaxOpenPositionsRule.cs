using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Application.RiskManagement.Configuration;

namespace TradingBot.Application.RiskManagement.Rules;

public class MaxOpenPositionsRule : IRiskRule
{
    private readonly RiskManagementOptions _options;

    public MaxOpenPositionsRule(IOptions<RiskManagementOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<RiskRuleResult> EvaluateAsync(TradeRiskContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        bool passed = context.OpenPositions < _options.MaxOpenPositions;

        return Task.FromResult(new RiskRuleResult
        {
            RuleName = nameof(MaxOpenPositionsRule),
            Passed = passed,
            Severity = RiskRuleSeverity.Error,
            Message = passed
                ? $"Current open positions ({context.OpenPositions}) are below the limit of {_options.MaxOpenPositions}."
                : $"Cannot open a new position. Current open positions ({context.OpenPositions}) are at or above the maximum limit of {_options.MaxOpenPositions}."
        });
    }
}
