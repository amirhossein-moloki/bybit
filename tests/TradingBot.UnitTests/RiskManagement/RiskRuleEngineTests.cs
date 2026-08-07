using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Application.Repositories;
using TradingBot.Application.RiskManagement.Engine;
using TradingBot.Application.RiskManagement.Exceptions;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Application.RiskManagement.Configuration;
using TradingBot.Application.RiskManagement.Rules;
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

    [Fact]
    public async Task EvaluateAsync_ShouldSupportHighLoad10000Signals_Concurrently()
    {
        // Arrange
        var calcOptions = Options.Create(new RiskCalculationOptions
        {
            DefaultRiskPercent = 0.1m,
            RoundingPrecision = 8
        });

        var riskAmountCalc = new RiskAmountCalculator();
        var stopLossDistanceCalc = new StopLossDistanceCalculator();
        var positionSizeCalc = new PositionSizeCalculator(riskAmountCalc, stopLossDistanceCalc, calcOptions);
        var riskRewardCalc = new RiskRewardCalculator(calcOptions);

        var calcService = new RiskCalculationService(
            riskAmountCalc,
            stopLossDistanceCalc,
            positionSizeCalc,
            riskRewardCalc,
            calcOptions
        );

        var engineOptions = Options.Create(new RiskManagementOptions
        {
            Enabled = true,
            DefaultProfile = "Balanced",
            MaxRiskPerTrade = 2.0m,
            MaxOpenPositions = 5,
            MaximumLeverage = 10,
            MaximumExposure = 50.0m,
            MaximumDailyLoss = 5.0m,
            MaximumDrawdown = 10.0m,
            OnePositionPerSymbol = true,
            MinimumRiskReward = 1.5m,
            RejectOnCritical = true
        });

        var mockPositionRepo = new Mock<IPositionRepository>();
        mockPositionRepo.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TradingBot.Domain.Entities.Position>());

        var rules = new List<IRiskRule>
        {
            new MaxRiskPerTradeRule(engineOptions, calcService),
            new MaxOpenPositionsRule(engineOptions),
            new MaximumLeverageRule(engineOptions),
            new MaximumExposureRule(engineOptions, calcService),
            new DailyLossRule(engineOptions),
            new DrawdownRule(engineOptions),
            new DuplicatePositionRule(engineOptions, mockPositionRepo.Object),
            new RiskRewardRule(engineOptions, calcService),
            new MarginAvailabilityRule(engineOptions, calcService)
        };

        var executor = new RiskRuleExecutor(NullLogger<RiskRuleExecutor>.Instance);
        var decisionService = new RiskDecisionService();

        var engine = new RiskRuleEngine(
            NullLogger<RiskRuleEngine>.Instance,
            engineOptions,
            rules,
            executor,
            decisionService,
            calcService
        );

        int iterations = 10000;
        var contexts = Enumerable.Range(0, iterations).Select(i => new TradeRiskContext
        {
            SignalId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 60000m,
            StopLoss = 59000m,
            TakeProfits = new[] { 62000m },
            Leverage = 10,
            AccountBalance = 100000m,
            OpenPositions = 2,
            DailyPnL = 100m,
            CurrentExposure = 2000m
        }).ToList();

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var tasks = contexts.Select(ctx => engine.EvaluateAsync(ctx));
        var results = await Task.WhenAll(tasks);

        stopwatch.Stop();

        // Assert
        results.Length.Should().Be(iterations);
        results.All(r => r.Decision == RiskDecisionStatus.Approved).Should().BeTrue();

        double averageTimeMs = stopwatch.Elapsed.TotalMilliseconds / iterations;
        averageTimeMs.Should().BeLessThan(100.0, "Average evaluation should be less than 100ms excluding database latency");

        // Output performance metrics
        System.IO.File.WriteAllText("run_output.txt", $"[Performance Stress Test] Processed {iterations} evaluations in {stopwatch.ElapsedMilliseconds} ms. Avg: {averageTimeMs:F4} ms.\n");
    }
}
