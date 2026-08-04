using System;
using FluentAssertions;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;
using Xunit;

namespace TradingBot.UnitTests;

public class DomainTests
{
    [Fact]
    public void Signal_ShouldInitialize_WhenValidParametersProvided()
    {
        // Arrange & Act
        var signal = new Signal("BTCUSDT", SignalType.Buy, 45000.50m, 0.05m);

        // Assert
        signal.Should().NotBeNull();
        signal.Symbol.Should().Be("BTCUSDT");
        signal.Type.Should().Be(SignalType.Buy);
        signal.Price.Should().Be(45000.50m);
        signal.Quantity.Should().Be(0.05m);
        signal.Id.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("", 10, 1)]
    [InlineData("   ", 10, 1)]
    [InlineData("BTC", 0, 1)]
    [InlineData("BTC", -5, 1)]
    [InlineData("BTC", 10, 0)]
    [InlineData("BTC", 10, -0.1)]
    public void Signal_ShouldThrowDomainException_WhenParametersAreInvalid(string symbol, decimal price, decimal quantity)
    {
        // Arrange, Act & Assert
        Action act = () => new Signal(symbol, SignalType.Buy, price, quantity);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Order_ShouldInitialize_WhenValidParametersProvided()
    {
        // Arrange & Act
        var order = new Order("CLIENT-123", "ETHUSDT", OrderType.Limit, SignalType.Sell, 3200.00m, 1.5m);

        // Assert
        order.Should().NotBeNull();
        order.ClientOrderId.Should().Be("CLIENT-123");
        order.Symbol.Should().Be("ETHUSDT");
        order.Type.Should().Be(OrderType.Limit);
        order.Side.Should().Be(SignalType.Sell);
        order.Price.Should().Be(3200.00m);
        order.Quantity.Should().Be(1.5m);
        order.Status.Should().Be(OrderStatus.New);
    }

    [Fact]
    public void Order_ShouldUpdateStatusSuccessfully_WhenStatusIsActive()
    {
        // Arrange
        var order = new Order("CLIENT-123", "ETHUSDT", OrderType.Limit, SignalType.Sell, 3200.00m, 1.5m);

        // Act
        order.UpdateStatus(OrderStatus.PartiallyFilled);

        // Assert
        order.Status.Should().Be(OrderStatus.PartiallyFilled);
        order.UpdatedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(OrderStatus.Filled)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Rejected)]
    public void Order_ShouldThrowException_WhenTransitioningFromTerminalStatus(OrderStatus terminalStatus)
    {
        // Arrange
        var order = new Order("CLIENT-123", "ETHUSDT", OrderType.Limit, SignalType.Sell, 3200.00m, 1.5m);
        order.UpdateStatus(terminalStatus);

        // Act & Assert
        Action act = () => order.UpdateStatus(OrderStatus.New);
        act.Should().Throw<DomainException>().WithMessage($"*Cannot change state of order from {terminalStatus}*");
    }
}
