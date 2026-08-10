using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingBot.Application.Configuration;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Application.Models;
using TradingBot.Application.Repositories;
using TradingBot.Application.Services;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Application.Trading.Execution.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using TradingBot.Worker;
using Xunit;

namespace TradingBot.IntegrationTests.Execution;

public class IdempotencyAndRecoveryTests : IAsyncLifetime
{
    private SqliteConnection? _sqliteConnection;
    private TradingDbContext _dbContext = null!;
    private UnitOfWork _unitOfWork = null!;
    private ProcessedEventRepository _processedEventRepo = null!;
    private TradeOperationRepository _tradeOperationRepo = null!;
    private SignalRepository _signalRepo = null!;
    private OrderRepository _orderRepo = null!;

    public async Task InitializeAsync()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        await _sqliteConnection.OpenAsync();

        using var command = _sqliteConnection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync();

        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _dbContext = new TradingDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _unitOfWork = new UnitOfWork(_dbContext, NullLogger<UnitOfWork>.Instance);
        _processedEventRepo = new ProcessedEventRepository(_dbContext);
        _tradeOperationRepo = new TradeOperationRepository(_dbContext);
        _signalRepo = new SignalRepository(_dbContext);
        _orderRepo = new OrderRepository(_dbContext);
    }

    public async Task DisposeAsync()
    {
        if (_dbContext != null)
        {
            await _dbContext.DisposeAsync();
        }
        if (_sqliteConnection != null)
        {
            await _sqliteConnection.CloseAsync();
            await _sqliteConnection.DisposeAsync();
        }
    }

    [Fact]
    public async Task EventIdempotency_TryRegisterEvent_ShouldOnlySucceedForFirstAttempt()
    {
        // Arrange
        var eventId = "Event-Filled-12345";
        var eventType = "OrderFilled";

        // Act
        var firstResult = await _processedEventRepo.TryRegisterEventAsync(eventId, eventType, Guid.NewGuid(), "EX-123");
        var secondResult = await _processedEventRepo.TryRegisterEventAsync(eventId, eventType, Guid.NewGuid(), "EX-123");

        // Assert
        firstResult.Should().BeTrue();
        secondResult.Should().BeFalse();

        var exists = await _processedEventRepo.ExistsAsync(eventId);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SignalStorage_StoreAsync_ConcurrentDuplicate_ShouldBeIgnoredGracefully()
    {
        // Arrange
        var mockMetrics = new Mock<ISignalStorageMetrics>();
        var storageService = new SignalStorageService(
            _signalRepo,
            _unitOfWork,
            mockMetrics.Object,
            NullLogger<SignalStorageService>.Instance
        );

        var candidate = new SignalCandidate
        {
            ChannelId = 987654321,
            MessageId = 11111,
            RawText = "BUY BTCUSDT entry 50000",
            DetectedSymbol = "BTCUSDT",
            DetectedSide = "BUY",
            DetectedAt = DateTime.UtcNow
        };

        // Act
        // Save first time
        await storageService.StoreAsync(candidate);

        // Attempt second time (Will find duplicate at application level and return)
        await storageService.StoreAsync(candidate);

        // Assert
        var allSignals = await _signalRepo.GetAllAsync();
        allSignals.Should().ContainSingle();

        mockMetrics.Verify(m => m.IncrementDuplicatesIgnored(), Times.Once);
    }

    [Fact]
    public async Task OrderSubmission_DuplicateRequest_ShouldReturnExistingCompletedOrder_WithoutCreatingNewOne()
    {
        // Arrange
        var signalId = Guid.NewGuid();

        // Seed database with a valid Signal to satisfy the foreign key constraint
        var signal = new Signal(signalId.ToString(), "BUY BTCUSDT", "BTCUSDT", OrderSide.Buy, 50000m, 1.0m);
        await _unitOfWork.BeginTransactionAsync();
        await _signalRepo.SaveAsync(signal);
        await _unitOfWork.SaveChangesAsync();
        await _unitOfWork.CommitTransactionAsync();

        var key = $"Order_{signal.Id}_BTCUSDT_Buy";
        var operation = new TradeOperation(key, "OrderSubmission", Guid.NewGuid().ToString(), "Completed");

        await _unitOfWork.BeginTransactionAsync();
        await _tradeOperationRepo.AddAsync(operation);
        await _unitOfWork.SaveChangesAsync();
        await _unitOfWork.CommitTransactionAsync();

        var localOrder = new Order(
            operation.Id,
            $"TB-{operation.Id:N}",
            new Domain.ValueObjects.Symbol("BTCUSDT"),
            OrderSide.Buy,
            OrderType.Limit,
            new Domain.ValueObjects.Quantity(1.0m),
            new Domain.ValueObjects.Money(50000m),
            signal.Id
        );
        localOrder.SetExchangeDetails("EX-SUBMIT-111", "Bybit");
        localOrder.UpdateStatus(OrderStatus.Filled);

        await _unitOfWork.BeginTransactionAsync();
        await _orderRepo.AddAsync(localOrder);
        await _unitOfWork.SaveChangesAsync();
        await _unitOfWork.CommitTransactionAsync();

        var mockValidator = new Mock<IOrderValidator>();
        var mockBuilder = new Mock<IOrderBuilder>();
        var mockGateway = new Mock<IExchangeTradingGateway>();
        var mockRules = new Mock<IExchangeInstrumentRules>();

        var execService = new TradingExecutionService(
            mockValidator.Object,
            mockBuilder.Object,
            mockGateway.Object,
            mockRules.Object,
            _orderRepo,
            new Mock<IOrderEventRepository>().Object,
            _unitOfWork,
            NullLogger<TradingExecutionService>.Instance,
            null,
            _tradeOperationRepo
        );

        var request = new TradeExecutionRequest
        {
            Id = Guid.NewGuid(),
            SignalId = signal.Id,
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 1.0m,
            Price = 50000m
        };

        // Act
        var result = await execService.ExecuteAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.OrderId.Should().Be(localOrder.Id);
        result.ExchangeOrderId.Should().Be("EX-SUBMIT-111");
        result.Status.Should().Be(OrderStatus.Filled);
        result.Message.Should().Contain("Duplicate request detected");

        mockGateway.Verify(g => g.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrderSubmission_UnknownStateResolution_ExchangeOrderFound_ShouldRecoverOrder()
    {
        // Arrange
        var signalId = Guid.NewGuid();

        var signal = new Signal(signalId.ToString(), "BUY BTCUSDT", "BTCUSDT", OrderSide.Buy, 50000m, 1.0m);
        await _unitOfWork.BeginTransactionAsync();
        await _signalRepo.SaveAsync(signal);
        await _unitOfWork.SaveChangesAsync();
        await _unitOfWork.CommitTransactionAsync();

        var key = $"Order_{signal.Id}_BTCUSDT_Buy";
        var operation = new TradeOperation(key, "OrderSubmission", Guid.NewGuid().ToString(), "Unknown");

        await _unitOfWork.BeginTransactionAsync();
        await _tradeOperationRepo.AddAsync(operation);
        await _unitOfWork.SaveChangesAsync();
        await _unitOfWork.CommitTransactionAsync();

        var clientOrderId = $"TB-{operation.Id:N}";

        var mockValidator = new Mock<IOrderValidator>();
        var mockBuilder = new Mock<IOrderBuilder>();
        var mockGateway = new Mock<IExchangeTradingGateway>();
        var mockRules = new Mock<IExchangeInstrumentRules>();

        mockGateway.Setup(g => g.GetOrderAsync(clientOrderId, "BTCUSDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderResult
            {
                Success = true,
                ExchangeOrderId = "EX-RECOVERED-333",
                Status = OrderStatus.Filled,
                ExecutedPrice = 50000m,
                ExecutedQuantity = 1.0m
            });

        var execService = new TradingExecutionService(
            mockValidator.Object,
            mockBuilder.Object,
            mockGateway.Object,
            mockRules.Object,
            _orderRepo,
            new Mock<IOrderEventRepository>().Object,
            _unitOfWork,
            NullLogger<TradingExecutionService>.Instance,
            null,
            _tradeOperationRepo
        );

        var request = new TradeExecutionRequest
        {
            Id = Guid.NewGuid(),
            SignalId = signal.Id,
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 1.0m,
            Price = 50000m
        };

        // Act
        var result = await execService.ExecuteAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.OrderId.Should().Be(operation.Id);
        result.ExchangeOrderId.Should().Be("EX-RECOVERED-333");
        result.Status.Should().Be(OrderStatus.Filled);

        var savedOrder = await _orderRepo.GetByIdAsync(operation.Id);
        savedOrder.Should().NotBeNull();
        savedOrder!.Status.Should().Be(OrderStatus.Filled);
        savedOrder.ExchangeOrderId.Should().Be("EX-RECOVERED-333");

        var updatedOp = await _tradeOperationRepo.GetByIdAsync(operation.Id);
        updatedOp!.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task OrderSubmission_UnknownStateResolution_ExchangeOrderNotFound_ShouldAllowSafeRetry()
    {
        // Arrange
        var signalId = Guid.NewGuid();

        var signal = new Signal(signalId.ToString(), "BUY BTCUSDT", "BTCUSDT", OrderSide.Buy, 50000m, 1.0m);
        await _unitOfWork.BeginTransactionAsync();
        await _signalRepo.SaveAsync(signal);
        await _unitOfWork.SaveChangesAsync();
        await _unitOfWork.CommitTransactionAsync();

        var key = $"Order_{signal.Id}_BTCUSDT_Buy";
        var operation = new TradeOperation(key, "OrderSubmission", Guid.NewGuid().ToString(), "Unknown");

        await _unitOfWork.BeginTransactionAsync();
        await _tradeOperationRepo.AddAsync(operation);
        await _unitOfWork.SaveChangesAsync();
        await _unitOfWork.CommitTransactionAsync();

        var clientOrderId = $"TB-{operation.Id:N}";

        var validator = new OrderValidator();
        var builder = new OrderBuilder();
        var mockGateway = new Mock<IExchangeTradingGateway>();
        var testRules = new TestExchangeInstrumentRules();

        // Query returns NotFound
        mockGateway.Setup(g => g.GetOrderAsync(clientOrderId, "BTCUSDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderResult
            {
                Success = false,
                ErrorCode = "ORDER_NOT_FOUND",
                ErrorMessage = "Order not found"
            });

        // Submit works
        mockGateway.Setup(g => g.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderResult
            {
                Success = true,
                ExchangeOrderId = "EX-NEWLY-CREATED",
                Status = OrderStatus.New
            });

        var execService = new TradingExecutionService(
            validator,
            builder,
            mockGateway.Object,
            testRules,
            _orderRepo,
            new Mock<IOrderEventRepository>().Object,
            _unitOfWork,
            NullLogger<TradingExecutionService>.Instance,
            null,
            _tradeOperationRepo
        );

        var request = new TradeExecutionRequest
        {
            Id = Guid.NewGuid(),
            SignalId = signal.Id,
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 1.0m,
            Price = 50000m
        };

        // Act
        var result = await execService.ExecuteAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.ExchangeOrderId.Should().Be("EX-NEWLY-CREATED");

        mockGateway.Verify(g => g.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
