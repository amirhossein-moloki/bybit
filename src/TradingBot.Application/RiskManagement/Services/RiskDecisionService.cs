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
        try
        {
            if (results == null)
            {
                return new TradeDecision
                {
                    Decision = RiskDecisionStatus.NeedsManualReview,
                    Reason = "Unexpected Error: Results collection is null.",
                    CreatedAt = DateTime.UtcNow
                };
            }

            var ruleList = results.ToList();

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

            // Deterministic priority logic:
            // 1. Critical/Error Rule Failed -> Rejected
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

            // 2. Only Warnings -> Approved
            // (meaning any failed rules are Warning or Info severity)
            var warningOrInfoFailures = failedRules
                .Where(r => r.Severity == RiskRuleSeverity.Warning || r.Severity == RiskRuleSeverity.Info)
                .ToList();

            if (warningOrInfoFailures.Count > 0)
            {
                var reasons = string.Join("; ", warningOrInfoFailures.Select(r => r.Message));
                return new TradeDecision
                {
                    Decision = RiskDecisionStatus.Approved,
                    Reason = $"Approved with warnings/info: {reasons}",
                    CreatedAt = DateTime.UtcNow
                };
            }

            return new TradeDecision
            {
                Decision = RiskDecisionStatus.Approved,
                Reason = "Approved.",
                CreatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new TradeDecision
            {
                Decision = RiskDecisionStatus.NeedsManualReview,
                Reason = $"Unexpected Error inside Decision Generation: {ex.Message}",
                CreatedAt = DateTime.UtcNow
            };
        }
    }
}
