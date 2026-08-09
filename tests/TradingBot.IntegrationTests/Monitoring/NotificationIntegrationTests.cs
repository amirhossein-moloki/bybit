using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.IntegrationTests.Monitoring;

public class NotificationIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public NotificationIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    [Fact]
    public async Task EventToNotificationPipeline_ShouldProcessEventAndCreatePendingNotification_EndToEnd()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IMonitoringEventPublisher>();
        var notificationRepo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var correlationId = $"NotifyTrace-{Guid.NewGuid():N}";
        var testMessage = $"Integration Test Event: {Guid.NewGuid()}";

        var @event = new MonitoringEvent(
            eventType: "ApplicationStarted",
            severity: "INFORMATION",
            source: "TestRunner",
            component: "E2ETest",
            status: "Succeeded",
            message: testMessage,
            correlationId: correlationId
        );

        // Act
        await publisher.PublishAsync(@event, forceSynchronous: false);

        // Wait up to 3 seconds for background worker to consume event and create a notification
        Notification? createdNotification = null;
        for (int i = 0; i < 30; i++)
        {
            var allNotifications = await notificationRepo.GetAllAsync();
            createdNotification = allNotifications.FirstOrDefault(x => x.CorrelationId == correlationId);
            if (createdNotification != null)
            {
                break;
            }
            await Task.Delay(100);
        }

        // Assert
        createdNotification.Should().NotBeNull();
        createdNotification!.Status.Should().Match(s => s == NotificationStatus.Pending ||
                                                        s == NotificationStatus.Processing ||
                                                        s == NotificationStatus.Delivered ||
                                                        s == NotificationStatus.Failed);
        createdNotification.EventType.Should().Be("ApplicationStarted");
        createdNotification.CorrelationId.Should().Be(correlationId);
        createdNotification.Message.Should().Contain("Trading Bot Started");
        createdNotification.Message.Should().Contain("Status: Running");
    }

    [Fact]
    public async Task AtomicClaiming_ShouldPreventDuplicateProcessing_UnderConcurrency()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Create a pending notification
        var notification = new Notification(
            eventId: Guid.NewGuid(),
            eventType: "ApplicationStarted",
            severity: "INFORMATION",
            channel: "Telegram",
            recipient: "987654321",
            title: "Concurrency Test",
            message: "Test message"
        );

        await repository.AddAsync(notification);
        await unitOfWork.SaveChangesAsync();

        // Simulate two threads trying to claim the same notification concurrently
        var task1 = Task.Run(async () =>
        {
            using var localScope = _factory.Services.CreateScope();
            var localRepo = localScope.ServiceProvider.GetRequiredService<INotificationRepository>();
            var localUow = localScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            try
            {
                var n = await localRepo.GetByIdAsync(notification.Id);
                if (n != null && n.Status == NotificationStatus.Pending)
                {
                    n.MarkProcessing();
                    localRepo.Update(n);
                    await localUow.SaveChangesAsync();
                    return true;
                }
            }
            catch
            {
                // Concurrency exception
            }
            return false;
        });

        var task2 = Task.Run(async () =>
        {
            using var localScope = _factory.Services.CreateScope();
            var localRepo = localScope.ServiceProvider.GetRequiredService<INotificationRepository>();
            var localUow = localScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            try
            {
                var n = await localRepo.GetByIdAsync(notification.Id);
                if (n != null && n.Status == NotificationStatus.Pending)
                {
                    n.MarkProcessing();
                    localRepo.Update(n);
                    await localUow.SaveChangesAsync();
                    return true;
                }
            }
            catch
            {
                // Concurrency exception
            }
            return false;
        });

        // Act
        var results = await Task.WhenAll(task1, task2);

        // Assert: only one worker should successfully claim and save changes
        results.Count(x => x).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task ApplicationRestartRecovery_ShouldNotLosePendingNotifications()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var uniqueId = Guid.NewGuid();
        var notification = new Notification(
            eventId: uniqueId,
            eventType: "ApplicationStarted",
            severity: "INFORMATION",
            channel: "Telegram",
            recipient: "987654321",
            title: "Recovery Test",
            message: "Must survive restart"
        );

        await repository.AddAsync(notification);
        await unitOfWork.SaveChangesAsync();

        // Act - query pending from a fresh scope (simulating recovery retrieval on restart)
        using var newScope = _factory.Services.CreateScope();
        var freshRepo = newScope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var pendingList = await freshRepo.GetPendingAndRetryScheduledAsync();
        var recovered = pendingList.FirstOrDefault(x => x.EventId == uniqueId);

        // Assert: Notification is recovered perfectly
        recovered.Should().NotBeNull();
        recovered!.Status.Should().Be(NotificationStatus.Pending);
        recovered.Message.Should().Be("Must survive restart");
    }
}
