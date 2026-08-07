using System;
using System.Collections.Generic;
using FluentAssertions;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Domain.RiskManagement.Enums;
using Xunit;

namespace TradingBot.UnitTests.RiskManagement;

public class RiskDecisionServiceTests
{
    private readonly RiskDecisionService _decisionService = new();

    [Fact]
    public void CreateDecision_ShouldReturnApproved_WhenNoRulesExecuted()
    {
        // Arrange & Act
        var decision = _decisionService.CreateDecision(new List<RiskRuleResult>());

        // Assert
        decision.Decision.Should().Be(RiskDecisionStatus.Approved);
        decision.Reason.Should().Contain("No risk rules executed.");
    }

    [Fact]
    public void CreateDecision_ShouldReturnRejected_WhenCriticalOrErrorRuleFails()
    {
        // Arrange
        var results = new List<RiskRuleResult>
        {
            new() { RuleName = "MaxRiskPerTradeRule", Passed = false, Message = "Exceeded max risk per trade", Severity = RiskRuleSeverity.Critical },
            new() { RuleName = "MaxOpenPositionsRule", Passed = true, Message = "Open positions limit ok", Severity = RiskRuleSeverity.Warning }
        };

        // Act
        var decision = _decisionService.CreateDecision(results);

        // Assert
        decision.Decision.Should().Be(RiskDecisionStatus.Rejected);
        decision.Reason.Should().Contain("Exceeded max risk per trade");
    }

    [Fact]
    public void CreateDecision_ShouldReturnApproved_WhenOnlyWarningOrInfoRulesFail()
    {
        // Arrange
        var results = new List<RiskRuleResult>
        {
            new() { RuleName = "MaxOpenPositionsRule", Passed = false, Message = "Open positions limit warning", Severity = RiskRuleSeverity.Warning },
            new() { RuleName = "MinimumRiskRewardRule", Passed = false, Message = "Low Risk Reward info level", Severity = RiskRuleSeverity.Info }
        };

        // Act
        var decision = _decisionService.CreateDecision(results);

        // Assert
        decision.Decision.Should().Be(RiskDecisionStatus.Approved);
        decision.Reason.Should().Contain("Approved with warnings/info");
    }

    [Fact]
    public void CreateDecision_ShouldReturnNeedsManualReview_WhenNullResultsPassed()
    {
        // Arrange & Act
        var decision = _decisionService.CreateDecision(null!);

        // Assert
        decision.Decision.Should().Be(RiskDecisionStatus.NeedsManualReview);
        decision.Reason.Should().Contain("Unexpected Error");
    }
}
