using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Domain.RiskManagement.Enums;

namespace TradingBot.Application.RiskManagement.Engine;

public class RiskRuleExecutor
{
    private readonly ILogger<RiskRuleExecutor> _logger;

    public RiskRuleExecutor(ILogger<RiskRuleExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RiskRuleResult> ExecuteRuleAsync(IRiskRule rule, TradeRiskContext context)
    {
        if (rule == null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        string ruleName = rule.GetType().Name;
        _logger.LogInformation("Rule Started: {RuleName}", ruleName);

        try
        {
            var result = await rule.EvaluateAsync(context);
            if (result.Passed)
            {
                if (result.Severity == RiskRuleSeverity.Warning)
                {
                    _logger.LogWarning("Rule Warning: {RuleName}. {Message}", ruleName, result.Message);
                }
                else
                {
                    _logger.LogInformation("Rule Passed: {RuleName}. {Message}", ruleName, result.Message);
                }
            }
            else
            {
                _logger.LogWarning("Rule Failed: {RuleName}. Severity: {Severity}. {Message}", ruleName, result.Severity, result.Message);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rule Exception: {RuleName} failed during evaluation.", ruleName);
            return new RiskRuleResult
            {
                RuleName = ruleName,
                Passed = false,
                Severity = RiskRuleSeverity.Critical,
                Message = $"Rule Failed due to exception: {ex.Message}"
            };
        }
    }
}
