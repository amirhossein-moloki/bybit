using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingBot.Application.Configuration;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Streams;
using TradingBot.Application.Monitoring;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Telegram.Interfaces;
using TradingBot.Worker.Lifecycle;
using Xunit;

namespace TradingBot.IntegrationTests.Services;

public class GracefulShutdownIntegrationTests
{
    private readonly Mock<ITradingGate> _mockTradingGate;
    private readonly Mock<IHostApplicationLifetime> _mockLifetime;
    private readonly Mock<IExchangeStreamClient> _mockWebSocketClient;
    private readonly Mock<ITelegramClient> _mockTelegramClient;
    private readonly Mock<IMonitoringEventPublisher> _mockEventPublisher;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly StartupShutdownOptions _options;

    public GracefulShutdownIntegrationTests()
    {
        _mockTradingGate = new Mock<ITradingGate>();
        _mockLifetime = new Mock<IHostApplicationLifetime>();
        _mockWebSocketClient = new Mock<IExchangeStreamClient>();
        _mockTelegramClient = new Mock<ITelegramClient>();
        _mockEventPublisher = new Mock<IMonitoringEventPublisher>();
        _mockServiceProvider = new Mock<IServiceProvider>();

        var mockScope = new Mock<IServiceScope>();
        var mockScopeFactory = new Mock<IServiceScopeFactory>();

        mockScope.Setup(s => s.ServiceProvider).Returns(_mockServiceProvider.Object);
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
        _mockServiceProvider.Setup(p => p.GetService(typeof(IServiceScopeFactory))).Returns(mockScopeFactory.Object);
        _mockServiceProvider.Setup(p => p.GetService(typeof(IMonitoringEventPublisher))).Returns(_mockEventPublisher.Object);
        _mockServiceProvider.Setup(p => p.GetService(typeof(IExchangeStreamClient))).Returns(_mockWebSocketClient.Object);
        _mockServiceProvider.Setup(p => p.GetService(typeof(ITelegramClient))).Returns(_mockTelegramClient.Object);

        _options = new StartupShutdownOptions
        {
            ShutdownTimeout = TimeSpan.FromSeconds(2),
            DrainPendingOperations = true
        };

        // Standard setup for application stopping token
        var stoppingCts = new CancellationTokenSource();
        _mockLifetime.Setup(l => l.ApplicationStopping).Returns(stoppingCts.Token);
    }

    private GracefulShutdownManager CreateManager()
    {
        return new GracefulShutdownManager(
            _mockTradingGate.Object,
            _mockLifetime.Object,
            _options,
            NullLogger<GracefulShutdownManager>.Instance,
            _mockServiceProvider.Object
        );
    }

    [Fact]
    public async Task ShutdownAsync_NormalSequence_ShouldSetStoppingAndDisableTradingAndCloseAllConnections()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        await manager.ShutdownAsync(CancellationToken.None);

        // Assert
        // Verify state is transitioned to Stopping first, then Stopped
        _mockTradingGate.Verify(g => g.SetState(ApplicationState.Stopping), Times.Once);
        _mockTradingGate.Verify(g => g.DisableTrading(), Times.Once);

        // Verify WebSocket disconnects
        _mockWebSocketClient.Verify(ws => ws.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Verify Telegram client disconnects
        _mockTelegramClient.Verify(tc => tc.DisconnectAsync(), Times.Once);

        // Verify State transitions to Stopped
        _mockTradingGate.Verify(g => g.SetState(ApplicationState.Stopped), Times.Once);

        // Verify Monitoring Events
        _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<MonitoringEvent>(e => e.EventType == "ShutdownRequested"), true, It.IsAny<CancellationToken>()), Times.Once);
        _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<MonitoringEvent>(e => e.EventType == "TradingDisabled"), true, It.IsAny<CancellationToken>()), Times.Once);
        _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<MonitoringEvent>(e => e.EventType == "WorkersStopping"), true, It.IsAny<CancellationToken>()), Times.Once);
        _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<MonitoringEvent>(e => e.EventType == "ApplicationStopped"), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShutdownAsync_WhenCalledTwice_ShouldBeIdempotentAndOnlyExecuteOnce()
    {
        // Arrange
        _mockTradingGate.Setup(g => g.CurrentState).Returns(ApplicationState.Stopping);

        var manager = CreateManager();

        // Act
        await manager.ShutdownAsync(CancellationToken.None);

        // Assert
        // Since state is already Stopping, should return immediately without executing again
        _mockTradingGate.Verify(g => g.SetState(ApplicationState.Stopping), Times.Never);
        _mockWebSocketClient.Verify(ws => ws.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ShutdownAsync_WithDrainTimeout_ShouldCompleteCorrectlyWithinShutdownTimeout()
    {
        // Arrange
        _options.ShutdownTimeout = TimeSpan.FromMilliseconds(500); // very fast timeout

        var manager = CreateManager();

        // Act
        Func<Task> act = async () => await manager.ShutdownAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        _mockTradingGate.Verify(g => g.SetState(ApplicationState.Stopped), Times.Once);
    }
}
