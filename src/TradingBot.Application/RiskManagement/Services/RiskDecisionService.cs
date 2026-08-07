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

        // Check for Critical and Error severity failures first
        var criticalOrErrorFailures = failedRules
            .Where(r => r.Severity == RiskRuleSeverity.Critical || r.Severity == RiskRuleSeverity.Error)
            .ToList();

        if (criticalOrErrorFailures.Count > 0)
        {
            var reasons = string.Join("; ", criticalOrErrorFailures.Select(r => r.Message));
            return new TradeDecision
            {
                Decision = RiskDecisionStatus.Rejected,
                Reason = $"Risk rules failed (Critical/Error): {reasons}",
                CreatedAt = DateTime.UtcNow
            };
        }

        // Check for Warning severity failures
        var warningFailures = failedRules
            .Where(r => r.Severity == RiskRuleSeverity.Warning)
            .ToList();

        if (warningFailures.Count > 0)
        {
            var reasons = string.Join("; ", warningFailures.Select(r => r.Message));
            return new TradeDecision
            {
                Decision = RiskDecisionStatus.NeedsReview,
                Reason = $"Risk rules failed (Warning): {reasons}",
                CreatedAt = DateTime.UtcNow
            };
        }

        // Must be Info severity failures
        var infoFailures = failedRules
            .Where(r => r.Severity == RiskRuleSeverity.Info)
            .ToList();

        var infoReasons = string.Join("; ", infoFailures.Select(r => r.Message));
        return new TradeDecision
        {
            Decision = RiskDecisionStatus.NeedsReview,
            Reason = $"Risk rules failed (Info): {infoReasons}",
            CreatedAt = DateTime.UtcNow
        };
    }
}
