using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Mappers;
using TradingBot.Application.Models;
using TradingBot.Application.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;
using Symbol = TradingBot.Domain.ValueObjects.Symbol;

namespace TradingBot.IntegrationTests.Services;

public class PositionRecoveryIntegrationTests : IAsyncLifetime
{
    private SqliteConnection? _sqliteConnection;

    public async Task InitializeAsync()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        await _sqliteConnection.OpenAsync();
        using var command = _sqliteConnection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_sqliteConnection != null)
        {
            await _sqliteConnection.CloseAsync();
            await _sqliteConnection.DisposeAsync();
        }
    }

    private TradingDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseSqlite(_sqliteConnection!)
            .Options;

        var context = new TradingDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task StartupRecoveryScenario_ShouldReconcileAndValidateDatabase_WhenAppRestartsWithExistingOpenPositions()
    {
        // 1. Arrange DB with an open position and matching order to satisfy FK
        using var context = CreateDbContext();
        var positionRepo = new PositionRepository(context);
        var orderRepo = new OrderRepository(context);

        var orderId = Guid.NewGuid();
        var symbolVal = new Symbol("ETHUSDT");
        var testOrder = new Order(
            id: orderId,
            clientOrderId: "INT-RECOVERY-TEST",
            symbol: symbolVal,
            side: OrderSide.Buy,
            type: OrderType.Limit,
            quantity: new Quantity(2.0m),
            price: new Money(3000m)
        );
        testOrder.Submit();
        testOrder.Accept("ETHUSDT_Long");
        testOrder.RecordExecution(2.0m, 3000m);
        await orderRepo.AddAsync(testOrder);
        await context.SaveChangesAsync();

        var position = new Position(
            orderId: orderId,
            symbol: "ETHUSDT",
            side: OrderSide.Buy,
            entryPrice: 3000m,
            quantity: 2.0m,
            exchangePositionId: "ETHUSDT_Long"
        );
        await positionRepo.AddAsync(position);
        await context.SaveChangesAsync();

        // 2. Mock Gateway to return updated position state (MarkPrice changed, UnrealizedPnL updated)
        var mockGateway = new Mock<IPositionGateway>();
        var exPosition = new ExchangePositionDto(
            ExchangePositionId: "ETHUSDT_Long",
            Symbol: "ETHUSDT",
            Side: PositionSide.Long,
            Quantity: 2.0m,
            EntryPrice: 3000m,
            MarkPrice: 3200m, // Price has moved up
            Leverage: 10m,
            Margin: 600m,
            UnrealizedPnL: 400m,
            LiquidationPrice: 2700m,
            StopLoss: null,
            TakeProfit: null,
            UpdatedAt: DateTime.UtcNow
        );
        mockGateway.Setup(g => g.GetOpenPositionsAsync())
            .ReturnsAsync(new List<ExchangePositionDto> { exPosition });

        // 3. Initialize Services in separate context to simulate App Restart
        using var restartContext = CreateDbContext();
        var restartRepo = new PositionRepository(restartContext);
        var restartOrderRepo = new OrderRepository(restartContext);
        var restartUow = new UnitOfWork(restartContext, NullLogger<UnitOfWork>.Instance);
        var reconciliationService = new PositionReconciliationService(restartRepo, restartOrderRepo, mockGateway.Object, restartUow, NullLogger<PositionReconciliationService>.Instance);
        var recoveryService = new PositionRecoveryService(reconciliationService, NullLogger<PositionRecoveryService>.Instance);

        // 4. Act - Execute startup recovery
        await recoveryService.RecoverPositionsAsync(CancellationToken.None);

        // 5. Assert - Validate Database state
        using var verifyContext = CreateDbContext();
        var verifyRepo = new PositionRepository(verifyContext);
        var recovered = await verifyRepo.GetByOrderIdAsync(orderId);

        recovered.Should().NotBeNull();
        recovered!.CurrentPrice.Should().Be(3200m);
        recovered.UnrealizedPnL.Should().Be(400m);
        recovered.Status.Should().Be(PositionStatus.Open);
        recovered.IsDesynchronized.Should().BeFalse();
    }

    [Fact]
    public async Task Reconciliation_ShouldHandleMismatchAndUseExchangeAsSourceOfTruth()
    {
        // 1. Arrange DB with an open position and matching order to satisfy FK
        using var context = CreateDbContext();
        var positionRepo = new PositionRepository(context);
        var orderRepo = new OrderRepository(context);

        var orderId = Guid.NewGuid();
        var symbolVal = new Symbol("BTCUSDT");
        var testOrder = new Order(
            id: orderId,
            clientOrderId: "INT-MISMATCH-TEST",
            symbol: symbolVal,
            side: OrderSide.Buy,
            type: OrderType.Limit,
            quantity: new Quantity(1.0m),
            price: new Money(50000m)
        );
        testOrder.Submit();
        testOrder.Accept("BTCUSDT_Long");
        testOrder.RecordExecution(1.0m, 50000m);
        await orderRepo.AddAsync(testOrder);
        await context.SaveChangesAsync();

        var position = new Position(
            orderId: orderId,
            symbol: "BTCUSDT",
            side: OrderSide.Buy,
            entryPrice: 50000m,
            quantity: 1.0m,
            exchangePositionId: "BTCUSDT_Long"
        );
        await positionRepo.AddAsync(position);
        await context.SaveChangesAsync();

        // 2. Gateway reports different quantity (0.5m) and entry price (50500m)
        var mockGateway = new Mock<IPositionGateway>();
        var exPosition = new ExchangePositionDto(
            ExchangePositionId: "BTCUSDT_Long",
            Symbol: "BTCUSDT",
            Side: PositionSide.Long,
            Quantity: 0.5m, // Quantity mismatched
            EntryPrice: 50500m, // Price mismatched
            MarkPrice: 51000m,
            Leverage: 10m,
            Margin: 2500m,
            UnrealizedPnL: 250m,
            LiquidationPrice: 45000m,
            StopLoss: null,
            TakeProfit: null,
            UpdatedAt: DateTime.UtcNow
        );
        mockGateway.Setup(g => g.GetOpenPositionsAsync())
            .ReturnsAsync(new List<ExchangePositionDto> { exPosition });

        // 3. Act - Reconcile
        using var actContext = CreateDbContext();
        var actRepo = new PositionRepository(actContext);
        var actOrderRepo = new OrderRepository(actContext);
        var actUow = new UnitOfWork(actContext, NullLogger<UnitOfWork>.Instance);
        var reconciliationService = new PositionReconciliationService(actRepo, actOrderRepo, mockGateway.Object, actUow, NullLogger<PositionReconciliationService>.Instance);
        await reconciliationService.ReconcileAsync(CancellationToken.None);

        // 4. Assert
        using var verifyContext = CreateDbContext();
        var verifyRepo = new PositionRepository(verifyContext);
        var repaired = await verifyRepo.GetByOrderIdAsync(orderId);

        repaired.Should().NotBeNull();
        repaired!.Quantity.Should().Be(0.5m);
        repaired.EntryPrice.Should().Be(50500m);
        repaired.IsDesynchronized.Should().BeFalse(); // Verified repaired and synced again
    }

    [Fact]
    public async Task Reconciliation_ShouldTransitionToClosed_WhenPositionIsClosedOnExchange()
    {
        // 1. Arrange DB with an open position and matching order to satisfy FK
        using var context = CreateDbContext();
        var positionRepo = new PositionRepository(context);
        var orderRepo = new OrderRepository(context);

        var orderId = Guid.NewGuid();
        var symbolVal = new Symbol("BTCUSDT");
        var testOrder = new Order(
            id: orderId,
            clientOrderId: "INT-CLOSED-TEST",
            symbol: symbolVal,
            side: OrderSide.Buy,
            type: OrderType.Limit,
            quantity: new Quantity(1.0m),
            price: new Money(50000m)
        );
        testOrder.Submit();
        testOrder.Accept("BTCUSDT_Long");
        testOrder.RecordExecution(1.0m, 50000m);
        await orderRepo.AddAsync(testOrder);
        await context.SaveChangesAsync();

        var position = new Position(
            orderId: orderId,
            symbol: "BTCUSDT",
            side: OrderSide.Buy,
            entryPrice: 50000m,
            quantity: 1.0m,
            exchangePositionId: "BTCUSDT_Long"
        );
        await positionRepo.AddAsync(position);
        await context.SaveChangesAsync();

        // 2. Gateway reports 0 positions
        var mockGateway = new Mock<IPositionGateway>();
        mockGateway.Setup(g => g.GetOpenPositionsAsync())
            .ReturnsAsync(new List<ExchangePositionDto>());

        // 3. Act - Reconcile
        using var actContext = CreateDbContext();
        var actRepo = new PositionRepository(actContext);
        var actOrderRepo = new OrderRepository(actContext);
        var actUow = new UnitOfWork(actContext, NullLogger<UnitOfWork>.Instance);
        var reconciliationService = new PositionReconciliationService(actRepo, actOrderRepo, mockGateway.Object, actUow, NullLogger<PositionReconciliationService>.Instance);
        await reconciliationService.ReconcileAsync(CancellationToken.None);

        // 4. Assert
        using var verifyContext = CreateDbContext();
        var verifyRepo = new PositionRepository(verifyContext);
        var closedPos = await verifyRepo.GetByOrderIdAsync(orderId);

        closedPos.Should().NotBeNull();
        closedPos!.Status.Should().Be(PositionStatus.Closed);
        closedPos.RemainingQuantity.Should().Be(0);
    }

    [Fact]
    public async Task Recovery_ShouldCreateUnknownPosition_WhenExchangeHasOpenPositionMissingInDatabase()
    {
        // 1. Arrange empty DB
        using var context = CreateDbContext();

        // 2. Gateway reports 1 active open position
        var mockGateway = new Mock<IPositionGateway>();
        var exPosition = new ExchangePositionDto(
            ExchangePositionId: "SOLUSDT_Short",
            Symbol: "SOLUSDT",
            Side: PositionSide.Short,
            Quantity: 10m,
            EntryPrice: 150m,
            MarkPrice: 145m,
            Leverage: 10m,
            Margin: 150m,
            UnrealizedPnL: 50m,
            LiquidationPrice: 180m,
            StopLoss: null,
            TakeProfit: null,
            UpdatedAt: DateTime.UtcNow
        );
        mockGateway.Setup(g => g.GetOpenPositionsAsync())
            .ReturnsAsync(new List<ExchangePositionDto> { exPosition });

        // 3. Act - Reconcile
        using var actContext = CreateDbContext();
        var actRepo = new PositionRepository(actContext);
        var actOrderRepo = new OrderRepository(actContext);
        var actUow = new UnitOfWork(actContext, NullLogger<UnitOfWork>.Instance);
        var reconciliationService = new PositionReconciliationService(actRepo, actOrderRepo, mockGateway.Object, actUow, NullLogger<PositionReconciliationService>.Instance);
        await reconciliationService.ReconcileAsync(CancellationToken.None);

        // 4. Assert - DB now has recovered Unknown Position
        using var verifyContext = CreateDbContext();
        var verifyRepo = new PositionRepository(verifyContext);
        var allPositions = (await verifyRepo.GetOpenPositionsAsync()).ToList();

        allPositions.Should().ContainSingle();
        var tempRecovered = allPositions.First();

        // Fetch full entity with Events and Targets included
        var recovered = await verifyRepo.GetByIdAsync(tempRecovered.Id);
        recovered.Should().NotBeNull();
        recovered!.Symbol.Should().Be("SOLUSDT");
        recovered.Side.Should().Be(OrderSide.Sell); // Short -> Sell
        recovered.Quantity.Should().Be(10m);
        recovered.EntryPrice.Should().Be(150m);
        recovered.Events.Should().HaveCount(1);
        recovered.Events.First().EventType.Should().Be("PositionRecovered");
    }

    [Fact]
    public async Task Reconciliation_ShouldHandleGatewayExceptionGracefully()
    {
        // Arrange
        using var context = CreateDbContext();
        var positionRepo = new PositionRepository(context);
        var orderRepo = new OrderRepository(context);
        var uow = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);

        var mockGateway = new Mock<IPositionGateway>();
        mockGateway.Setup(g => g.GetOpenPositionsAsync())
            .ThrowsAsync(new Exception("Exchange Timeout")); // Simulating exchange timeout error

        var reconciliationService = new PositionReconciliationService(positionRepo, orderRepo, mockGateway.Object, uow, NullLogger<PositionReconciliationService>.Instance);

        // Act & Assert - Should propagate the exception cleanly for retry handlers or logger
        Func<Task> act = async () => await reconciliationService.ReconcileAsync(CancellationToken.None);
        await act.Should().ThrowAsync<Exception>().WithMessage("Exchange Timeout");
    }
}
