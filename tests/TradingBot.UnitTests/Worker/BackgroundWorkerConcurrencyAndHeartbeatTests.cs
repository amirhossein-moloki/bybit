using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingBot.Application.Interfaces.Streams;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Models.Events;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Worker;
using Xunit;

namespace TradingBot.UnitTests.Worker;

public class BackgroundWorkerConcurrencyAndHeartbeatTests
{
    [Fact]
    public async Task NotificationWorker_ShouldHandleConcurrencyConflicts_DuringClaimAndResultSave()
    {
        // Arrange
        var healthRegistry = new WorkerHealthRegistry();
        var mockChannel = new Mock<INotificationChannel>();
        mockChannel.Setup(c => c.ChannelName).Returns("Telegram");
        mockChannel.Setup(c => c.SendAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotificationDeliveryResult.AsSuccess());

        var mockNotifRepo = new Mock<INotificationRepository>();
        var mockUow = new Mock<IUnitOfWork>();

        var testNotif = new Notification(
            eventId: Guid.NewGuid(),
            eventType: "TestEvent",
            severity: "INFO",
            channel: "Telegram",
            recipient: "12345",
            title: "Test",
            message: "Hello"
        );

        mockNotifRepo.Setup(r => r.GetPendingAndRetryScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Notification> { testNotif });

        mockNotifRepo.Setup(r => r.GetByIdAsync(testNotif.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testNotif);

        // First SaveChangesAsync call (claiming) throws concurrency conflict, then succeeds on retry or second call
        int saveCount = 0;
        mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                saveCount++;
                if (saveCount == 1)
                {
                    throw new DbUpdateConcurrencyException("Claiming conflict", new List<Microsoft.EntityFrameworkCore.Update.IUpdateEntry>());
                }
                return Task.FromResult(1);
            });

        var services = new ServiceCollection();
        services.AddSingleton(mockNotifRepo.Object);
        services.AddSingleton(mockUow.Object);
        var serviceProvider = services.BuildServiceProvider();

        var options = new NotificationOptions
        {
            Enabled = true,
            Telegram = new TelegramNotificationSettings { ChatId = "12345" }
        };

        var worker = new NotificationWorker(
            serviceProvider,
            options,
            NullLogger<NotificationWorker>.Instance,
            healthRegistry,
            new[] { mockChannel.Object }
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Act
        var executeTask = worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        // Assert: Process completed without crashing or throwing
        healthRegistry.GetWorkerHeartbeats().Should().ContainKey("NotificationWorker");
    }

    [Fact]
    public async Task PositionSyncBackgroundService_ShouldRecordPeriodicHeartbeat_WithoutEvents()
    {
        // Arrange
        var healthRegistry = new WorkerHealthRegistry();
        var mockStream = new Mock<IPositionStream>();

        // Stream never yields any events
        mockStream.Setup(s => s.ReceiveEventsAsync(It.IsAny<CancellationToken>()))
            .Returns(GetEmptyAsyncEnumerable<PositionUpdateEvent>());

        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var worker = new PositionSyncBackgroundService(
            mockStream.Object,
            serviceProvider,
            NullLogger<PositionSyncBackgroundService>.Instance,
            healthRegistry
        );

        using var cts = new CancellationTokenSource();

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100); // Allow periodic heartbeat loop to run
        var heartbeats = healthRegistry.GetWorkerHeartbeats();
        var runningStatus = heartbeats[nameof(PositionSyncBackgroundService)].Status;
        var lastHb = heartbeats[nameof(PositionSyncBackgroundService)].LastHeartbeatAt;

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Assert
        heartbeats.Should().ContainKey(nameof(PositionSyncBackgroundService));
        runningStatus.Should().Be("Running");
        lastHb.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task OrderSyncBackgroundService_ShouldRecordPeriodicHeartbeat_WithoutEvents()
    {
        // Arrange
        var healthRegistry = new WorkerHealthRegistry();
        var mockStream = new Mock<IOrderStream>();

        mockStream.Setup(s => s.ReceiveEventsAsync(It.IsAny<CancellationToken>()))
            .Returns(GetEmptyAsyncEnumerable<OrderUpdateEvent>());

        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var worker = new OrderSyncBackgroundService(
            mockStream.Object,
            serviceProvider,
            NullLogger<OrderSyncBackgroundService>.Instance,
            healthRegistry
        );

        using var cts = new CancellationTokenSource();

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        var heartbeats = healthRegistry.GetWorkerHeartbeats();
        var runningStatus = heartbeats[nameof(OrderSyncBackgroundService)].Status;
        var lastHb = heartbeats[nameof(OrderSyncBackgroundService)].LastHeartbeatAt;

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Assert
        heartbeats.Should().ContainKey(nameof(OrderSyncBackgroundService));
        runningStatus.Should().Be("Running");
        lastHb.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task MarketDataBackgroundService_ShouldRecordPeriodicHeartbeat_WithoutEvents()
    {
        // Arrange
        var healthRegistry = new WorkerHealthRegistry();
        var mockStream = new Mock<IMarketStream>();

        mockStream.Setup(s => s.SubscribeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mockStream.Setup(s => s.ReceiveEventsAsync(It.IsAny<CancellationToken>()))
            .Returns(GetEmptyAsyncEnumerable<MarketTickerUpdateEvent>());

        var worker = new MarketDataBackgroundService(
            mockStream.Object,
            NullLogger<MarketDataBackgroundService>.Instance,
            healthRegistry
        );

        using var cts = new CancellationTokenSource();

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        var heartbeats = healthRegistry.GetWorkerHeartbeats();
        var runningStatus = heartbeats[nameof(MarketDataBackgroundService)].Status;
        var lastHb = heartbeats[nameof(MarketDataBackgroundService)].LastHeartbeatAt;

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Assert
        heartbeats.Should().ContainKey(nameof(MarketDataBackgroundService));
        runningStatus.Should().Be("Running");
        lastHb.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    private static async IAsyncEnumerable<T> GetEmptyAsyncEnumerable<T>()
    {
        await Task.Delay(5000); // Simulate blocking stream waiting for items
        yield break;
    }
}
