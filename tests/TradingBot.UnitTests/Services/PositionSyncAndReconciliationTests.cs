using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Mappers;
using TradingBot.Application.Models;
using TradingBot.Application.Repositories;
using TradingBot.Application.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.UnitTests.Services;

public class PositionSyncAndReconciliationTests
{
    [Fact]
    public void ExchangePositionMapper_ShouldMapDtoToDomainCorrectly()
    {
        // Arrange
        var dto = new ExchangePositionDto(
            ExchangePositionId: "BTCUSDT_Long",
            Symbol: "BTCUSDT",
            Side: PositionSide.Long,
            Quantity: 1.5m,
            EntryPrice: 50000m,
            MarkPrice: 51000m,
            Leverage: 10m,
            Margin: 7500m,
            UnrealizedPnL: 1500m,
            LiquidationPrice: 45000m,
            StopLoss: 48000m,
            TakeProfit: 55000m,
            UpdatedAt: DateTime.UtcNow
        );

        var orderId = Guid.NewGuid();

        // Act
        var position = ExchangePositionMapper.ToDomain(dto, orderId);

        // Assert
        position.Should().NotBeNull();
        position.OrderId.Should().Be(orderId);
        position.Symbol.Should().Be("BTCUSDT");
        position.Side.Should().Be(OrderSide.Buy);
        position.Quantity.Should().Be(1.5m);
        position.EntryPrice.Should().Be(50000m);
        position.CurrentPrice.Should().Be(51000m);
        position.Leverage.Should().Be(10m);
        position.Margin.Should().Be(7500m);
        position.StopLoss.Should().Be(48000m);
        position.TakeProfit.Should().Be(55000m);
        position.Status.Should().Be(PositionStatus.Open);
        position.ExchangePositionId.Should().Be("BTCUSDT_Long");
    }

    [Fact]
    public async Task PositionSynchronizationService_ShouldUpdateDbPositionToMatchExchange()
    {
        // Arrange
        var mockRepo = new Mock<IPositionRepository>();
        var mockGateway = new Mock<IPositionGateway>();
        var mockUow = new Mock<IUnitOfWork>();

        var dbPosition = new Position(
            orderId: Guid.NewGuid(),
            symbol: "BTCUSDT",
            side: OrderSide.Buy,
            entryPrice: 49000m,
            quantity: 1.0m,
            stopLoss: 47000m,
            takeProfit: 55000m,
            exchangePositionId: "BTCUSDT_Long",
            leverage: 10m,
            margin: 4900m
        );

        mockRepo.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position> { dbPosition });

        var exPosition = new ExchangePositionDto(
            ExchangePositionId: "BTCUSDT_Long",
            Symbol: "BTCUSDT",
            Side: PositionSide.Long,
            Quantity: 1.0m,
            EntryPrice: 49000m,
            MarkPrice: 50500m, // Mark price has moved
            Leverage: 10m,
            Margin: 4900m,
            UnrealizedPnL: 1500m, // unrealized PnL updated
            LiquidationPrice: 44000m,
            StopLoss: 47000m,
            TakeProfit: 55000m,
            UpdatedAt: DateTime.UtcNow
        );

        mockGateway.Setup(g => g.GetOpenPositionsAsync())
            .ReturnsAsync(new List<ExchangePositionDto> { exPosition });

        var service = new PositionSynchronizationService(
            mockRepo.Object,
            mockGateway.Object,
            mockUow.Object,
            NullLogger<PositionSynchronizationService>.Instance
        );

        // Act
        await service.SynchronizeAsync(CancellationToken.None);

        // Assert
        dbPosition.CurrentPrice.Should().Be(50500m);
        dbPosition.UnrealizedPnL.Should().Be(1500m);
        dbPosition.IsDesynchronized.Should().BeFalse();
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PositionSynchronizationService_ShouldCloseDbPosition_WhenMissingOnExchange()
    {
        // Arrange
        var mockRepo = new Mock<IPositionRepository>();
        var mockGateway = new Mock<IPositionGateway>();
        var mockUow = new Mock<IUnitOfWork>();

        var dbPosition = new Position(
            orderId: Guid.NewGuid(),
            symbol: "BTCUSDT",
            side: OrderSide.Buy,
            entryPrice: 49000m,
            quantity: 1.0m,
            exchangePositionId: "BTCUSDT_Long"
        );

        mockRepo.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position> { dbPosition });

        mockGateway.Setup(g => g.GetOpenPositionsAsync())
            .ReturnsAsync(new List<ExchangePositionDto>()); // Empty exchange positions

        var service = new PositionSynchronizationService(
            mockRepo.Object,
            mockGateway.Object,
            mockUow.Object,
            NullLogger<PositionSynchronizationService>.Instance
        );

        // Act
        await service.SynchronizeAsync(CancellationToken.None);

        // Assert
        dbPosition.Status.Should().Be(PositionStatus.Closed);
        dbPosition.RemainingQuantity.Should().Be(0);
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PositionReconciliationService_ShouldRepairMismatchUsingExchangeAsSourceOfTruth()
    {
        // Arrange
        var mockRepo = new Mock<IPositionRepository>();
        var mockOrderRepo = new Mock<IOrderRepository>();
        var mockGateway = new Mock<IPositionGateway>();
        var mockUow = new Mock<IUnitOfWork>();

        var dbPosition = new Position(
            orderId: Guid.NewGuid(),
            symbol: "BTCUSDT",
            side: OrderSide.Buy,
            entryPrice: 49000m,
            quantity: 1.0m,
            exchangePositionId: "BTCUSDT_Long"
        );

        mockRepo.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position> { dbPosition });

        // Exchange size has been modified to 1.5m (Mismatch!)
        var exPosition = new ExchangePositionDto(
            ExchangePositionId: "BTCUSDT_Long",
            Symbol: "BTCUSDT",
            Side: PositionSide.Long,
            Quantity: 1.5m,
            EntryPrice: 49500m,
            MarkPrice: 50000m,
            Leverage: 10m,
            Margin: 4950m,
            UnrealizedPnL: 750m,
            LiquidationPrice: 44500m,
            StopLoss: null,
            TakeProfit: null,
            UpdatedAt: DateTime.UtcNow
        );

        mockGateway.Setup(g => g.GetOpenPositionsAsync())
            .ReturnsAsync(new List<ExchangePositionDto> { exPosition });

        var service = new PositionReconciliationService(
            mockRepo.Object,
            mockOrderRepo.Object,
            mockGateway.Object,
            mockUow.Object,
            NullLogger<PositionReconciliationService>.Instance
        );

        // Act
        await service.ReconcileAsync(CancellationToken.None);

        // Assert
        dbPosition.Quantity.Should().Be(1.5m);
        dbPosition.EntryPrice.Should().Be(49500m);
        dbPosition.IsDesynchronized.Should().BeFalse(); // Cleared after sync
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PositionReconciliationService_ShouldCreateRecoveryRecord_WhenExchangeHasPositionMissingInDb()
    {
        // Arrange
        var mockRepo = new Mock<IPositionRepository>();
        var mockOrderRepo = new Mock<IOrderRepository>();
        var mockGateway = new Mock<IPositionGateway>();
        var mockUow = new Mock<IUnitOfWork>();

        mockRepo.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>()); // No open positions in DB

        var exPosition = new ExchangePositionDto(
            ExchangePositionId: "BTCUSDT_Long",
            Symbol: "BTCUSDT",
            Side: PositionSide.Long,
            Quantity: 1.5m,
            EntryPrice: 49500m,
            MarkPrice: 50000m,
            Leverage: 10m,
            Margin: 4950m,
            UnrealizedPnL: 750m,
            LiquidationPrice: 44500m,
            StopLoss: null,
            TakeProfit: null,
            UpdatedAt: DateTime.UtcNow
        );

        mockGateway.Setup(g => g.GetOpenPositionsAsync())
            .ReturnsAsync(new List<ExchangePositionDto> { exPosition });

        Position? savedPosition = null;
        mockRepo.Setup(r => r.AddAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .Callback<Position, CancellationToken>((p, ct) => savedPosition = p)
            .Returns(Task.CompletedTask);

        mockOrderRepo.Setup(o => o.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new PositionReconciliationService(
            mockRepo.Object,
            mockOrderRepo.Object,
            mockGateway.Object,
            mockUow.Object,
            NullLogger<PositionReconciliationService>.Instance
        );

        // Act
        await service.ReconcileAsync(CancellationToken.None);

        // Assert
        savedPosition.Should().NotBeNull();
        savedPosition!.Symbol.Should().Be("BTCUSDT");
        savedPosition.Side.Should().Be(OrderSide.Buy);
        savedPosition.Quantity.Should().Be(1.5m);
        savedPosition.EntryPrice.Should().Be(49500m);
        savedPosition.Events.Should().HaveCount(1);
        savedPosition.Events.First().EventType.Should().Be("PositionRecovered");
        mockRepo.Verify(r => r.AddAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()), Times.Once);
        mockOrderRepo.Verify(o => o.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Once);
        mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
