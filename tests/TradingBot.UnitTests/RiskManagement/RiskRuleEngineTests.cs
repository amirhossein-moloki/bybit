using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Application.RiskManagement.Engine;
using TradingBot.Application.RiskManagement.Exceptions;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Application.RiskManagement.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Application.RiskManagement.Calculators;
using Xunit;

namespace TradingBot.UnitTests.RiskManagement;

public class RiskRuleEngineTests
{
    private readonly RiskCalculationService _calculationService;

    public RiskRuleEngineTests()
    {
        var calcOptions = Options.Create(new RiskCalculationOptions
        {
            DefaultRiskPercent = 2.0m,
            RoundingPrecision = 8
        });

        var riskAmountCalc = new RiskAmountCalculator();
        var stopLossDistanceCalc = new StopLossDistanceCalculator();
        var positionSizeCalc = new PositionSizeCalculator(riskAmountCalc, stopLossDistanceCalc, calcOptions);
        var riskRewardCalc = new RiskRewardCalculator(calcOptions);

        _calculationService = new RiskCalculationService(
            riskAmountCalc,
            stopLossDistanceCalc,
            positionSizeCalc,
            riskRewardCalc,
            calcOptions
        );
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenConfigurationMissing()
    {
        // Arrange
        IOptions<RiskManagementOptions>? options = null;

        // Act & Assert
        Assert.Throws<RiskManagementException>(() => new RiskRuleEngine(
            NullLogger<RiskRuleEngine>.Instance,
            options!,
            Enumerable.Empty<IRiskRule>(),
            new RiskRuleExecutor(NullLogger<RiskRuleExecutor>.Instance),
            new RiskDecisionService(),
            _calculationService
        ));
    }

    [Fact]
    public async Task EvaluateAsync_ShouldReturnNeedsReview_WhenContextIsInvalid()
    {
        // Arrange
        var context = new TradeRiskContext
        {
            SignalId = Guid.NewGuid(),
            Symbol = "", // Invalid
            AccountBalance = 0m // Invalid
        };

        var options = Options.Create(new RiskManagementOptions { DefaultProfile = "Balanced" });
        var engine = new RiskRuleEngine(
            NullLogger<RiskRuleEngine>.Instance,
            options,
            Enumerable.Empty<IRiskRule>(),
            new RiskRuleExecutor(NullLogger<RiskRuleExecutor>.Instance),
            new RiskDecisionService(),
            _calculationService
        );

        // Act
        var result = await engine.EvaluateAsync(context);

        // Assert
        result.Should().NotBeNull();
        result.Decision.Should().Be(RiskDecisionStatus.NeedsReview);
        result.Reason.Should().Contain("Invalid Context");
    }

    [Fact]
    public async Task EvaluateAsync_ShouldExecuteAllRules_EvenIfOneThrowsException()
    {
        // Arrange
        var context = new TradeRiskContext
        {
            SignalId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 60000m,
            StopLoss = 59000m,
            TakeProfits = new[] { 61000m },
            AccountBalance = 10000m
        };

        var mockRule1 = new Mock<IRiskRule>();
        mockRule1.Setup(r => r.EvaluateAsync(context))
            .ThrowsAsync(new InvalidOperationException("Simulated rule explosion."));

        var mockRule2 = new Mock<IRiskRule>();
        mockRule2.Setup(r => r.EvaluateAsync(context))
            .ReturnsAsync(new RiskRuleResult
            {
                RuleName = "HealthyRule",
                Passed = true,
                Severity = RiskRuleSeverity.Info,
                Message = "Passed perfectly"
            });

        var options = Options.Create(new RiskManagementOptions { DefaultProfile = "Balanced" });
        var executor = new RiskRuleExecutor(NullLogger<RiskRuleExecutor>.Instance);
        var decisionService = new RiskDecisionService();

        var engine = new RiskRuleEngine(
            NullLogger<RiskRuleEngine>.Instance,
            options,
            new[] { mockRule1.Object, mockRule2.Object },
            executor,
            decisionService,
            _calculationService
        );

        // Act
        var result = await engine.EvaluateAsync(context);

        // Assert
        result.Should().NotBeNull();
        result.ExecutedRules.Should().HaveCount(2);
        result.PassedRules.Should().Contain("HealthyRule");
        result.FailedRules.Should().Contain(mockRule1.Object.GetType().Name);
        result.Errors.Should().ContainSingle(m => m.Contains("Simulated rule explosion."));
        result.Decision.Should().Be(RiskDecisionStatus.Rejected); // Critical exception translates to reject
    }
}
