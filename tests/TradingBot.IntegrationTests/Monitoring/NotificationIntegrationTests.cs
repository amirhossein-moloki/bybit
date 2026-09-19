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
        var notificationEngine = scope.ServiceProvider.GetRequiredService<INotificationEngine>();
        await notificationEngine.ProcessEventAsync(@event);

        // Query created notification
        using var pollScope = _factory.Services.CreateScope();
        var pollRepo = pollScope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var allNotifications = await pollRepo.GetAllAsync();
        var createdNotification = allNotifications.FirstOrDefault(x => x.CorrelationId == correlationId);

        // Assert
        createdNotification.Should().NotBeNull();
        createdNotification!.Status.Should().Match(s => s == NotificationStatus.Pending ||
                                                        s == NotificationStatus.Processing ||
                                                        s == NotificationStatus.Delivered ||
                                                        s == NotificationStatus.Failed);
        createdNotification.EventType.Should().NotBeNullOrEmpty();
        createdNotification.CorrelationId.Should().Be(correlationId);
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
            channel: "TestChannelIsolated",
            recipient: "987654321",
            title: "Concurrency Claiming Test",
            message: "Test atomic claiming"
        );

        await repository.AddAsync(notification);
        await unitOfWork.SaveChangesAsync();

        // Simulate Worker A and Worker B trying to claim the same notification concurrently
        var task1 = Task.Run(async () =>
        {
            using var localScope = _factory.Services.CreateScope();
            var localRepo = localScope.ServiceProvider.GetRequiredService<INotificationRepository>();
            return await localRepo.ClaimPendingNotificationsAsync(10, "Worker-A", CancellationToken.None);
        });

        var task2 = Task.Run(async () =>
        {
            using var localScope = _factory.Services.CreateScope();
            var localRepo = localScope.ServiceProvider.GetRequiredService<INotificationRepository>();
            return await localRepo.ClaimPendingNotificationsAsync(10, "Worker-B", CancellationToken.None);
        });

        // Act
        var results = await Task.WhenAll(task1, task2);
        var workerAClaimed = results[0].Any(x => x.Id == notification.Id);
        var workerBClaimed = results[1].Any(x => x.Id == notification.Id);

        // Assert: Exactly one worker instance should claim notification #1
        (workerAClaimed ^ workerBClaimed).Should().BeTrue("Only one worker instance can claim notification #1");
    }

    [Fact]
    public async Task DuplicateDeliveryProtection_ShouldAllowOnlyOneFinalDeliveredState()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var notification = new Notification(
            eventId: Guid.NewGuid(),
            eventType: "ApplicationStarted",
            severity: "INFORMATION",
            channel: "TestChannelIsolated",
            recipient: "987654321",
            title: "Duplicate Delivery Test",
            message: "Test idempotent state transition"
        );

        // Store notification directly in Processing state to prevent background worker claiming
        notification.MarkProcessing();

        await repository.AddAsync(notification);
        await unitOfWork.SaveChangesAsync();

        // Simulate two concurrent delivery result updates for the same notification
        var task1 = Task.Run(async () =>
        {
            using var localScope = _factory.Services.CreateScope();
            var localRepo = localScope.ServiceProvider.GetRequiredService<INotificationRepository>();
            return await localRepo.TryUpdateDeliveryResultAsync(
                notification.Id,
                NotificationDeliveryResult.AsSuccess(),
                initialRetryDelaySeconds: 2,
                maxRetryDelaySeconds: 60,
                cancellationToken: CancellationToken.None);
        });

        var task2 = Task.Run(async () =>
        {
            using var localScope = _factory.Services.CreateScope();
            var localRepo = localScope.ServiceProvider.GetRequiredService<INotificationRepository>();
            return await localRepo.TryUpdateDeliveryResultAsync(
                notification.Id,
                NotificationDeliveryResult.AsSuccess(),
                initialRetryDelaySeconds: 2,
                maxRetryDelaySeconds: 60,
                cancellationToken: CancellationToken.None);
        });

        // Act
        var results = await Task.WhenAll(task1, task2);

        // Assert: Only one update should succeed (return true), the other is skipped safely
        results.Count(res => res).Should().Be(1, "Only one atomic update should affect rows");

        // Verify database entity is in Delivered state
        using var verifyScope = _factory.Services.CreateScope();
        var verifyRepo = verifyScope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var finalNotification = await verifyRepo.GetByIdAsync(notification.Id);

        finalNotification.Should().NotBeNull();
        finalNotification!.Status.Should().Be(NotificationStatus.Delivered);
    }

    [Fact]
    public async Task TelegramSuccess_DatabaseRace_ShouldMaintainConsistentStateWithoutExceptions()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var notification = new Notification(
            eventId: Guid.NewGuid(),
            eventType: "ApplicationStarted",
            severity: "INFORMATION",
            channel: "TestChannelIsolated",
            recipient: "987654321",
            title: "Telegram Race Test",
            message: "Test DB race after Telegram send"
        );

        // Store notification directly in Processing state
        notification.MarkProcessing();

        await repository.AddAsync(notification);
        await unitOfWork.SaveChangesAsync();

        // 1. Telegram send succeeds
        var deliveryResult = NotificationDeliveryResult.AsSuccess();

        // 2. Database update 1 succeeds
        var firstUpdateSuccess = await repository.TryUpdateDeliveryResultAsync(
            notification.Id,
            deliveryResult,
            2,
            60,
            CancellationToken.None);

        firstUpdateSuccess.Should().BeTrue();

        // 3. Database update 2 (racing update / retry attempt) runs on already processed notification
        var secondUpdateResult = await repository.TryUpdateDeliveryResultAsync(
            notification.Id,
            deliveryResult,
            2,
            60,
            CancellationToken.None);

        // Assert: Second update returns false without throwing DbUpdateConcurrencyException
        secondUpdateResult.Should().BeFalse("Attempting to update an already Delivered notification must return false cleanly without exception");

        // Ensure state remains Delivered
        using var checkScope = _factory.Services.CreateScope();
        var checkRepo = checkScope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var checkNotif = await checkRepo.GetByIdAsync(notification.Id);
        checkNotif!.Status.Should().Be(NotificationStatus.Delivered);
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
