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
using TradingBot.Application.Repositories;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Application.Trading.Execution.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;
using Symbol = TradingBot.Domain.ValueObjects.Symbol;

namespace TradingBot.IntegrationTests.Execution;

public class OrderLifecycleIntegrationTests : IAsyncLifetime
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
    public async Task Execution_E2E_Success_ShouldFollowStateMachineAndAuditTrail()
    {
        // Arrange
        using var context = CreateDbContext();

        // Seed a valid Signal to satisfy the foreign key constraint
        var signal = new Signal("TELEGRAM", "BUY BTCUSDT", "BTCUSDT", OrderSide.Buy, 45000m, 0.05m);
        context.Signals.Add(signal);
        await context.SaveChangesAsync();

        var orderRepo = new OrderRepository(context);
        var orderEventRepo = new OrderEventRepository(context);
        var uow = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);

        var validator = new OrderValidator();
        var builder = new OrderBuilder();
        var instrumentRules = new TestExchangeInstrumentRules();
        var mockGateway = new TestExchangeTradingGateway(); // default returns success Filled

        var service = new TradingExecutionService(
            validator,
            builder,
            mockGateway,
            instrumentRules,
            orderRepo,
            orderEventRepo,
            uow,
            NullLogger<TradingExecutionService>.Instance);

        var request = new TradeExecutionRequest
        {
            SignalId = signal.Id,
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 0.05m,
            Price = 45000m
        };

        // Act
        var result = await service.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Status.Should().Be(OrderStatus.Filled);

        // Verify order persisted in Db
        using var context2 = CreateDbContext();
        var persistedOrder = await context2.Orders.FirstOrDefaultAsync(o => o.SignalId == signal.Id);
        persistedOrder.Should().NotBeNull();
        persistedOrder!.Status.Should().Be(OrderStatus.Filled);
        persistedOrder.ExecutedQuantity.Should().Be(0.05m);
        persistedOrder.ExecutedPrice.Should().Be(45000m);

        // Verify events persisted in Db
        var events = await context2.OrderEvents.Where(e => e.OrderId == persistedOrder.Id).OrderBy(e => e.CreatedAt).ToListAsync();
        events.Should().HaveCountGreaterOrEqualTo(3);
        events[0].NewStatus.Should().Be(OrderStatus.Pending);
        events.Last().NewStatus.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public async Task Execution_NetworkTimeout_ShouldMarkUnknown_AndReconcileSuccessfully()
    {
        // Arrange
        using var context = CreateDbContext();

        // Seed a valid Signal to satisfy the foreign key constraint
        var signal = new Signal("TELEGRAM", "BUY BTCUSDT", "BTCUSDT", OrderSide.Buy, 45000m, 0.05m);
        context.Signals.Add(signal);
        await context.SaveChangesAsync();

        var orderRepo = new OrderRepository(context);
        var orderEventRepo = new OrderEventRepository(context);
        var uow = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);

        var validator = new OrderValidator();
        var builder = new OrderBuilder();
        var instrumentRules = new TestExchangeInstrumentRules();

        // Simulate a timeout by throwing TaskCanceledException
        var mockGateway = new Mock<IExchangeTradingGateway>();
        mockGateway.Setup(g => g.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("Submission timeout."));

        var service = new TradingExecutionService(
            validator,
            builder,
            mockGateway.Object,
            instrumentRules,
            orderRepo,
            orderEventRepo,
            uow,
            NullLogger<TradingExecutionService>.Instance);

        var request = new TradeExecutionRequest
        {
            SignalId = signal.Id,
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 0.05m,
            Price = 45000m
        };

        // Act
        var result = await service.ExecuteAsync(request);

        // Assert submission failure with Unknown status
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Status.Should().Be(OrderStatus.Unknown);

        // Verify order is saved locally as Unknown
        using var context2 = CreateDbContext();
        var persistedOrder = await context2.Orders.FirstOrDefaultAsync(o => o.SignalId == signal.Id);
        persistedOrder.Should().NotBeNull();
        persistedOrder!.Status.Should().Be(OrderStatus.Unknown);

        // Setup Gateway to return Filled order details upon query (Reconciliation)
        mockGateway.Setup(g => g.GetOrderAsync(persistedOrder.ClientOrderId, "BTCUSDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderResult
            {
                Success = true,
                ExchangeOrderId = "EX-RECOVERED-123",
                Status = OrderStatus.Filled,
                ExecutedQuantity = 0.05m,
                ExecutedPrice = 45000m
            });

        var reconciliationService = new OrderReconciliationService(
            new OrderRepository(context2),
            new OrderEventRepository(context2),
            mockGateway.Object,
            new UnitOfWork(context2, NullLogger<UnitOfWork>.Instance),
            NullLogger<OrderReconciliationService>.Instance);

        // Act Reconciliation
        await reconciliationService.ReconcileAsync();

        // Verify status upgraded to Filled and ExchangeOrderId linked
        using var context3 = CreateDbContext();
        var recoveredOrder = await context3.Orders.FindAsync(persistedOrder.Id);
        recoveredOrder.Should().NotBeNull();
        recoveredOrder!.Status.Should().Be(OrderStatus.Filled);
        recoveredOrder.ExchangeOrderId.Should().Be("EX-RECOVERED-123");
        recoveredOrder.ExecutedQuantity.Should().Be(0.05m);
        recoveredOrder.ExecutedPrice.Should().Be(45000m);
    }

    [Fact]
    public async Task Execution_DuplicateRequest_ShouldReturnSameExecution_WithoutContactingGatewayAgain()
    {
        // Arrange
        using var context = CreateDbContext();

        // Seed a valid Signal to satisfy the foreign key constraint
        var signal = new Signal("TELEGRAM", "BUY BTCUSDT", "BTCUSDT", OrderSide.Buy, 45000m, 0.05m);
        context.Signals.Add(signal);
        await context.SaveChangesAsync();

        var orderRepo = new OrderRepository(context);
        var orderEventRepo = new OrderEventRepository(context);
        var uow = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);

        var validator = new OrderValidator();
        var builder = new OrderBuilder();
        var instrumentRules = new TestExchangeInstrumentRules();

        var mockGateway = new Mock<IExchangeTradingGateway>();
        mockGateway.Setup(g => g.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderResult
            {
                Success = true,
                ExchangeOrderId = "EX-DUP-999",
                Status = OrderStatus.New
            });

        var service = new TradingExecutionService(
            validator,
            builder,
            mockGateway.Object,
            instrumentRules,
            orderRepo,
            orderEventRepo,
            uow,
            NullLogger<TradingExecutionService>.Instance);

        var request = new TradeExecutionRequest
        {
            SignalId = signal.Id,
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 0.05m,
            Price = 45000m
        };

        // Act - Call 1 (Will save to DB and hit Gateway)
        var result1 = await service.ExecuteAsync(request);

        // Act - Call 2 (Should load from DB, check idempotency, and bypass Gateway)
        var result2 = await service.ExecuteAsync(request);

        // Assert
        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();
        result1.ExchangeOrderId.Should().Be("EX-DUP-999");
        result2.ExchangeOrderId.Should().Be("EX-DUP-999");
        result2.Message.Should().Contain("Duplicate request detected");

        mockGateway.Verify(g => g.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
