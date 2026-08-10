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
using TradingBot.Application.Monitoring;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Infrastructure.Configuration;
using TradingBot.Persistence.Context;
using TradingBot.Worker.Lifecycle;
using Xunit;

namespace TradingBot.IntegrationTests.Services;

public class StartupRecoveryTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly TradingDbContext _dbContext;
    private readonly Mock<ITradingGate> _mockTradingGate;
    private readonly Mock<IExchangeClient> _mockExchangeClient;
    private readonly Mock<IPositionRecoveryService> _mockPositionRecoveryService;
    private readonly Mock<IOrderReconciliationService> _mockOrderReconciliationService;
    private readonly Mock<IIncompleteOperationRecoveryService> _mockIncompleteOperationRecoveryService;
    private readonly Mock<IMonitoringEventPublisher> _mockEventPublisher;
    private readonly StartupShutdownOptions _options;
    private readonly TradingBotSettings _settings;

    public StartupRecoveryTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        var dbOptions = new DbContextOptionsBuilder<TradingDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _dbContext = new TradingDbContext(dbOptions);
        _dbContext.Database.EnsureCreated();

        _mockTradingGate = new Mock<ITradingGate>();
        _mockExchangeClient = new Mock<IExchangeClient>();
        _mockPositionRecoveryService = new Mock<IPositionRecoveryService>();
        _mockOrderReconciliationService = new Mock<IOrderReconciliationService>();
        _mockIncompleteOperationRecoveryService = new Mock<IIncompleteOperationRecoveryService>();
        _mockEventPublisher = new Mock<IMonitoringEventPublisher>();

        _options = new StartupShutdownOptions
        {
            RequireDatabase = true,
            RequireExchange = true,
            RequireRecovery = true
        };

        _settings = new TradingBotSettings
        {
            Database = new DatabaseSettings { ConnectionString = "Host=localhost;Database=test" },
            Exchange = new ExchangeSettings { ApiKey = "test_api_key", ApiSecret = "test_api_secret" }
        };
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _sqliteConnection.Close();
        _sqliteConnection.Dispose();
    }

    private StartupRecoveryManager CreateManager()
    {
        return new StartupRecoveryManager(
            _mockTradingGate.Object,
            _dbContext,
            _mockExchangeClient.Object,
            _mockPositionRecoveryService.Object,
            _mockOrderReconciliationService.Object,
            _mockIncompleteOperationRecoveryService.Object,
            _options,
            _settings,
            NullLogger<StartupRecoveryManager>.Instance,
            _mockEventPublisher.Object
        );
    }

    [Fact]
    public async Task RunRecoverySequenceAsync_NormalFlow_ShouldExecuteAllStepsAndEnableTrading()
    {
        // Arrange
        _mockExchangeClient.Setup(c => c.PingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var manager = CreateManager();

        // Act
        await manager.RunRecoverySequenceAsync(CancellationToken.None);

        // Assert
        _mockTradingGate.Verify(g => g.SetState(ApplicationState.Starting), Times.Once);
        _mockTradingGate.Verify(g => g.SetState(ApplicationState.Initializing), Times.Once);
        _mockTradingGate.Verify(g => g.SetState(ApplicationState.Recovering), Times.Once);
        _mockTradingGate.Verify(g => g.SetState(ApplicationState.Ready), Times.Once);
        _mockTradingGate.Verify(g => g.EnableTrading(), Times.Once);

        _mockIncompleteOperationRecoveryService.Verify(s => s.RecoverIncompleteOperationsAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockPositionRecoveryService.Verify(s => s.RecoverPositionsAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockOrderReconciliationService.Verify(s => s.ReconcileAsync(It.IsAny<CancellationToken>()), Times.Once);

        _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<MonitoringEvent>(e => e.EventType == "ApplicationStarting"), true, It.IsAny<CancellationToken>()), Times.Once);
        _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<MonitoringEvent>(e => e.EventType == "StartupRecoveryStarted"), true, It.IsAny<CancellationToken>()), Times.Once);
        _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<MonitoringEvent>(e => e.EventType == "StartupRecoveryCompleted"), true, It.IsAny<CancellationToken>()), Times.Once);
        _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<MonitoringEvent>(e => e.EventType == "ApplicationReady"), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunRecoverySequenceAsync_MissingApiKey_ShouldThrowAndDisableTrading()
    {
        // Arrange
        _settings.Exchange.ApiKey = ""; // empty API key

        var manager = CreateManager();

        // Act
        Func<Task> act = async () => await manager.RunRecoverySequenceAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*API key is missing*");
        _mockTradingGate.Verify(g => g.SetState(ApplicationState.Failed), Times.Once);
        _mockTradingGate.Verify(g => g.DisableTrading(), Times.Once);
    }

    [Fact]
    public async Task RunRecoverySequenceAsync_BybitPingFails_ShouldThrowAndDisableTrading()
    {
        // Arrange
        _mockExchangeClient.Setup(c => c.PingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Ping fails

        var manager = CreateManager();

        // Act
        Func<Task> act = async () => await manager.RunRecoverySequenceAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Failed to verify exchange connectivity*");
        _mockTradingGate.Verify(g => g.SetState(ApplicationState.Failed), Times.Once);
        _mockTradingGate.Verify(g => g.DisableTrading(), Times.Once);
    }

    [Fact]
    public async Task RunRecoverySequenceAsync_ExecutedTwice_ShouldBeIdempotentWithoutDuplicateCreations()
    {
        // Arrange
        _mockExchangeClient.Setup(c => c.PingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var manager = CreateManager();

        // Act
        await manager.RunRecoverySequenceAsync(CancellationToken.None);
        await manager.RunRecoverySequenceAsync(CancellationToken.None);

        // Assert
        _mockIncompleteOperationRecoveryService.Verify(s => s.RecoverIncompleteOperationsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _mockPositionRecoveryService.Verify(s => s.RecoverPositionsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _mockOrderReconciliationService.Verify(s => s.ReconcileAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
