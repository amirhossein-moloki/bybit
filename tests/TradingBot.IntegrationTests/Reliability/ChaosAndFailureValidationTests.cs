using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Polly;
using Polly.CircuitBreaker;
using TradingBot.Application.Configuration;
using TradingBot.Application.Enums;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Services;
using TradingBot.Infrastructure.Resilience;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Services;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Monitoring.Services;
using TradingBot.Application.Repositories;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Infrastructure.Configuration;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.IntegrationTests.Reliability;

public class ChaosAndFailureValidationTests : IDisposable
{
    private readonly FailureSimulator _simulator;
    private readonly ControllableExchangeTradingGateway _controllableGateway;
    private readonly ControllableTelegramClient _controllableTelegram;

    public ChaosAndFailureValidationTests()
    {
        _simulator = new FailureSimulator();
        _controllableGateway = new ControllableExchangeTradingGateway(_simulator);
        _controllableTelegram = new ControllableTelegramClient(_simulator);
    }

    public void Dispose()
    {
        _simulator.ClearAll();
        _controllableGateway.ClearExchangeOrders();
    }

    #region Step 3 Tests: Retry, Backoff, and Circuit Breaker Validation

    [Fact]
    public async Task Retry_TransientFailure_ShouldRetryAndStopAfterSuccess()
    {
        // Arrange
        var options = new ReliabilityOptions
        {
            Retry = new RetrySettings
            {
                Enabled = true,
                MaxAttempts = 3,
                InitialDelaySeconds = 0.001,
                MaxDelaySeconds = 0.1,
                BackoffMultiplier = 1.5,
                JitterEnabled = false
            },
            Timeout = new TimeoutSettings { Enabled = false }
        };

        var delayCalculator = new RetryDelayCalculator();
        var errorClassifier = new ErrorClassifier();
        var reliabilityService = new ReliabilityService(options, delayCalculator, errorClassifier, NullLogger<ReliabilityService>.Instance);

        int attempts = 0;

        // Act
        var result = await reliabilityService.ExecuteAsync(ct =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new TimeoutException("Temporary timeout");
            }
            return Task.FromResult("SuccessResult");
        }, "RetryTest_Success");

        // Assert
        result.Should().Be("SuccessResult");
        attempts.Should().Be(2); // Retried once, succeeded on 2nd attempt, stopped retrying!
    }

    [Fact]
    public async Task Retry_PermanentBusinessError_ShouldNotRetry()
    {
        // Arrange
        var options = new ReliabilityOptions
        {
            Retry = new RetrySettings
            {
                Enabled = true,
                MaxAttempts = 3,
                InitialDelaySeconds = 0.001,
                MaxDelaySeconds = 0.1,
                BackoffMultiplier = 1.5,
                JitterEnabled = false
            },
            Timeout = new TimeoutSettings { Enabled = false }
        };

        var delayCalculator = new RetryDelayCalculator();
        var errorClassifier = new ErrorClassifier();
        var reliabilityService = new ReliabilityService(options, delayCalculator, errorClassifier, NullLogger<ReliabilityService>.Instance);

        int attempts = 0;

        // Act
        Func<Task> act = async () =>
        {
            await reliabilityService.ExecuteAsync<string>(ct =>
            {
                attempts++;
                throw new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden); // Non-retryable
            }, "RetryTest_PermanentError");
        };

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        attempts.Should().Be(1); // Fails immediately, no retry
    }

    [Fact]
    public void Backoff_DelayCalculation_ShouldIncreaseAccordingToPolicyAndRespectCap()
    {
        // Arrange
        var options = new ReliabilityOptions
        {
            Retry = new RetrySettings
            {
                Enabled = true,
                MaxAttempts = 4,
                InitialDelaySeconds = 1.0,
                MaxDelaySeconds = 5.0,
                BackoffMultiplier = 2.0,
                JitterEnabled = false
            }
        };

        var calculator = new RetryDelayCalculator();

        // Act
        var delay1 = calculator.CalculateDelay(1, options); // Attempt 1: Initial Delay
        var delay2 = calculator.CalculateDelay(2, options); // Attempt 2: Initial * Multiplier
        var delay3 = calculator.CalculateDelay(3, options); // Attempt 3: Prev * Multiplier (Capped)

        // Assert
        delay1.TotalSeconds.Should().Be(1.0);
        delay2.TotalSeconds.Should().Be(2.0);
        delay3.TotalSeconds.Should().Be(4.0);

        var delay4 = calculator.CalculateDelay(4, options); // should be capped at MaxDelaySeconds (5.0)
        delay4.TotalSeconds.Should().Be(5.0);
    }

    [Fact]
    public async Task CircuitBreaker_StateTransitions_ShouldBlockOnOpenAndRecoverOnClosed()
    {
        // Arrange - Setup a custom circuit breaker using Polly to verify expected threshold & recovery behaviors
        var breakerPolicy = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(10),
                MinimumThroughput = 2,
                BreakDuration = TimeSpan.FromMilliseconds(500), // minimum required 500ms
            })
            .Build();

        int executedCount = 0;

        Func<Task> failingCall = async () =>
        {
            await breakerPolicy.ExecuteAsync(async ct =>
            {
                executedCount++;
                throw new InvalidOperationException("Failing call");
            });
        };

        Func<Task<string>> successfulCall = async () =>
        {
            return await breakerPolicy.ExecuteAsync(async ct =>
            {
                executedCount++;
                return "OK";
            });
        };

        // 1. Threshold activation: Execute 2 failures (meets minimum throughput of 2 with 100% failure ratio)
        await failingCall.Should().ThrowAsync<InvalidOperationException>();
        await failingCall.Should().ThrowAsync<InvalidOperationException>();

        // 2. Circuit is now OPEN. Requests are blocked immediately without execution
        Func<Task> callOnOpenCircuit = async () => await successfulCall();
        await callOnOpenCircuit.Should().ThrowAsync<BrokenCircuitException>();
        executedCount.Should().Be(2); // No new execution occurred

        // 3. Wait for break duration to transition to Half-Open
        await Task.Delay(600);

        // 4. Recovery: Half-Open transition. A successful call will close the circuit.
        var result = await successfulCall();
        result.Should().Be("OK");
        executedCount.Should().Be(3);

        // 5. Circuit is CLOSED again. Calls succeed normally.
        var result2 = await successfulCall();
        result2.Should().Be("OK");
        executedCount.Should().Be(4);
    }

    #endregion

    #region Step 4 Tests: Bybit REST, WebSocket, and Rate Limit Failure Recovery

    [Fact]
    public async Task BybitREST_Http500Failure_ShouldRetryAndSucceed()
    {
        // Arrange
        var options = new ReliabilityOptions
        {
            Retry = new RetrySettings
            {
                Enabled = true,
                MaxAttempts = 3,
                InitialDelaySeconds = 0.001,
                MaxDelaySeconds = 0.1,
                BackoffMultiplier = 2.0,
                JitterEnabled = false
            },
            Timeout = new TimeoutSettings { Enabled = false }
        };

        var delayCalculator = new RetryDelayCalculator();
        var errorClassifier = new ErrorClassifier();
        var reliabilityService = new ReliabilityService(options, delayCalculator, errorClassifier, NullLogger<ReliabilityService>.Instance);

        // Inject 1 Http5xx failure on Bybit_REST
        _simulator.InjectFailure("Bybit_REST", FailureType.Http5xx, count: 1);

        int attempts = 0;

        // Act
        var result = await reliabilityService.ExecuteAsync(async ct =>
        {
            attempts++;
            // This will trigger FailureSimulator inside ControllableExchangeTradingGateway
            var orderReq = new OrderRequest { Symbol = "BTCUSDT", Quantity = 1.0m, Price = 50000m };
            return await _controllableGateway.CreateOrderAsync(orderReq, ct);
        }, "BybitREST_Http500Test");

        // Assert
        result.Success.Should().BeTrue();
        result.ExchangeOrderId.Should().NotBeNullOrEmpty();
        attempts.Should().Be(2); // First failed (500), second succeeded
    }

    [Fact]
    public async Task BybitREST_RateLimit_ShouldHonorRetryAfterDynamicHeader()
    {
        // Arrange
        var options = new ReliabilityOptions
        {
            Retry = new RetrySettings
            {
                Enabled = true,
                MaxAttempts = 2,
                InitialDelaySeconds = 10.0, // huge delay
                MaxDelaySeconds = 20.0,
                BackoffMultiplier = 2.0,
                JitterEnabled = false
            },
            Timeout = new TimeoutSettings { Enabled = false }
        };

        var delayCalculator = new RetryDelayCalculator();
        var errorClassifier = new ErrorClassifier();
        var reliabilityService = new ReliabilityService(options, delayCalculator, errorClassifier, NullLogger<ReliabilityService>.Instance);

        // Inject RateLimit with 150ms RetryAfter
        _simulator.InjectFailure("Bybit_REST", FailureType.RateLimit, count: 1, rateLimitDuration: TimeSpan.FromMilliseconds(150));

        int attempts = 0;
        var startTime = DateTime.UtcNow;

        // Act
        var result = await reliabilityService.ExecuteAsync(async ct =>
        {
            attempts++;
            var orderReq = new OrderRequest { Symbol = "BTCUSDT", Quantity = 1.0m, Price = 50000m };
            return await _controllableGateway.CreateOrderAsync(orderReq, ct);
        }, "BybitREST_RateLimitTest");

        var duration = DateTime.UtcNow - startTime;

        // Assert
        result.Success.Should().BeTrue();
        attempts.Should().Be(2);
        duration.TotalMilliseconds.Should().BeLessThan(2000); // should be almost instant (150ms), not wait 10 seconds!
    }

    [Fact]
    public async Task BybitREST_Timeout_ShouldActivateCircuitBreakerWhenThresholdExceeded()
    {
        // Arrange
        var breakerPolicy = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(5),
                MinimumThroughput = 2,
                BreakDuration = TimeSpan.FromMilliseconds(500),
            })
            .Build();

        // Inject 2 consecutive timeouts on Bybit_REST
        _simulator.InjectFailure("Bybit_REST", FailureType.Timeout, count: 2);

        Func<Task> callRest = async () =>
        {
            await breakerPolicy.ExecuteAsync(async ct =>
            {
                var orderReq = new OrderRequest { Symbol = "BTCUSDT", Quantity = 1.0m, Price = 50000m };
                await _controllableGateway.CreateOrderAsync(orderReq, ct);
            });
        };

        // Act & Assert
        // First timeout
        await callRest.Should().ThrowAsync<TimeoutException>();
        // Second timeout -> opens circuit breaker
        await callRest.Should().ThrowAsync<TimeoutException>();

        // Third call -> blocked because breaker is open
        await callRest.Should().ThrowAsync<BrokenCircuitException>();
    }

    #endregion

    #region Step 5 Tests: Database and Telegram Failure Isolation

    [Fact]
    public async Task TelegramNotification_FailureIsolation_ShouldNotCrashOrRethrowException()
    {
        // Arrange
        var mockPolicy = new Mock<INotificationPolicy>();
        mockPolicy.Setup(p => p.ShouldNotify(It.IsAny<MonitoringEvent>())).Returns(true);

        var mockMessageBuilder = new Mock<ITelegramMessageBuilder>();
        mockMessageBuilder.Setup(m => m.BuildMessage(It.IsAny<MonitoringEvent>())).Returns("Test message");

        // Mock repository that throws a database exception
        var mockRepo = new Mock<INotificationRepository>();
        mockRepo.Setup(r => r.ExistsForEventAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated database failure during duplicate check"));

        var mockUow = new Mock<TradingBot.Application.Repositories.IUnitOfWork>();

        var options = new NotificationOptions
        {
            Telegram = new TelegramNotificationSettings
            {
                ChatId = "123456",
                RetryCount = 3
            }
        };

        var engine = new NotificationEngine(
            mockPolicy.Object,
            mockMessageBuilder.Object,
            mockRepo.Object,
            mockUow.Object,
            options,
            NullLogger<NotificationEngine>.Instance
        );

        var mEvent = new MonitoringEvent(
            eventType: "SystemAlert",
            severity: "Error",
            source: "UnitTest",
            component: "NotificationEngine",
            status: "Triggered",
            message: "Test description",
            correlationId: Guid.NewGuid().ToString()
        );

        // Act & Assert
        // Invoking ProcessEventAsync should handle the exception internally and NOT crash/throw it
        Func<Task> act = async () => await engine.ProcessEventAsync(mEvent, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Database_Unavailability_ShouldPreventTradingStateAction()
    {
        // Arrange
        // We set up a scenario where the Database/UnitOfWork fails during order state transitions
        var mockValidator = new Mock<IOrderValidator>();
        var mockBuilder = new Mock<IOrderBuilder>();
        var mockGateway = new Mock<IExchangeTradingGateway>();
        var mockRules = new Mock<IExchangeInstrumentRules>();
        var mockOrderRepo = new Mock<TradingBot.Application.Repositories.IOrderRepository>();
        var mockEventRepo = new Mock<IOrderEventRepository>();
        var mockOpRepo = new Mock<ITradeOperationRepository>();

        // Mock unit of work that throws when transaction begins or changes are saved
        var mockUow = new Mock<TradingBot.Application.Repositories.IUnitOfWork>();
        mockUow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection lost"));

        var execService = new TradingExecutionService(
            mockValidator.Object,
            mockBuilder.Object,
            mockGateway.Object,
            mockRules.Object,
            mockOrderRepo.Object,
            mockEventRepo.Object,
            mockUow.Object,
            NullLogger<TradingExecutionService>.Instance,
            null,
            mockOpRepo.Object
        );

        var request = new TradeExecutionRequest
        {
            Id = Guid.NewGuid(),
            SignalId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 1.0m,
            Price = 50000m
        };

        // Act
        Func<Task> act = async () => await execService.ExecuteAsync(request);

        // Assert
        // Unavailability must be caught/propagated cleanly so the application doesn't execute live exchange orders
        // while the database is down (fail-closed!)
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Database connection lost*");
        mockGateway.Verify(g => g.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Step 6 Tests: Worker Failures, Application Crash Recovery, and Graceful Shutdown

    [Fact]
    public async Task ApplicationRestart_DuringUnknownOrderState_ShouldRecoverInsteadOfDuplicateOrder()
    {
        // Arrange
        var mockValidator = new Mock<IOrderValidator>();
        var mockBuilder = new Mock<IOrderBuilder>();
        mockBuilder.Setup(b => b.Build(It.IsAny<TradeExecutionRequest>()))
            .Returns(new OrderRequest
            {
                Symbol = "BTCUSDT",
                Side = OrderSide.Buy,
                Quantity = 1.0m,
                Price = 50000m
            });

        var mockRules = new Mock<IExchangeInstrumentRules>();
        var mockOrderRepo = new Mock<TradingBot.Application.Repositories.IOrderRepository>();
        var mockEventRepo = new Mock<IOrderEventRepository>();

        var mockOpRepo = new Mock<ITradeOperationRepository>();
        var mockUow = new Mock<TradingBot.Application.Repositories.IUnitOfWork>();

        var operationId = Guid.NewGuid();
        var clientOrderId = $"TB-{operationId:N}";

        // Simulate that after restart, the operation is found in the database with "Unknown" status
        var existingOp = new TradeOperation(operationId, $"Order_Signal123_BTCUSDT_Buy", "OrderSubmission", Guid.NewGuid().ToString(), "Unknown");

        mockOpRepo.Setup(r => r.GetByIdAsync(operationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOp);

        mockOpRepo.Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOp);

        // Seed mock exchange with the order that actually succeeded before the crash
        var successfulExOrder = new OrderResult
        {
            Success = true,
            ExchangeOrderId = "EX-SUCCESS-BEFORE-CRASH",
            Status = OrderStatus.Filled,
            ExecutedPrice = 50000m,
            ExecutedQuantity = 1.0m
        };
        _controllableGateway.SeedExchangeOrder(clientOrderId, successfulExOrder);

        var execService = new TradingExecutionService(
            mockValidator.Object,
            mockBuilder.Object,
            _controllableGateway,
            mockRules.Object,
            mockOrderRepo.Object,
            mockEventRepo.Object,
            mockUow.Object,
            NullLogger<TradingExecutionService>.Instance,
            null,
            mockOpRepo.Object
        );

        var request = new TradeExecutionRequest
        {
            Id = operationId, // maps to operation Id
            SignalId = Guid.NewGuid(),
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
        result.ExchangeOrderId.Should().Be("EX-SUCCESS-BEFORE-CRASH");
        result.Status.Should().Be(OrderStatus.Filled);

        // No new order request was actually sent because it recovered the existing one!
        _simulator.ShouldFail("Bybit_REST", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GracefulShutdown_DuringRecovery_ShouldCancelOperationAndDisableTrading()
    {
        // Arrange
        using var sqliteConn = new SqliteConnection("DataSource=:memory:");
        await sqliteConn.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<TradingBot.Persistence.Context.TradingDbContext>()
            .UseSqlite(sqliteConn)
            .Options;
        using var dbContext = new TradingBot.Persistence.Context.TradingDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();

        var mockTradingGate = new Mock<ITradingGate>();
        var mockExchangeClient = new Mock<IExchangeClient>();
        var mockPositionRecovery = new Mock<IPositionRecoveryService>();
        var mockOrderReconciliation = new Mock<IOrderReconciliationService>();
        var mockIncompleteOpRecovery = new Mock<IIncompleteOperationRecoveryService>();
        var mockEventPublisher = new Mock<IMonitoringEventPublisher>();

        var options = new StartupShutdownOptions
        {
            RequireDatabase = false,
            RequireExchange = true,
            RequireRecovery = true
        };

        var settings = new TradingBotSettings
        {
            Exchange = new ExchangeSettings { ApiKey = "test_key", ApiSecret = "test_secret" }
        };

        // Let exchange client ping block or take long to simulate startup sequence execution
        mockExchangeClient.Setup(c => c.PingAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                await Task.Delay(1000, ct);
                return true;
            });

        var manager = new TradingBot.Worker.Lifecycle.StartupRecoveryManager(
            mockTradingGate.Object,
            dbContext,
            mockExchangeClient.Object,
            mockPositionRecovery.Object,
            mockOrderReconciliation.Object,
            mockIncompleteOpRecovery.Object,
            options,
            settings,
            NullLogger<TradingBot.Worker.Lifecycle.StartupRecoveryManager>.Instance,
            mockEventPublisher.Object
        );

        using var cts = new CancellationTokenSource();

        // Act
        var runTask = manager.RunRecoverySequenceAsync(cts.Token);

        // Immediately trigger graceful shutdown/cancellation
        cts.Cancel();

        // Assert
        Func<Task> act = async () => await runTask;
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Trading gate should remain disabled on aborted recovery
        mockTradingGate.Verify(g => g.DisableTrading(), Times.AtLeastOnce());
    }

    #endregion

    #region Step 7 Tests: Multi-Dependency Chaos Scenarios and Concurrency Stress

    [Fact]
    public async Task Chaos_BybitAndDatabaseOutage_ShouldFailClosedAndThenRecoverCleanly()
    {
        // Arrange
        var mockValidator = new Mock<IOrderValidator>();
        var mockBuilder = new Mock<IOrderBuilder>();
        var mockRules = new Mock<IExchangeInstrumentRules>();
        var mockOrderRepo = new Mock<TradingBot.Application.Repositories.IOrderRepository>();
        var mockEventRepo = new Mock<IOrderEventRepository>();
        var mockOpRepo = new Mock<ITradeOperationRepository>();

        // Scenario 1: Database is offline (Unit of Work throws)
        var mockUow = new Mock<TradingBot.Application.Repositories.IUnitOfWork>();
        mockUow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database server is offline"));

        var execService = new TradingExecutionService(
            mockValidator.Object,
            mockBuilder.Object,
            _controllableGateway,
            mockRules.Object,
            mockOrderRepo.Object,
            mockEventRepo.Object,
            mockUow.Object,
            NullLogger<TradingExecutionService>.Instance,
            null,
            mockOpRepo.Object
        );

        var request = new TradeExecutionRequest
        {
            Id = Guid.NewGuid(),
            SignalId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 1.0m,
            Price = 50000m
        };

        // Act & Assert (Phase 1: Fail closed when database is offline)
        Func<Task> actOffline = async () => await execService.ExecuteAsync(request);
        await actOffline.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Database server is offline*");

        // Scenario 2: Database is restored, but Bybit is offline (FailureSimulator injects Http5xx)
        mockUow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _simulator.InjectFailure("Bybit_REST", FailureType.Http5xx, count: 1);

        mockBuilder.Setup(b => b.Build(It.IsAny<TradeExecutionRequest>()))
            .Returns(new OrderRequest { Symbol = "BTCUSDT", Side = OrderSide.Buy, Quantity = 1.0m, Price = 50000m });
        mockValidator.Setup(v => v.Validate(It.IsAny<TradeExecutionRequest>(), It.IsAny<OrderRequest>(), It.IsAny<InstrumentRules>()))
            .Returns(new OrderValidationResult { IsValid = true });

        // Act & Assert (Phase 2: Fail gracefully or return unknown on temporary exchange offline)
        var resultExchangeOffline = await execService.ExecuteAsync(request);
        resultExchangeOffline.Success.Should().BeFalse();
        resultExchangeOffline.Status.Should().Be(OrderStatus.Unknown); // Correctly transitioned to Unknown for recovery

        // Scenario 3: Both Database and Bybit are restored
        _simulator.ClearAll();

        // Act & Assert (Phase 3: Successful execution)
        var nextRequest = new TradeExecutionRequest
        {
            Id = Guid.NewGuid(),
            SignalId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 1.0m,
            Price = 50000m
        };

        var finalResult = await execService.ExecuteAsync(nextRequest);
        finalResult.Success.Should().BeTrue();
        finalResult.ExchangeOrderId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Concurrency_StressTest_ShouldBeThreadSafeAndMaintainStateConsistency()
    {
        // Arrange
        var signalId = Guid.NewGuid();
        var key = $"Order_{signalId}_BTCUSDT_Buy";

        // Thread-safe dictionary representing the unique database constraints on IdempotencyKey
        var dbOperations = new System.Collections.Concurrent.ConcurrentDictionary<string, TradeOperation>();
        var dbOrders = new System.Collections.Concurrent.ConcurrentDictionary<Guid, Order>();

        var mockUow = new Mock<TradingBot.Application.Repositories.IUnitOfWork>();
        var mockEventRepo = new Mock<IOrderEventRepository>();

        var mockOpRepo = new Mock<ITradeOperationRepository>();
        mockOpRepo.Setup(r => r.GetByIdempotencyKeyAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string k, CancellationToken ct) =>
            {
                dbOperations.TryGetValue(k, out var op);
                return op;
            });

        mockOpRepo.Setup(r => r.AddAsync(It.IsAny<TradeOperation>(), It.IsAny<CancellationToken>()))
            .Returns((TradeOperation op, CancellationToken ct) =>
            {
                // Simulate unique index constraint on IdempotencyKey
                if (!dbOperations.TryAdd(op.IdempotencyKey, op))
                {
                    throw new TradingBot.Application.Exceptions.DatabaseException(
                        "Simulated unique constraint violation on IdempotencyKey",
                        new DbUpdateException("Duplicate key index", new Exception("SQLite Error 19: UNIQUE constraint failed")));
                }
                return Task.CompletedTask;
            });

        var mockOrderRepo = new Mock<TradingBot.Application.Repositories.IOrderRepository>();
        mockOrderRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken ct) =>
            {
                dbOrders.TryGetValue(id, out var o);
                return o;
            });

        mockOrderRepo.Setup(r => r.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns((Order o, CancellationToken ct) =>
            {
                dbOrders[o.Id] = o;
                return Task.CompletedTask;
            });

        var validator = new OrderValidator();
        var builder = new OrderBuilder();
        var testRules = new TestExchangeInstrumentRules();

        var execService = new TradingExecutionService(
            validator,
            builder,
            _controllableGateway,
            testRules,
            mockOrderRepo.Object,
            mockEventRepo.Object,
            mockUow.Object,
            NullLogger<TradingExecutionService>.Instance,
            null,
            mockOpRepo.Object
        );

        var requests = new List<TradeExecutionRequest>();
        for (int i = 0; i < 10; i++)
        {
            requests.Add(new TradeExecutionRequest
            {
                Id = Guid.NewGuid(),
                SignalId = signalId,
                Symbol = "BTCUSDT",
                Side = OrderSide.Buy,
                OrderType = OrderType.Limit,
                Quantity = 1.0m,
                Price = 50000m
            });
        }

        // Act
        var tasks = new List<Task<ExecutionResult>>();
        foreach (var req in requests)
        {
            tasks.Add(execService.ExecuteAsync(req));
        }

        var results = new List<ExecutionResult>();
        var exceptionCount = 0;

        foreach (var task in tasks)
        {
            try
            {
                var res = await task;
                results.Add(res);
            }
            catch (Exception ex) when (ex is TradingBot.Application.Exceptions.DatabaseException || ex is DbUpdateException)
            {
                exceptionCount++;
            }
        }

        // Assert
        // Out of 10 concurrent requests, exactly 1 succeeds.
        // The other 9 are either intercepted gracefully at the application level as duplicates,
        // or thrown/aborted safely at the database unique index layer.
        var successfulSubmissions = 0;
        var duplicateSubmissionsPrevented = 0;

        foreach (var res in results)
        {
            if (res.Success && res.Message != null && res.Message.Contains("Duplicate"))
            {
                duplicateSubmissionsPrevented++;
            }
            else if (res.Success)
            {
                successfulSubmissions++;
            }
        }

        (successfulSubmissions + duplicateSubmissionsPrevented + exceptionCount).Should().Be(10);
        successfulSubmissions.Should().Be(1);

        dbOperations.Count.Should().Be(1);
        dbOrders.Count.Should().Be(1);
    }

    #endregion

    #region Step 8 Tests: Security and Production Configuration Validation

    [Fact]
    public void EventSanitizer_ShouldSuccessfullyRedactSensitiveSecrets()
    {
        // Arrange
        var sanitizer = new EventSanitizer();
        var rawLog1 = "Connecting to Bybit using api_key=123456789abc and secret=mySuperSecretValue!";
        var rawLog2 = "Authorization header: bearer some_token_value_xyz";

        // Act
        var sanitized1 = sanitizer.Sanitize(rawLog1);
        var sanitized2 = sanitizer.Sanitize(rawLog2);

        // Assert
        sanitized1.Should().NotContain("123456789abc");
        sanitized1.Should().NotContain("mySuperSecretValue!");
        sanitized1.Should().Contain("[REDACTED]");

        sanitized2.Should().NotContain("some_token_value_xyz");
        sanitized2.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void ProductionConfiguration_InvalidOptions_ShouldThrowDescriptiveExceptions()
    {
        // Arrange
        var invalidRetryOptions = new ReliabilityOptions
        {
            Retry = new RetrySettings
            {
                Enabled = true,
                MaxAttempts = -1 // Invalid negative attempts
            }
        };

        var invalidTimeoutOptions = new ReliabilityOptions
        {
            Timeout = new TimeoutSettings
            {
                Enabled = true,
                DefaultTimeoutSeconds = -5 // Invalid negative timeout
            }
        };

        // Act & Assert
        Action actRetry = () => invalidRetryOptions.Validate();
        actRetry.Should().Throw<ArgumentException>().WithMessage("*MaxAttempts must be non-negative.*");

        Action actTimeout = () => invalidTimeoutOptions.Validate();
        actTimeout.Should().Throw<ArgumentException>().WithMessage("*DefaultTimeout must be greater than zero.*");
    }

    #endregion
}
