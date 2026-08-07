using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Application.Repositories;
using TradingBot.Application.RiskManagement.Calculators;
using TradingBot.Application.RiskManagement.Configuration;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Application.RiskManagement.Rules;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Domain.RiskManagement.ValueObjects;
using Xunit;

namespace TradingBot.UnitTests.RiskManagement;

public class RiskRulesTests
{
    private readonly RiskCalculationService _calculationService;
    private readonly IOptions<RiskCalculationOptions> _calcOptions;

    public RiskRulesTests()
    {
        _calcOptions = Options.Create(new RiskCalculationOptions
        {
            DefaultRiskPercent = 2.0m, // 2% default risk
            RoundingPrecision = 8
        });

        var riskAmountCalc = new RiskAmountCalculator();
        var stopLossDistanceCalc = new StopLossDistanceCalculator();
        var positionSizeCalc = new PositionSizeCalculator(riskAmountCalc, stopLossDistanceCalc, _calcOptions);
        var riskRewardCalc = new RiskRewardCalculator(_calcOptions);

        _calculationService = new RiskCalculationService(
            riskAmountCalc,
            stopLossDistanceCalc,
            positionSizeCalc,
            riskRewardCalc,
            _calcOptions
        );
    }

    private TradeRiskContext CreateDefaultContext()
    {
        return new TradeRiskContext
        {
            SignalId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 60000m,
            StopLoss = 59000m,
            TakeProfits = new List<decimal> { 62000m, 65000m },
            Leverage = 10,
            AccountBalance = 10000m,
            OpenPositions = 1,
            DailyPnL = 100m,
            CurrentExposure = 1000m
        };
    }

    #region Max Risk Rule Tests

    [Fact]
    public async Task MaxRiskPerTradeRule_ShouldPass_WhenRiskWithinLimit()
    {
        // Arrange
        var context = CreateDefaultContext(); // Risk = 200 USDT (2% of 10000)
        var options = Options.Create(new RiskManagementOptions
        {
            MaxRiskPerTrade = 2.5m // limit = 250 USDT
        });
        var rule = new MaxRiskPerTradeRule(options, _calculationService);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeTrue();
        result.Severity.Should().Be(RiskRuleSeverity.Error);
        result.Message.Should().Contain("within the allowed limit");
    }

    [Fact]
    public async Task MaxRiskPerTradeRule_ShouldFail_WhenRiskExceedsLimit()
    {
        // Arrange
        var context = CreateDefaultContext(); // Risk = 200 USDT
        var options = Options.Create(new RiskManagementOptions
        {
            MaxRiskPerTrade = 1.0m // limit = 100 USDT
        });
        var rule = new MaxRiskPerTradeRule(options, _calculationService);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeFalse();
        result.Severity.Should().Be(RiskRuleSeverity.Error);
        result.Message.Should().Contain("exceeds the maximum allowed limit");
    }

    [Fact]
    public async Task MaxRiskPerTradeRule_BoundaryValue_ShouldPass()
    {
        // Arrange
        var context = CreateDefaultContext(); // Risk = 200 USDT
        var options = Options.Create(new RiskManagementOptions
        {
            MaxRiskPerTrade = 2.0m // limit = 200 USDT exactly
        });
        var rule = new MaxRiskPerTradeRule(options, _calculationService);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeTrue();
    }

    #endregion

    #region Max Open Positions Rule Tests

    [Fact]
    public async Task MaxOpenPositionsRule_ShouldPass_WhenBelowLimit()
    {
        // Arrange
        var context = CreateDefaultContext(); // Current open = 1
        var options = Options.Create(new RiskManagementOptions
        {
            MaxOpenPositions = 3
        });
        var rule = new MaxOpenPositionsRule(options);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeTrue();
        result.Message.Should().Contain("below the limit");
    }

    [Fact]
    public async Task MaxOpenPositionsRule_ShouldFail_WhenAtOrAboveLimit()
    {
        // Arrange
        var context = CreateDefaultContext() with { OpenPositions = 3 };
        var options = Options.Create(new RiskManagementOptions
        {
            MaxOpenPositions = 3
        });
        var rule = new MaxOpenPositionsRule(options);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeFalse();
        result.Severity.Should().Be(RiskRuleSeverity.Error);
        result.Message.Should().Contain("at or above the maximum limit");
    }

    #endregion

    #region Maximum Leverage Rule Tests

    [Fact]
    public async Task MaximumLeverageRule_ShouldPass_WhenBelowLimit()
    {
        // Arrange
        var context = CreateDefaultContext() with { Leverage = 10 };
        var options = Options.Create(new RiskManagementOptions
        {
            MaximumLeverage = 20
        });
        var rule = new MaximumLeverageRule(options);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeTrue();
        result.Severity.Should().Be(RiskRuleSeverity.Info);
    }

    [Fact]
    public async Task MaximumLeverageRule_ShouldFail_WhenExceedsLimitAndAutoReduceDisabled()
    {
        // Arrange
        var context = CreateDefaultContext() with { Leverage = 25 };
        var options = Options.Create(new RiskManagementOptions
        {
            MaximumLeverage = 20,
            AutoReduceLeverage = false
        });
        var rule = new MaximumLeverageRule(options);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeFalse();
        result.Severity.Should().Be(RiskRuleSeverity.Error);
    }

    [Fact]
    public async Task MaximumLeverageRule_ShouldPassWithWarning_WhenExceedsLimitAndAutoReduceEnabled()
    {
        // Arrange
        var context = CreateDefaultContext() with { Leverage = 25 };
        var options = Options.Create(new RiskManagementOptions
        {
            MaximumLeverage = 20,
            AutoReduceLeverage = true
        });
        var rule = new MaximumLeverageRule(options);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeTrue();
        result.Severity.Should().Be(RiskRuleSeverity.Warning);
        result.Message.Should().Contain("Automatically reduced leverage");
    }

    #endregion

    #region Maximum Exposure Rule Tests

    [Fact]
    public async Task MaximumExposureRule_ShouldPass_WhenWithinLimit()
    {
        // Arrange
        var context = CreateDefaultContext(); // New Pos size = 0.2 BTC. Entry = 60,000. New exposure = 12,000. Current = 1,000. Total = 13,000.
        var options = Options.Create(new RiskManagementOptions
        {
            MaximumExposure = 150.0m // Limit = 15,000 USDT (150% of 10,000)
        });
        var rule = new MaximumExposureRule(options, _calculationService);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeTrue();
        result.Message.Should().Contain("within the maximum limit");
    }

    [Fact]
    public async Task MaximumExposureRule_ShouldFail_WhenExceedsLimit()
    {
        // Arrange
        var context = CreateDefaultContext(); // Total = 13,000.
        var options = Options.Create(new RiskManagementOptions
        {
            MaximumExposure = 10.0m // Limit = 1,000 USDT (10% of 10,000)
        });
        var rule = new MaximumExposureRule(options, _calculationService);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeFalse();
        result.Severity.Should().Be(RiskRuleSeverity.Error);
        result.Message.Should().Contain("exceeds the limit");
    }

    #endregion

    #region Daily Loss Rule Tests

    [Fact]
    public async Task DailyLossRule_ShouldPass_WhenLossWithinLimit()
    {
        // Arrange
        var context = CreateDefaultContext() with { DailyPnL = -300m }; // Daily Loss = 300 USDT
        var options = Options.Create(new RiskManagementOptions
        {
            MaximumDailyLoss = 5.0m // limit = 500 USDT (5% of 10,000)
        });
        var rule = new DailyLossRule(options);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeTrue();
        result.Message.Should().Contain("above the maximum allowed daily loss limit");
    }

    [Fact]
    public async Task DailyLossRule_ShouldFail_WhenLossExceedsLimit()
    {
        // Arrange
        var context = CreateDefaultContext() with { DailyPnL = -600m }; // Daily Loss = 600 USDT
        var options = Options.Create(new RiskManagementOptions
        {
            MaximumDailyLoss = 5.0m // limit = 500 USDT
        });
        var rule = new DailyLossRule(options);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeFalse();
        result.Severity.Should().Be(RiskRuleSeverity.Critical);
        result.Message.Should().Contain("Trading Disabled");
    }

    #endregion

    #region Drawdown Rule Tests

    [Fact]
    public async Task DrawdownRule_ShouldPass_WhenDrawdownWithinLimit()
    {
        // Arrange
        var context = CreateDefaultContext() with { DailyPnL = -1000m }; // 10% drawdown
        var options = Options.Create(new RiskManagementOptions
        {
            MaximumDrawdown = 15.0m // 15% limit
        });
        var rule = new DrawdownRule(options);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeTrue();
        result.Message.Should().Contain("within the limit");
    }

    [Fact]
    public async Task DrawdownRule_ShouldFail_WhenDrawdownExceedsLimit()
    {
        // Arrange
        var context = CreateDefaultContext() with { DailyPnL = -2000m }; // 20% drawdown
        var options = Options.Create(new RiskManagementOptions
        {
            MaximumDrawdown = 15.0m // 15% limit
        });
        var rule = new DrawdownRule(options);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeFalse();
        result.Severity.Should().Be(RiskRuleSeverity.Critical);
    }

    #endregion

    #region Duplicate Position Rule Tests

    [Fact]
    public async Task DuplicatePositionRule_ShouldPass_WhenOnePositionPerSymbolDisabled()
    {
        // Arrange
        var context = CreateDefaultContext();
        var options = Options.Create(new RiskManagementOptions
        {
            OnePositionPerSymbol = false
        });
        var repoMock = new Mock<IPositionRepository>();
        var rule = new DuplicatePositionRule(options, repoMock.Object);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeTrue();
        result.Message.Should().Contain("disabled");
        repoMock.Verify(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DuplicatePositionRule_ShouldPass_WhenNoExistingOpenPosition()
    {
        // Arrange
        var context = CreateDefaultContext() with { Symbol = "ETHUSDT" };
        var options = Options.Create(new RiskManagementOptions
        {
            OnePositionPerSymbol = true
        });

        var existingPos = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 60000m, 1m);

        var repoMock = new Mock<IPositionRepository>();
        repoMock.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position> { existingPos });

        var rule = new DuplicatePositionRule(options, repoMock.Object);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeTrue();
        result.Message.Should().Contain("No existing open position found");
    }

    [Fact]
    public async Task DuplicatePositionRule_ShouldFail_WhenDuplicateOpenPositionExists()
    {
        // Arrange
        var context = CreateDefaultContext() with { Symbol = "BTCUSDT" };
        var options = Options.Create(new RiskManagementOptions
        {
            OnePositionPerSymbol = true
        });

        var existingPos = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 60000m, 1m);

        var repoMock = new Mock<IPositionRepository>();
        repoMock.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position> { existingPos });

        var rule = new DuplicatePositionRule(options, repoMock.Object);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeFalse();
        result.Severity.Should().Be(RiskRuleSeverity.Error);
        result.Message.Should().Contain("An open position already exists");
    }

    #endregion

    #region Risk Reward Rule Tests

    [Fact]
    public async Task RiskRewardRule_ShouldPass_WhenRRWithinLimit()
    {
        // Arrange
        var context = CreateDefaultContext(); // RR = 3.5
        var options = Options.Create(new RiskManagementOptions
        {
            MinimumRiskReward = 3.0m
        });
        var rule = new RiskRewardRule(options, _calculationService);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task RiskRewardRule_ShouldFail_WhenRRBelowLimit()
    {
        // Arrange
        var context = CreateDefaultContext(); // RR = 3.5
        var options = Options.Create(new RiskManagementOptions
        {
            MinimumRiskReward = 4.0m
        });
        var rule = new RiskRewardRule(options, _calculationService);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeFalse();
        result.Severity.Should().Be(RiskRuleSeverity.Error);
    }

    #endregion

    #region Margin Availability Rule Tests

    [Fact]
    public async Task MarginAvailabilityRule_ShouldPass_WhenMarginSufficient()
    {
        // Arrange
        var context = CreateDefaultContext(); // Balance = 10k, Exposure = 1k. Free = 9k. Required margin = 1200 USDT.
        var options = Options.Create(new RiskManagementOptions());
        var rule = new MarginAvailabilityRule(options, _calculationService);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeTrue();
        result.Message.Should().Contain("Required margin of");
    }

    [Fact]
    public async Task MarginAvailabilityRule_ShouldFail_WhenMarginInsufficient()
    {
        // Arrange
        var context = CreateDefaultContext() with { CurrentExposure = 9500m }; // Free = 500. Required margin = 1200.
        var options = Options.Create(new RiskManagementOptions());
        var rule = new MarginAvailabilityRule(options, _calculationService);

        // Act
        var result = await rule.EvaluateAsync(context);

        // Assert
        result.Passed.Should().BeFalse();
        result.Severity.Should().Be(RiskRuleSeverity.Critical);
        result.Message.Should().Contain("Insufficient margin");
    }

    #endregion
}
