using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using Xunit;

namespace TradingBot.UnitTests.Persistence;

public class NotificationRepositoryTests : IDisposable
{
    private readonly TradingDbContext _dbContext;
    private readonly NotificationRepository _repository;

    public NotificationRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TradingDbContext(options);
        _repository = new NotificationRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task ClaimPendingNotificationsAsync_ShouldClaimPendingNotifications_AndSetProcessingStatus()
    {
        // Arrange
        var notification1 = new Notification(
            eventId: Guid.NewGuid(),
            eventType: "TestEvent",
            severity: "INFO",
            channel: "Telegram",
            recipient: "123456",
            title: "Test Claim 1",
            message: "Message 1"
        );

        var notification2 = new Notification(
            eventId: Guid.NewGuid(),
            eventType: "TestEvent",
            severity: "INFO",
            channel: "Telegram",
            recipient: "123456",
            title: "Test Claim 2",
            message: "Message 2"
        );

        await _repository.AddAsync(notification1);
        await _repository.AddAsync(notification2);
        await _dbContext.SaveChangesAsync();

        // Act
        var claimed = await _repository.ClaimPendingNotificationsAsync(batchSize: 10, workerId: "WorkerTest", CancellationToken.None);

        // Assert
        claimed.Should().HaveCount(2);
        claimed.Should().AllSatisfy(n => n.Status.Should().Be(NotificationStatus.Processing));

        var fromDb = await _repository.GetByIdAsync(notification1.Id);
        fromDb.Should().NotBeNull();
        fromDb!.Status.Should().Be(NotificationStatus.Processing);
    }

    [Fact]
    public async Task ClaimPendingNotificationsAsync_WhenNoPendingNotifications_ShouldReturnEmptyList()
    {
        // Act
        var claimed = await _repository.ClaimPendingNotificationsAsync(batchSize: 10, workerId: "WorkerTest", CancellationToken.None);

        // Assert
        claimed.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPendingAndRetryScheduledAsync_ShouldReturnEligibleNotifications()
    {
        // Arrange
        var pendingNotif = new Notification(Guid.NewGuid(), "Event", "INFO", "Telegram", "123", "Title", "Msg");

        var deliveredNotif = new Notification(Guid.NewGuid(), "Event", "INFO", "Telegram", "123", "Title", "Msg");
        deliveredNotif.MarkProcessing();
        deliveredNotif.MarkDelivered();

        await _repository.AddAsync(pendingNotif);
        await _repository.AddAsync(deliveredNotif);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _repository.GetPendingAndRetryScheduledAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result.First().Id.Should().Be(pendingNotif.Id);
    }

    [Fact]
    public async Task ExistsForEventAsync_ShouldReturnTrue_WhenNotificationExistsForEvent()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var notif = new Notification(eventId, "Event", "INFO", "Telegram", "123", "Title", "Msg");

        await _repository.AddAsync(notif);
        await _dbContext.SaveChangesAsync();

        // Act
        var exists = await _repository.ExistsForEventAsync(eventId, "Telegram", "123", CancellationToken.None);
        var notExists = await _repository.ExistsForEventAsync(eventId, "Email", "123", CancellationToken.None);

        // Assert
        exists.Should().BeTrue();
        notExists.Should().BeFalse();
    }
}
