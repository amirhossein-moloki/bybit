using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.Entities;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Infrastructure.RiskManagement.Configuration;
using TradingBot.Infrastructure.RiskManagement.Services;
using Xunit;

namespace TradingBot.UnitTests.RiskManagement;

public class RiskManagementTests
{
    [Fact]
    public void RiskProfile_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var profile = new RiskProfile();

        // Assert
        profile.Should().NotBeNull();
        profile.Id.Should().NotBeEmpty();
        profile.Name.Should().Be("Balanced");
        profile.MaxRiskPerTrade.Should().Be(1.0m);
        profile.MaxOpenPositions.Should().Be(5);
        profile.MaxLeverage.Should().Be(10);
        profile.MinimumRiskReward.Should().Be(2.0m);
        profile.CreatedAt.Should().BeBefore(DateTime.UtcNow.AddSeconds(1));
        profile.UpdatedAt.Should().BeBefore(DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void TradeRiskContext_ShouldMapDataCorrectly()
    {
        // Arrange
        var signalId = Guid.NewGuid();
        var takeProfits = new List<decimal> { 46000m, 47000m };

        // Act
        var context = new TradeRiskContext
        {
            SignalId = signalId,
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 45000m,
            StopLoss = 44000m,
            TakeProfits = takeProfits,
            Leverage = 10,
            AccountBalance = 10000m,
            OpenPositions = 2,
            DailyPnL = 150m,
            CurrentExposure = 5000m,
            CreatedAt = DateTime.UtcNow
        };

        // Assert
        context.SignalId.Should().Be(signalId);
        context.Symbol.Should().Be("BTCUSDT");
        context.Side.Should().Be(OrderSide.Buy);
        context.EntryPrice.Should().Be(45000m);
        context.StopLoss.Should().Be(44000m);
        context.TakeProfits.Should().Equal(takeProfits);
        context.Leverage.Should().Be(10);
        context.AccountBalance.Should().Be(10000m);
        context.OpenPositions.Should().Be(2);
        context.DailyPnL.Should().Be(150m);
        context.CurrentExposure.Should().Be(5000m);
    }

    [Theory]
    [InlineData(RiskDecisionStatus.Approved, true, false, false)]
    [InlineData(RiskDecisionStatus.Rejected, false, true, false)]
    [InlineData(RiskDecisionStatus.NeedsReview, false, false, true)]
    public void TradeDecision_ShouldReturnCorrectStatusHelpers(
        RiskDecisionStatus status,
        bool expectedApproved,
        bool expectedRejected,
        bool expectedNeedsReview)
    {
        // Arrange & Act
        var decision = new TradeDecision
        {
            Decision = status,
            Reason = "Test reason"
        };

        // Assert
        decision.Decision.Should().Be(status);
        decision.Approved.Should().Be(expectedApproved);
        decision.Rejected.Should().Be(expectedRejected);
        decision.NeedsReview.Should().Be(expectedNeedsReview);
        decision.Reason.Should().Be("Test reason");
    }

    [Fact]
    public void RiskDecisionService_ShouldReturnApproved_WhenNoRulesProvided()
    {
        // Arrange
        var service = new RiskDecisionService();

        // Act
        var decision = service.CreateDecision(Enumerable.Empty<RiskRuleResult>());

        // Assert
        decision.Approved.Should().BeTrue();
        decision.Decision.Should().Be(RiskDecisionStatus.Approved);
        decision.Reason.Should().Contain("No risk rules executed");
    }

    [Fact]
    public void RiskDecisionService_ShouldReturnApproved_WhenAllRulesPass()
    {
        // Arrange
        var service = new RiskDecisionService();
        var results = new List<RiskRuleResult>
        {
            new() { RuleName = "Rule1", Passed = true, Severity = RiskLevel.Low, Message = "Passed" },
            new() { RuleName = "Rule2", Passed = true, Severity = RiskLevel.High, Message = "Passed" }
        };

        // Act
        var decision = service.CreateDecision(results);

        // Assert
        decision.Approved.Should().BeTrue();
        decision.Decision.Should().Be(RiskDecisionStatus.Approved);
        decision.Reason.Should().Contain("All risk rules passed");
    }

    [Theory]
    [InlineData(RiskLevel.Critical)]
    [InlineData(RiskLevel.High)]
    public void RiskDecisionService_ShouldReturnRejected_WhenAnyHighOrCriticalRuleFails(RiskLevel severity)
    {
        // Arrange
        var service = new RiskDecisionService();
        var results = new List<RiskRuleResult>
        {
            new() { RuleName = "Rule1", Passed = true, Severity = RiskLevel.Low, Message = "Passed" },
            new() { RuleName = "Rule2", Passed = false, Severity = severity, Message = "Leverage exceeded limit" }
        };

        // Act
        var decision = service.CreateDecision(results);

        // Assert
        decision.Rejected.Should().BeTrue();
        decision.Decision.Should().Be(RiskDecisionStatus.Rejected);
        decision.Reason.Should().Contain("Leverage exceeded limit");
    }

    [Theory]
    [InlineData(RiskLevel.Medium)]
    [InlineData(RiskLevel.Low)]
    public void RiskDecisionService_ShouldReturnNeedsReview_WhenNoHighCriticalFailedButMediumOrLowFails(RiskLevel severity)
    {
        // Arrange
        var service = new RiskDecisionService();
        var results = new List<RiskRuleResult>
        {
            new() { RuleName = "Rule1", Passed = true, Severity = RiskLevel.High, Message = "Passed" },
            new() { RuleName = "Rule2", Passed = false, Severity = severity, Message = "Moderate exposure warning" }
        };

        // Act
        var decision = service.CreateDecision(results);

        // Assert
        decision.NeedsReview.Should().BeTrue();
        decision.Decision.Should().Be(RiskDecisionStatus.NeedsReview);
        decision.Reason.Should().Contain("Moderate exposure warning");
    }

    [Fact]
    public async Task RiskEngineService_ShouldEvaluateCorrectly_WithRegisteredRules()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<RiskEngineService>>();
        var options = Options.Create(new RiskManagementOptions { Enabled = true });
        var decisionServiceMock = new Mock<IRiskDecisionService>();

        var context = new TradeRiskContext { SignalId = Guid.NewGuid(), Symbol = "BTCUSDT" };

        var mockRule1 = new Mock<IRiskRule>();
        mockRule1.Setup(r => r.EvaluateAsync(context))
            .ReturnsAsync(new RiskRuleResult { RuleName = "Rule1", Passed = true, Severity = RiskLevel.Low });

        var rules = new List<IRiskRule> { mockRule1.Object };

        var expectedDecision = new TradeDecision { Decision = RiskDecisionStatus.Approved, Reason = "Passed in mock" };
        decisionServiceMock.Setup(d => d.CreateDecision(It.IsAny<IEnumerable<RiskRuleResult>>()))
            .Returns(expectedDecision);

        // Act
        var engine = new RiskEngineService(loggerMock.Object, options, decisionServiceMock.Object, rules);
        var decision = await engine.EvaluateAsync(context);

        // Assert
        decision.Should().Be(expectedDecision);

        // Verify Logging with strict nullability matching
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v != null && v.ToString()!.Contains("Risk Engine Initialized")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v != null && v.ToString()!.Contains("Risk Configuration Loaded")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v != null && v.ToString()!.Contains("Risk Evaluation Started")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RiskEngineService_ShouldReturnApproved_WhenDisabledInOptions()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<RiskEngineService>>();
        var options = Options.Create(new RiskManagementOptions { Enabled = false });
        var decisionServiceMock = new Mock<IRiskDecisionService>();
        var rules = new List<IRiskRule>();

        var context = new TradeRiskContext { SignalId = Guid.NewGuid(), Symbol = "BTCUSDT" };

        // Act
        var engine = new RiskEngineService(loggerMock.Object, options, decisionServiceMock.Object, rules);
        var decision = await engine.EvaluateAsync(context);

        // Assert
        decision.Approved.Should().BeTrue();
        decision.Decision.Should().Be(RiskDecisionStatus.Approved);
        decision.Reason.Should().Contain("Risk management is disabled in configuration.");
    }
}
