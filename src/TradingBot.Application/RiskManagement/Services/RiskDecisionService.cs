using System;
using System.Collections.Generic;
using System.Linq;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Domain.RiskManagement.ValueObjects;

namespace TradingBot.Application.RiskManagement.Services;

public class RiskDecisionService : IRiskDecisionService
{
    public TradeDecision CreateDecision(IEnumerable<RiskRuleResult> results)
    {
        var ruleList = results?.ToList() ?? new List<RiskRuleResult>();

        if (ruleList.Count == 0)
        {
            return new TradeDecision
            {
                Decision = RiskDecisionStatus.Approved,
                Reason = "No risk rules executed.",
                CreatedAt = DateTime.UtcNow
            };
        }

        var failedRules = ruleList.Where(r => !r.Passed).ToList();

        if (failedRules.Count == 0)
        {
            return new TradeDecision
            {
                Decision = RiskDecisionStatus.Approved,
                Reason = "All risk rules passed.",
                CreatedAt = DateTime.UtcNow
            };
        }

        // Check for Critical and High severity failures first
        var highOrCriticalFailures = failedRules
            .Where(r => r.Severity == RiskLevel.Critical || r.Severity == RiskLevel.High)
            .ToList();

        if (highOrCriticalFailures.Count > 0)
        {
            var reasons = string.Join("; ", highOrCriticalFailures.Select(r => r.Message));
            return new TradeDecision
            {
                Decision = RiskDecisionStatus.Rejected,
                Reason = $"Risk rules failed (High/Critical): {reasons}",
                CreatedAt = DateTime.UtcNow
            };
        }

        // Check for Medium severity failures
        var mediumFailures = failedRules
            .Where(r => r.Severity == RiskLevel.Medium)
            .ToList();

        if (mediumFailures.Count > 0)
        {
            var reasons = string.Join("; ", mediumFailures.Select(r => r.Message));
            return new TradeDecision
            {
                Decision = RiskDecisionStatus.NeedsReview,
                Reason = $"Risk rules failed (Medium): {reasons}",
                CreatedAt = DateTime.UtcNow
            };
        }

        // Must be Low severity failures
        var lowFailures = failedRules
            .Where(r => r.Severity == RiskLevel.Low)
            .ToList();

        var lowReasons = string.Join("; ", lowFailures.Select(r => r.Message));
        return new TradeDecision
        {
            Decision = RiskDecisionStatus.NeedsReview,
            Reason = $"Risk rules failed (Low): {lowReasons}",
            CreatedAt = DateTime.UtcNow
        };
    }
}
