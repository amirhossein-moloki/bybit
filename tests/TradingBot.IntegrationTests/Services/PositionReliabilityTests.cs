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
using TradingBot.Application.Repositories;
using TradingBot.Application.Services;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;
using Symbol = TradingBot.Domain.ValueObjects.Symbol;

namespace TradingBot.IntegrationTests.Services;

public class PositionReliabilityTests : IAsyncLifetime
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
    public async Task DuplicateTPEvent_ShouldOnlyExecuteOnce_AndCreateProcessedEvent()
    {
        // Arrange
        using var context = CreateDbContext();
        var positionRepo = new PositionRepository(context);
        var orderRepo = new OrderRepository(context);
        var tradeRepo = new TradeRepository(context);
        var processedEventRepo = new ProcessedEventRepository(context);
        var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);

        var mockGateway = new Mock<IExchangeTradingGateway>();
        var mockRules = new Mock<IExchangeInstrumentRules>();
        var pnlCalc = new PnLCalculator();

        var orderId = Guid.NewGuid();
        var testOrder = new Order(orderId, "CID-11", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1.0m), new Money(50000m));
        testOrder.Submit();
        testOrder.Accept("ex-order-11");
        testOrder.RecordExecution(1.0m, 50000m);
        await orderRepo.AddAsync(testOrder);
        await context.SaveChangesAsync();

        var position = new Position(orderId, "BTCUSDT", OrderSide.Buy, 50000m, 1.0m, exchangePositionId: "BTCUSDT_Long");
        var target = new PositionTarget(position.Id, 1, 55000m, 0.5m, 50m, "Active");
        target.SetExchangeOrderId("ex-tp-99");
        position.Targets.Add(target);

        await positionRepo.AddAsync(position);
        await context.SaveChangesAsync();

        var partialCloseManager = new PartialCloseManager(positionRepo, mockGateway.Object, mockRules.Object, unitOfWork, processedEventRepo, NullLogger<PartialCloseManager>.Instance);

        // Act - Submit TP Hit Event first time
        var success1 = await partialCloseManager.ProcessTakeProfitHitAsync("ex-tp-99", 0.5m, 55000m, CancellationToken.None);

        // Act - Submit TP Hit Event second time (Duplicate!)
        var success2 = await partialCloseManager.ProcessTakeProfitHitAsync("ex-tp-99", 0.5m, 55000m, CancellationToken.None);

        // Assert
        success1.Should().BeTrue();
        success2.Should().BeTrue(); // Returns true for idempotency

        using var verifyContext = CreateDbContext();
        var verifyRepo = new PositionRepository(verifyContext);
        var savedPos = await verifyRepo.GetByIdAsync(position.Id);

        savedPos.Should().NotBeNull();
        savedPos!.RemainingQuantity.Should().Be(0.5m); // Quantity reduced once, NOT twice!
        savedPos.Status.Should().Be(PositionStatus.PartiallyClosed);

        var processedEvents = await verifyContext.ProcessedEvents.ToListAsync();
        processedEvents.Should().ContainSingle();
        processedEvents.First().EventId.Should().Be("TPHit_ex-tp-99");
    }

    [Fact]
    public async Task DuplicateCloseEvent_ShouldOnlyExecuteOnce_AndCreateProcessedEvent()
    {
        // Arrange
        using var context = CreateDbContext();
        var positionRepo = new PositionRepository(context);
        var orderRepo = new OrderRepository(context);
        var tradeRepo = new TradeRepository(context);
        var processedEventRepo = new ProcessedEventRepository(context);
        var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);

        var mockGateway = new Mock<IExchangeTradingGateway>();
        var pnlCalc = new PnLCalculator();

        var orderId = Guid.NewGuid();
        var testOrder = new Order(orderId, "CID-12", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1.0m), new Money(50000m));
        testOrder.Submit();
        testOrder.Accept("ex-order-12");
        testOrder.RecordExecution(1.0m, 50000m);
        await orderRepo.AddAsync(testOrder);
        await context.SaveChangesAsync();

        var position = new Position(orderId, "BTCUSDT", OrderSide.Buy, 50000m, 1.0m, exchangePositionId: "BTCUSDT_Long");
        await positionRepo.AddAsync(position);
        await context.SaveChangesAsync();

        var closeManager = new PositionCloseManager(positionRepo, tradeRepo, mockGateway.Object, pnlCalc, unitOfWork, processedEventRepo, NullLogger<PositionCloseManager>.Instance);

        // Act - Handle Exchange Position Update 0 (Complete Close) first time
        var success1 = await closeManager.HandleExchangePositionUpdateAsync("BTCUSDT", 0m, 51000m, 5m, CloseReason.TakeProfit, null, CancellationToken.None);

        // Act - Handle same update second time (Duplicate!)
        var success2 = await closeManager.HandleExchangePositionUpdateAsync("BTCUSDT", 0m, 51000m, 5m, CloseReason.TakeProfit, null, CancellationToken.None);

        // Assert
        success1.Should().BeTrue();
        success2.Should().BeTrue();

        using var verifyContext = CreateDbContext();
        var trades = await verifyContext.Trades.ToListAsync();
        trades.Should().ContainSingle(); // Trade Settled EXACTLY ONCE!
        trades.First().GrossPnL.Should().Be(1000m);
    }

    [Fact]
    public async Task OptimisticConcurrency_ShouldRaiseConflict_WhenConcurrentUpdatesOccur()
    {
        // Arrange
        using var context1 = CreateDbContext();
        using var context2 = CreateDbContext();

        var positionRepo1 = new PositionRepository(context1);
        var positionRepo2 = new PositionRepository(context2);
        var orderRepo = new OrderRepository(context1);

        var orderId = Guid.NewGuid();
        var testOrder = new Order(orderId, "CID-CONCURRENCY", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1.0m), new Money(50000m));
        testOrder.Submit();
        testOrder.Accept("ex-order-concurrency");
        testOrder.RecordExecution(1.0m, 50000m);
        await orderRepo.AddAsync(testOrder);
        await context1.SaveChangesAsync();

        var position = new Position(orderId, "BTCUSDT", OrderSide.Buy, 50000m, 1.0m, exchangePositionId: "BTCUSDT_Long");
        await positionRepo1.AddAsync(position);
        await context1.SaveChangesAsync();

        // Load position in two distinct DbContexts to simulate concurrent threads
        var pos1 = await positionRepo1.GetByIdAsync(position.Id);
        var pos2 = await positionRepo2.GetByIdAsync(position.Id);

        pos1.Should().NotBeNull();
        pos2.Should().NotBeNull();

        // Thread 1 updates and saves
        pos1!.UpdatePrice(51000m);
        positionRepo1.Update(pos1);
        await context1.SaveChangesAsync();

        // Thread 2 tries to update and save starting from the same stale version
        pos2!.UpdatePrice(52000m);
        positionRepo2.Update(pos2);

        // Act & Assert
        Func<Task> act = async () => await context2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task WebSocketDisconnect_ShouldTrigerRESTFallbackReconciliation()
    {
        // Arrange DB with mismatch
        using var context = CreateDbContext();
        var positionRepo = new PositionRepository(context);
        var orderRepo = new OrderRepository(context);
        var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);

        var orderId = Guid.NewGuid();
        var testOrder = new Order(orderId, "CID-FALLBACK", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1.0m), new Money(50000m));
        testOrder.Submit();
        testOrder.Accept("BTCUSDT_Long");
        testOrder.RecordExecution(1.0m, 50000m);
        await orderRepo.AddAsync(testOrder);
        await context.SaveChangesAsync();

        var position = new Position(orderId, "BTCUSDT", OrderSide.Buy, 50000m, 1.0m, exchangePositionId: "BTCUSDT_Long");
        await positionRepo.AddAsync(position);
        await context.SaveChangesAsync();

        // Gateway reports position closed (Qty = 0)
        var mockGateway = new Mock<IPositionGateway>();
        mockGateway.Setup(g => g.GetOpenPositionsAsync())
            .ReturnsAsync(new List<ExchangePositionDto>()); // Empty represents closed on exchange

        var reconciliationService = new PositionReconciliationService(positionRepo, orderRepo, mockGateway.Object, unitOfWork, NullLogger<PositionReconciliationService>.Instance);

        // Act - REST Fallback reconciliation pass
        await reconciliationService.ReconcileAsync(CancellationToken.None);

        // Assert - DB position is now closed to match exchange source of truth
        using var verifyContext = CreateDbContext();
        var verifyRepo = new PositionRepository(verifyContext);
        var resolved = await verifyRepo.GetByIdAsync(position.Id);

        resolved.Should().NotBeNull();
        resolved!.Status.Should().Be(PositionStatus.Closed);
        resolved.RemainingQuantity.Should().Be(0);
    }

    [Fact]
    public async Task E2E_LongPositionLifecycle_ShouldCalculatePnLAndSettleCorrectly()
    {
        // Arrange
        using var context = CreateDbContext();
        var positionRepo = new PositionRepository(context);
        var orderRepo = new OrderRepository(context);
        var tradeRepo = new TradeRepository(context);
        var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);

        var mockGateway = new Mock<IExchangeTradingGateway>();
        mockGateway.Setup(g => g.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderRequest req, CancellationToken ct) => new OrderResult {
                Success = true,
                ExchangeOrderId = "order-" + Guid.NewGuid().ToString().Substring(0, 8),
                ExecutedQuantity = req.Quantity,
                ExecutedPrice = req.Price > 0 ? req.Price : 50000m
            });

        var mockRules = new Mock<IExchangeInstrumentRules>();
        var pnlCalc = new PnLCalculator();

        var orderId = Guid.NewGuid();
        var testOrder = new Order(orderId, "CID-LONG-E2E", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1.0m), new Money(50000m));
        testOrder.Submit();
        testOrder.Accept("ex-order-long-e2e");
        testOrder.RecordExecution(1.0m, 50000m);
        await orderRepo.AddAsync(testOrder);
        await context.SaveChangesAsync();

        // 1. Open Position
        var position = new Position(orderId, "BTCUSDT", OrderSide.Buy, 50000m, 1.0m, exchangePositionId: "BTCUSDT_Long");
        var target1 = new PositionTarget(position.Id, 1, 55000m, 0.5m, 50m, "Active");
        target1.SetExchangeOrderId("ex-tp-e2e-1");
        position.Targets.Add(target1);

        await positionRepo.AddAsync(position);
        await context.SaveChangesAsync();

        var partialCloseManager = new PartialCloseManager(positionRepo, mockGateway.Object, mockRules.Object, unitOfWork, null!, NullLogger<PartialCloseManager>.Instance);
        var closeManager = new PositionCloseManager(positionRepo, tradeRepo, mockGateway.Object, pnlCalc, unitOfWork, null!, NullLogger<PositionCloseManager>.Instance);

        // 2. Partial Close (TP1 hit)
        var hit1 = await partialCloseManager.ProcessTakeProfitHitAsync("ex-tp-e2e-1", 0.5m, 55000m, CancellationToken.None);
        hit1.Should().BeTrue();

        using var actContext = CreateDbContext();
        var posAfterTP1 = await new PositionRepository(actContext).GetByIdAsync(position.Id);
        posAfterTP1.Should().NotBeNull();
        posAfterTP1!.RemainingQuantity.Should().Be(0.5m);
        posAfterTP1.RealizedPnL.Should().Be(2500m); // (55000 - 50000) * 0.5

        // 3. Final Close
        var closed = await closeManager.ClosePositionAsync(position.Id, CloseReason.Manual, 60000m, "User", CancellationToken.None);
        closed.Should().BeTrue();

        // 4. Verify Final State and Trade Record
        using var verifyContext = CreateDbContext();
        var finalPos = await new PositionRepository(verifyContext).GetByIdAsync(position.Id);
        finalPos.Should().NotBeNull();
        finalPos!.Status.Should().Be(PositionStatus.Closed);
        finalPos.RemainingQuantity.Should().Be(0);
        finalPos.RealizedPnL.Should().Be(7500m); // 2500 + (60000 - 50000) * 0.5

        var trades = await verifyContext.Trades.ToListAsync();
        trades.Should().ContainSingle();
        var trade = trades.First();
        trade.GrossPnL.Should().Be(7500m);
        trade.NetPnL.Should().Be(7500m);
        trade.CloseReason.Should().Be(CloseReason.Manual);
    }

    [Fact]
    public async Task E2E_ShortPositionLifecycle_ShouldCalculatePnLAndSettleCorrectly()
    {
        // Arrange
        using var context = CreateDbContext();
        var positionRepo = new PositionRepository(context);
        var orderRepo = new OrderRepository(context);
        var tradeRepo = new TradeRepository(context);
        var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);

        var mockGateway = new Mock<IExchangeTradingGateway>();
        mockGateway.Setup(g => g.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderRequest req, CancellationToken ct) => new OrderResult {
                Success = true,
                ExchangeOrderId = "order-" + Guid.NewGuid().ToString().Substring(0, 8),
                ExecutedQuantity = req.Quantity,
                ExecutedPrice = req.Price > 0 ? req.Price : 50000m
            });

        var mockRules = new Mock<IExchangeInstrumentRules>();
        var pnlCalc = new PnLCalculator();

        var orderId = Guid.NewGuid();
        var testOrder = new Order(orderId, "CID-SHORT-E2E", new Symbol("BTCUSDT"), OrderSide.Sell, OrderType.Limit, new Quantity(1.0m), new Money(50000m));
        testOrder.Submit();
        testOrder.Accept("ex-order-short-e2e");
        testOrder.RecordExecution(1.0m, 50000m);
        await orderRepo.AddAsync(testOrder);
        await context.SaveChangesAsync();

        // 1. Open Position
        var position = new Position(orderId, "BTCUSDT", OrderSide.Sell, 50000m, 1.0m, exchangePositionId: "BTCUSDT_Short");
        var target1 = new PositionTarget(position.Id, 1, 45000m, 0.5m, 50m, "Active");
        target1.SetExchangeOrderId("ex-tp-e2e-2");
        position.Targets.Add(target1);

        await positionRepo.AddAsync(position);
        await context.SaveChangesAsync();

        var partialCloseManager = new PartialCloseManager(positionRepo, mockGateway.Object, mockRules.Object, unitOfWork, null!, NullLogger<PartialCloseManager>.Instance);
        var closeManager = new PositionCloseManager(positionRepo, tradeRepo, mockGateway.Object, pnlCalc, unitOfWork, null!, NullLogger<PositionCloseManager>.Instance);

        // 2. Partial Close (TP1 hit)
        var hit1 = await partialCloseManager.ProcessTakeProfitHitAsync("ex-tp-e2e-2", 0.5m, 45000m, CancellationToken.None);
        hit1.Should().BeTrue();

        using var actContext = CreateDbContext();
        var posAfterTP1 = await new PositionRepository(actContext).GetByIdAsync(position.Id);
        posAfterTP1.Should().NotBeNull();
        posAfterTP1!.RemainingQuantity.Should().Be(0.5m);
        posAfterTP1.RealizedPnL.Should().Be(2500m); // (50000 - 45000) * 0.5

        // 3. Final Close
        var closed = await closeManager.ClosePositionAsync(position.Id, CloseReason.Manual, 40000m, "User", CancellationToken.None);
        closed.Should().BeTrue();

        // 4. Verify Final State and Trade Record
        using var verifyContext = CreateDbContext();
        var finalPos = await new PositionRepository(verifyContext).GetByIdAsync(position.Id);
        finalPos.Should().NotBeNull();
        finalPos!.Status.Should().Be(PositionStatus.Closed);
        finalPos.RemainingQuantity.Should().Be(0);
        finalPos.RealizedPnL.Should().Be(7500m); // 2500 + (50000 - 40000) * 0.5

        var trades = await verifyContext.Trades.ToListAsync();
        trades.Should().ContainSingle();
        var trade = trades.First();
        trade.GrossPnL.Should().Be(7500m);
        trade.NetPnL.Should().Be(7500m);
        trade.CloseReason.Should().Be(CloseReason.Manual);
    }
}
