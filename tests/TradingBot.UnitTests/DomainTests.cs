using System;
using FluentAssertions;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;
using TradingBot.Domain.ValueObjects;
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
        var order = new Order("CLIENT-123", new Symbol("ETHUSDT"), OrderSide.Sell, OrderType.Limit, new Quantity(1.5m), new Money(3200.00m));

        // Assert
        order.Should().NotBeNull();
        order.ClientOrderId.Should().Be("CLIENT-123");
        order.Symbol.Value.Should().Be("ETHUSDT");
        order.Type.Should().Be(OrderType.Limit);
        order.Side.Should().Be(OrderSide.Sell);
        order.Price.Amount.Should().Be(3200.00m);
        order.Quantity.Value.Should().Be(1.5m);
        order.Status.Should().Be(OrderStatus.Created);
    }

    [Fact]
    public void Order_ShouldUpdateStatusSuccessfully_WhenStatusIsActive()
    {
        // Arrange
        var order = new Order("CLIENT-123", new Symbol("ETHUSDT"), OrderSide.Sell, OrderType.Limit, new Quantity(1.5m), new Money(3200.00m));

        // Act
        order.Submit();
        order.Accept("EXCHANGE-123");
        order.MarkPartiallyFilled();

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
        var order = new Order("CLIENT-123", new Symbol("ETHUSDT"), OrderSide.Sell, OrderType.Limit, new Quantity(1.5m), new Money(3200.00m));

        // Move to terminal status
        order.Submit();
        if (terminalStatus == OrderStatus.Rejected)
        {
            order.Reject("Rejected in test.");
        }
        else
        {
            order.Accept("EXCHANGE-ID");
            if (terminalStatus == OrderStatus.Cancelled)
            {
                order.Cancel();
            }
            else if (terminalStatus == OrderStatus.Filled)
            {
                order.MarkFilled();
            }
        }

        // Act & Assert
        Action act = () => order.Submit();
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void OrderStateMachine_ShouldFollowSuccessFlow()
    {
        // Arrange
        var order = new Order("CLIENT-123", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.1m), new Money(50000m));

        // Act & Assert
        order.Status.Should().Be(OrderStatus.Created);

        order.Submit();
        order.Status.Should().Be(OrderStatus.Submitted);

        order.Accept("EXCHANGE-999");
        order.Status.Should().Be(OrderStatus.Accepted);
        order.ExchangeOrderId.Should().Be("EXCHANGE-999");

        order.MarkFilled();
        order.Status.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public void OrderStateMachine_ShouldFollowRejectFlow()
    {
        // Arrange
        var order = new Order("CLIENT-123", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.1m), new Money(50000m));

        // Act
        order.Submit();
        order.Reject("Out of funds");

        // Assert
        order.Status.Should().Be(OrderStatus.Rejected);
    }

    [Fact]
    public void OrderStateMachine_ShouldFollowCancelFlow()
    {
        // Arrange
        var order = new Order("CLIENT-123", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.1m), new Money(50000m));

        // Act
        order.Submit();
        order.Accept("EX-111");
        order.Cancel();

        // Assert
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void MoneyValueObject_ShouldValidateAmountAndCurrency()
    {
        // Valid
        var money = new Money(100.50m, "USDT");
        money.Amount.Should().Be(100.50m);
        money.Currency.Should().Be("USDT");

        // Invalid amount
        Action act1 = () => _ = new Money(-5m);
        act1.Should().Throw<DomainException>();

        // Invalid currency
        Action act2 = () => _ = new Money(100m, "   ");
        act2.Should().Throw<DomainException>();
    }

    [Fact]
    public void SymbolValueObject_ShouldNormalizeAndValidate()
    {
        // Valid
        var symbol = new Symbol("ethusdt");
        symbol.Value.Should().Be("ETHUSDT");

        // Invalid empty
        Action act1 = () => _ = new Symbol("");
        act1.Should().Throw<DomainException>();

        // Invalid short
        Action act2 = () => _ = new Symbol("BT");
        act2.Should().Throw<DomainException>();
    }

    [Fact]
    public void QuantityValueObject_ShouldValidate()
    {
        // Valid
        var qty = new Quantity(0.001m, "BTC");
        qty.Value.Should().Be(0.001m);
        qty.Unit.Should().Be("BTC");

        // Invalid zero or negative
        Action act1 = () => _ = new Quantity(0m);
        act1.Should().Throw<DomainException>();

        Action act2 = () => _ = new Quantity(-0.1m);
        act2.Should().Throw<DomainException>();
    }
}
