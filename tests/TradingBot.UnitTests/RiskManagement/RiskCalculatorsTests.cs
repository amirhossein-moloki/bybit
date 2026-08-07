using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Options;
using TradingBot.Application.RiskManagement.Calculators;
using TradingBot.Application.RiskManagement.Configuration;
using TradingBot.Application.RiskManagement.Exceptions;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.ValueObjects;
using Xunit;

namespace TradingBot.UnitTests.RiskManagement;

public class RiskCalculatorsTests
{
    private readonly IOptions<RiskCalculationOptions> _options;

    public RiskCalculatorsTests()
    {
        _options = Options.Create(new RiskCalculationOptions
        {
            DefaultRiskPercent = 1.0m,
            RoundingPrecision = 8
        });
    }

    [Fact]
    public void RiskAmountCalculator_ShouldCalculateCorrectly()
    {
        // Arrange
        var calculator = new RiskAmountCalculator();

        // Act
        decimal result = calculator.Calculate(1000m, 1.0m);

        // Assert
        result.Should().Be(10m);
    }

    [Fact]
    public void StopLossDistanceCalculator_Long_ShouldCalculateCorrectly()
    {
        // Arrange
        var calculator = new StopLossDistanceCalculator();

        // Act
        decimal result = calculator.Calculate(OrderSide.Buy, 60000m, 59000m);

        // Assert
        result.Should().Be(1000m);
    }

    [Fact]
    public void StopLossDistanceCalculator_Short_ShouldCalculateCorrectly()
    {
        // Arrange
        var calculator = new StopLossDistanceCalculator();

        // Act
        decimal result = calculator.Calculate(OrderSide.Sell, 60000m, 61000m);

        // Assert
        result.Should().Be(1000m);
    }

    [Fact]
    public void PositionSizeCalculator_ShouldCalculateCorrectly()
    {
        // Arrange
        var riskCalc = new RiskAmountCalculator();
        var slCalc = new StopLossDistanceCalculator();
        var calculator = new PositionSizeCalculator(riskCalc, slCalc, _options);

        // Act
        decimal result = calculator.Calculate(10m, 1000m);

        // Assert
        result.Should().Be(0.01m);
    }

    [Fact]
    public void RiskRewardCalculator_ShouldCalculateCorrectly()
    {
        // Arrange
        var calculator = new RiskRewardCalculator(_options);

        // Act
        decimal result = calculator.Calculate(1000m, 2000m);

        // Assert
        result.Should().Be(2m);
    }

    [Fact]
    public void RiskRewardCalculator_FirstTp_ShouldCalculateCorrectly()
    {
        // Arrange
        var calculator = new RiskRewardCalculator(_options);
        var takeProfits = new List<decimal> { 62000m, 65000m };

        // Act
        decimal result = calculator.CalculateFirstTp(OrderSide.Buy, 60000m, 59000m, takeProfits);

        // Assert
        result.Should().Be(2m); // (62000 - 60000) / (60000 - 59000) = 2
    }

    [Fact]
    public void RiskRewardCalculator_AverageTp_ShouldCalculateCorrectly()
    {
        // Arrange
        var calculator = new RiskRewardCalculator(_options);
        var takeProfits = new List<decimal> { 62000m, 64000m };

        // Act
        decimal result = calculator.CalculateAverageTp(OrderSide.Buy, 60000m, 59000m, takeProfits);

        // Assert
        result.Should().Be(3m); // Average TP is 63000. (63000 - 60000) / (60000 - 59000) = 3
    }

    [Fact]
    public void RiskAmountCalculator_ShouldThrow_WhenBalanceIsZeroOrNegative()
    {
        // Arrange
        var calculator = new RiskAmountCalculator();

        // Act & Assert
        Action actZero = () => calculator.Calculate(0m, 1m);
        actZero.Should().Throw<RiskManagementException>().WithMessage("*account balance*");

        Action actNegative = () => calculator.Calculate(-100m, 1m);
        actNegative.Should().Throw<RiskManagementException>().WithMessage("*account balance*");
    }

    [Fact]
    public void RiskAmountCalculator_ShouldThrow_WhenRiskPercentIsNegative()
    {
        // Arrange
        var calculator = new RiskAmountCalculator();

        // Act & Assert
        Action act = () => calculator.Calculate(1000m, -1m);
        act.Should().Throw<RiskManagementException>().WithMessage("*risk percentage*");
    }

    [Fact]
    public void StopLossDistanceCalculator_ShouldThrow_WhenStopLossIsMissing()
    {
        // Arrange
        var calculator = new StopLossDistanceCalculator();

        // Act & Assert
        Action act = () => calculator.Calculate(OrderSide.Buy, 60000m, null);
        act.Should().Throw<RiskManagementException>().WithMessage("*Missing stop loss*");
    }

    [Fact]
    public void StopLossDistanceCalculator_ShouldThrow_WhenDistanceIsZero()
    {
        // Arrange
        var calculator = new StopLossDistanceCalculator();

        // Act & Assert
        Action act = () => calculator.Calculate(OrderSide.Buy, 60000m, 60000m);
        act.Should().Throw<RiskManagementException>().WithMessage("*distance is zero*");
    }

    [Fact]
    public void StopLossDistanceCalculator_ShouldThrow_WhenDistanceIsNegative()
    {
        // Arrange
        var calculator = new StopLossDistanceCalculator();

        // Act & Assert
        Action act = () => calculator.Calculate(OrderSide.Buy, 60000m, 61000m);
        act.Should().Throw<RiskManagementException>().WithMessage("*distance is negative*");
    }

    [Fact]
    public void PositionSizeCalculator_ShouldThrow_WhenStopLossDistanceIsZeroOrNegative()
    {
        // Arrange
        var riskCalc = new RiskAmountCalculator();
        var slCalc = new StopLossDistanceCalculator();
        var calculator = new PositionSizeCalculator(riskCalc, slCalc, _options);

        // Act & Assert
        Action actZero = () => calculator.Calculate(10m, 0m);
        actZero.Should().Throw<RiskManagementException>().WithMessage("*distance is zero*");

        Action actNeg = () => calculator.Calculate(10m, -5m);
        actNeg.Should().Throw<RiskManagementException>().WithMessage("*distance is negative*");
    }
}
