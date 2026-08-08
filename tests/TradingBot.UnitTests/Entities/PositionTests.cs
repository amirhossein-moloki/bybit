using System;
using FluentAssertions;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;
using Xunit;

namespace TradingBot.UnitTests.Entities;

public class PositionTests
{
    [Fact]
    public void Position_ShouldInitialize_WhenValidParametersProvided()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var symbol = "BTCUSDT";
        var side = OrderSide.Buy;
        var entryPrice = 50000m;
        var quantity = 0.5m;
        var stopLoss = 48000m;
        var takeProfit = 55000m;
        var exchangePositionId = "ex-pos-123";
        var leverage = 10m;
        var margin = 2500m;

        // Act
        var position = new Position(
            orderId,
            symbol,
            side,
            entryPrice,
            quantity,
            stopLoss,
            takeProfit,
            exchangePositionId,
            leverage,
            margin
        );

        // Assert
        position.Should().NotBeNull();
        position.OrderId.Should().Be(orderId);
        position.Symbol.Should().Be("BTCUSDT");
        position.Side.Should().Be(OrderSide.Buy);
        position.EntryPrice.Should().Be(entryPrice);
        position.Quantity.Should().Be(quantity);
        position.RemainingQuantity.Should().Be(quantity);
        position.StopLoss.Should().Be(stopLoss);
        position.TakeProfit.Should().Be(takeProfit);
        position.ExchangePositionId.Should().Be(exchangePositionId);
        position.Leverage.Should().Be(leverage);
        position.Margin.Should().Be(margin);
        position.Status.Should().Be(PositionStatus.Open);
        position.UnrealizedPnL.Should().Be(0m);
        position.RealizedPnL.Should().Be(0m);
    }

    [Theory]
    [InlineData("BTCUSDT", 50000, 0, "Quantity must be greater than zero.")]
    [InlineData("BTCUSDT", 0, 0.5, "EntryPrice must be greater than zero.")]
    [InlineData("", 50000, 0.5, "Symbol cannot be empty.")]
    [InlineData("   ", 50000, 0.5, "Symbol cannot be empty.")]
    public void Position_ShouldThrowDomainException_WhenParametersAreInvalid(string symbol, decimal entryPrice, decimal quantity, string expectedMessage)
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Act & Assert
        Action act = () => new Position(orderId, symbol, OrderSide.Buy, entryPrice, quantity);
        act.Should().Throw<DomainException>().WithMessage($"*{expectedMessage}*");
    }

    [Fact]
    public void Position_ShouldThrowDomainException_WhenOrderIdIsEmpty()
    {
        // Act & Assert
        Action act = () => new Position(Guid.Empty, "BTCUSDT", OrderSide.Buy, 50000m, 0.5m);
        act.Should().Throw<DomainException>().WithMessage("*OrderId cannot be empty.*");
    }

    [Fact]
    public void Position_ShouldCalculateUnrealizedPnLCorrectly_ForBuySide()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 50000m, 0.5m);

        // Act (Price rises to 52000)
        position.UpdatePrice(52000m);

        // Assert
        // PnL = (52000 - 50000) * 0.5 = 1000
        position.UnrealizedPnL.Should().Be(1000m);

        // Act (Price drops to 49000)
        position.UpdatePrice(49000m);

        // Assert
        // PnL = (49000 - 50000) * 0.5 = -500
        position.UnrealizedPnL.Should().Be(-500m);
    }

    [Fact]
    public void Position_ShouldCalculateUnrealizedPnLCorrectly_ForSellSide()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Sell, 50000m, 0.5m);

        // Act (Price drops to 48000)
        position.UpdatePrice(48000m);

        // Assert
        // PnL = (50000 - 48000) * 0.5 = 1000
        position.UnrealizedPnL.Should().Be(1000m);

        // Act (Price rises to 51000)
        position.UpdatePrice(51000m);

        // Assert
        // PnL = (50000 - 51000) * 0.5 = -500
        position.UnrealizedPnL.Should().Be(-500m);
    }

    [Fact]
    public void Position_ShouldTransitionStatusCorrectly_WhenValid()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 50000m, 0.5m, initialStatus: PositionStatus.Pending);

        // Pending -> Open
        position.TransitionTo(PositionStatus.Open);
        position.Status.Should().Be(PositionStatus.Open);

        // Open -> PartiallyClosed
        position.TransitionTo(PositionStatus.PartiallyClosed);
        position.Status.Should().Be(PositionStatus.PartiallyClosed);

        // PartiallyClosed -> Closed
        position.TransitionTo(PositionStatus.Closed);
        position.Status.Should().Be(PositionStatus.Closed);
        position.ClosedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(PositionStatus.Closed, PositionStatus.Open)]
    [InlineData(PositionStatus.Closed, PositionStatus.PartiallyClosed)]
    [InlineData(PositionStatus.Liquidated, PositionStatus.Open)]
    public void Position_ShouldThrowDomainException_OnInvalidStatusTransitions(PositionStatus fromStatus, PositionStatus toStatus)
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 50000m, 0.5m, initialStatus: fromStatus);

        // Act & Assert
        Action act = () => position.TransitionTo(toStatus);
        act.Should().Throw<DomainException>().WithMessage("*Invalid transition: Cannot change position status*");
    }

    [Fact]
    public void Position_PartialClose_ShouldCalculatePnLAndRemainingCorrectly()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 50000m, 1.0m);

        // Act (Partially close 0.4 BTC at 55000, fee 10)
        position.PartialClose(0.4m, 55000m, 10m);

        // Assert
        // Realized PnL = (55000 - 50000) * 0.4 = 2000
        position.RealizedPnL.Should().Be(2000m);
        position.RemainingQuantity.Should().Be(0.6m);
        position.Fee.Should().Be(10m);
        position.Status.Should().Be(PositionStatus.PartiallyClosed);

        // Remaining unrealized PnL = (55000 - 50000) * 0.6 = 3000
        position.UnrealizedPnL.Should().Be(3000m);

        // Act (Close the remaining 0.6 BTC at 60000, fee 15)
        position.PartialClose(0.6m, 60000m, 15m);

        // Assert
        // Realized PnL added = (60000 - 50000) * 0.6 = 6000. Total Realized PnL = 2000 + 6000 = 8000
        position.RealizedPnL.Should().Be(8000m);
        position.RemainingQuantity.Should().Be(0m);
        position.Fee.Should().Be(25m);
        position.Status.Should().Be(PositionStatus.Closed);
        position.UnrealizedPnL.Should().Be(0m);
        position.ClosedAt.Should().NotBeNull();
    }

    [Fact]
    public void Position_Liquidate_ShouldResultInTotalLoss()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 50000m, 0.5m);

        // Act
        position.Liquidate();

        // Assert
        position.Status.Should().Be(PositionStatus.Liquidated);
        position.RealizedPnL.Should().Be(-25000m); // -50000 * 0.5 = -25000
        position.RemainingQuantity.Should().Be(0m);
        position.UnrealizedPnL.Should().Be(0m);
        position.CurrentPrice.Should().Be(0m);
        position.ClosedAt.Should().NotBeNull();
    }
}
