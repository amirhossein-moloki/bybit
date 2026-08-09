using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Monitoring.Services;
using TradingBot.Application.Repositories;
using TradingBot.Infrastructure.Monitoring.Checks;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.UnitTests.Monitoring;

public class MonitoringSystemTests
{
    [Fact]
    public void WorkerHealthRegistry_ShouldRegisterAndRecordHeartbeats()
    {
        // Arrange
        var registry = new WorkerHealthRegistry();

        // Act
        registry.RegisterWorker("Worker1", isCritical: true);
        registry.RecordHeartbeat("Worker1", "Running");

        // Assert
        var heartbeats = registry.GetWorkerHeartbeats();
        heartbeats.Should().ContainKey("Worker1");
        heartbeats["Worker1"].Status.Should().Be("Running");
        heartbeats["Worker1"].IsCritical.Should().BeTrue();
    }

    [Fact]
    public void HealthStatusProvider_ShouldAggregateCorrectly_AllHealthy()
    {
        // Arrange
        var provider = new HealthStatusProvider();
        provider.UpdateStatus("Database", new HealthCheckResult("Database", HealthStatus.Healthy, DateTime.UtcNow, 10));
        provider.UpdateStatus("Bybit REST", new HealthCheckResult("Bybit REST", HealthStatus.Healthy, DateTime.UtcNow, 15));

        // Act & Assert
        provider.GetOverallStatus().Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void HealthStatusProvider_ShouldAggregateCorrectly_CriticalUnhealthy()
    {
        // Arrange
        var provider = new HealthStatusProvider();
        provider.UpdateStatus("Database", new HealthCheckResult("Database", HealthStatus.Unhealthy, DateTime.UtcNow, 10));
        provider.UpdateStatus("Telegram", new HealthCheckResult("Telegram", HealthStatus.Healthy, DateTime.UtcNow, 15));

        // Act & Assert
        provider.GetOverallStatus().Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public void HealthStatusProvider_ShouldAggregateCorrectly_NonCriticalUnhealthy()
    {
        // Arrange
        var provider = new HealthStatusProvider();
        provider.UpdateStatus("Database", new HealthCheckResult("Database", HealthStatus.Healthy, DateTime.UtcNow, 10));
        provider.UpdateStatus("Telegram", new HealthCheckResult("Telegram", HealthStatus.Unhealthy, DateTime.UtcNow, 15));

        // Act & Assert
        provider.GetOverallStatus().Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task WorkerHealthCheck_ShouldReturnUnhealthy_WhenCriticalWorkerIsStale()
    {
        // Arrange
        var registry = new WorkerHealthRegistry();
        registry.RegisterWorker("CriticalWorker", isCritical: true);

        // Simulate stale heartbeat by forcing LastHeartbeatAt into the past
        var hbs = registry.GetWorkerHeartbeats();
        hbs["CriticalWorker"].LastHeartbeatAt = DateTime.UtcNow.AddMinutes(-10);

        var options = new MonitoringOptions();
        options.Workers.StaleThresholdSeconds = 30;

        var check = new WorkerHealthCheck(registry, options);

        // Act
        var result = await check.CheckAsync(CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.ErrorMessage.Should().Contain("heartbeat");
    }

    [Fact]
    public async Task WorkerHealthCheck_ShouldReturnDegraded_WhenNonCriticalWorkerIsStale()
    {
        // Arrange
        var registry = new WorkerHealthRegistry();
        registry.RegisterWorker("NonCriticalWorker", isCritical: false);

        // Simulate stale heartbeat
        var hbs = registry.GetWorkerHeartbeats();
        hbs["NonCriticalWorker"].LastHeartbeatAt = DateTime.UtcNow.AddMinutes(-10);

        var options = new MonitoringOptions();
        options.Workers.StaleThresholdSeconds = 30;

        var check = new WorkerHealthCheck(registry, options);

        // Act
        var result = await check.CheckAsync(CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task WorkerHealthCheck_ShouldReturnUnhealthy_WhenWorkerIsFailed()
    {
        // Arrange
        var registry = new WorkerHealthRegistry();
        registry.RegisterWorker("CriticalWorker", isCritical: true);
        registry.RecordHeartbeat("CriticalWorker", "Failed", "Some unhandled error");

        var options = new MonitoringOptions();
        var check = new WorkerHealthCheck(registry, options);

        // Act
        var result = await check.CheckAsync(CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.ErrorMessage.Should().Contain("failed");
    }

    [Fact]
    public async Task HealthCheckEngine_ShouldIsolateExceptionsAndPreventOverlap()
    {
        // Arrange
        var mockCheck = new Mock<IHealthCheck>();
        mockCheck.Setup(c => c.Name).Returns("MockCheck");
        mockCheck.Setup(c => c.CheckAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Network timeout"));

        var logger = new Mock<ILogger<HealthCheckEngine>>();
        var engine = new HealthCheckEngine(new[] { mockCheck.Object }, new MonitoringOptions(), logger.Object);

        // Act
        var results = await engine.RunAllChecksAsync(CancellationToken.None);

        // Assert
        results.Should().HaveCount(1);
        var res = results.First();
        res.ServiceName.Should().Be("MockCheck");
        res.Status.Should().Be(HealthStatus.Unhealthy);
        res.ErrorMessage.Should().Contain("Network timeout");
    }

    [Fact]
    public void Options_ShouldThrowException_WhenConfigIsInvalid()
    {
        // 1. MonitoringOptions invalid
        var monitoringOptions = new MonitoringOptions();
        monitoringOptions.Database.IntervalSeconds = -5;
        Assert.Throws<ArgumentException>(() => monitoringOptions.Validate());

        monitoringOptions.Database.IntervalSeconds = 30;
        monitoringOptions.Database.TimeoutSeconds = 0;
        Assert.Throws<ArgumentException>(() => monitoringOptions.Validate());

        // 2. NotificationOptions invalid
        var notificationOptions = new NotificationOptions();
        notificationOptions.Telegram.Enabled = true;
        notificationOptions.Telegram.ChatId = "";
        Assert.Throws<ArgumentException>(() => notificationOptions.Validate());

        notificationOptions.Telegram.ChatId = "123456";
        notificationOptions.Telegram.RetryCount = -1;
        Assert.Throws<ArgumentException>(() => notificationOptions.Validate());

        // 3. AlertOptions invalid
        var alertOptions = new AlertOptions();
        alertOptions.Deduplication.WindowSeconds = -10;
        Assert.Throws<ArgumentException>(() => alertOptions.Validate());

        alertOptions.Deduplication.WindowSeconds = 60;
        alertOptions.Rules["TestRule"] = new AlertRuleSettings
        {
            Enabled = true,
            Threshold = "invalid-format"
        };
        Assert.Throws<ArgumentException>(() => alertOptions.Validate());
    }

    [Fact]
    public async Task AlertEngine_ShouldRecoverStartupState_WhenDictionariesAreCleared()
    {
        // Arrange
        var alertRepoMock = new Mock<IAlertRepository>();
        var alertEventRepoMock = new Mock<IAlertEventRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var notificationEngineMock = new Mock<INotificationEngine>();
        var metricsServiceMock = new Mock<IMetricsService>();
        var loggerMock = new Mock<ILogger<AlertEngine>>();

        var services = new ServiceCollection();
        services.AddSingleton(alertRepoMock.Object);
        services.AddSingleton(alertEventRepoMock.Object);
        services.AddSingleton(unitOfWorkMock.Object);
        services.AddSingleton(notificationEngineMock.Object);
        services.AddSingleton(metricsServiceMock.Object);

        var serviceProvider = services.BuildServiceProvider();

        var options = new AlertOptions
        {
            Enabled = true,
            Rules = new Dictionary<string, AlertRuleSettings>
            {
                ["BybitDisconnected"] = new()
                {
                    Enabled = true,
                    Severity = "WARNING",
                    Cooldown = "5m"
                }
            }
        };

        var engine = new AlertEngine(serviceProvider, options, loggerMock.Object);

        var @event = new MonitoringEvent(
            eventType: "BybitDisconnected",
            severity: "WARNING",
            source: "Exchange",
            component: "Bybit",
            status: "Disconnected",
            message: "Bybit connection lost."
        );

        // Alert is active, but static LastNotificationTimes is empty (representing a restart)
        var activeAlert = new Alert(
            ruleId: "BybitDisconnected",
            alertType: "BybitDisconnected",
            severity: "WARNING",
            status: "Triggered",
            source: "Exchange",
            component: "Bybit",
            message: "Bybit connection lost.",
            deduplicationKey: "BybitDisconnected:Bybit:BybitDisconnected"
        );
        activeAlert.IncrementNotificationCount(); // NotificationCount = 1

        alertRepoMock.Setup(x => x.GetActiveByDeduplicationKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeAlert);

        // Act
        await engine.ProcessEventAsync(@event);

        // Assert: It should see activeAlert.NotificationCount > 0 and fallback to UpdatedAt/TriggeredAt,
        // which matches the cooldown (< 5 minutes), so notification should be suppressed!
        metricsServiceMock.Verify(x => x.IncrementNotificationsSuppressed(), Times.Once);
        notificationEngineMock.Verify(x => x.ProcessEventAsync(It.IsAny<MonitoringEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AlertEngine_ShouldHandleConcurrentCreationDbUpdateException_ByFallingBackToExistingAlert()
    {
        // Arrange
        var alertRepoMock = new Mock<IAlertRepository>();
        var alertEventRepoMock = new Mock<IAlertEventRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var notificationEngineMock = new Mock<INotificationEngine>();
        var metricsServiceMock = new Mock<IMetricsService>();
        var loggerMock = new Mock<ILogger<AlertEngine>>();

        var services = new ServiceCollection();
        services.AddSingleton(alertRepoMock.Object);
        services.AddSingleton(alertEventRepoMock.Object);
        services.AddSingleton(unitOfWorkMock.Object);
        services.AddSingleton(notificationEngineMock.Object);
        services.AddSingleton(metricsServiceMock.Object);

        var serviceProvider = services.BuildServiceProvider();

        var options = new AlertOptions
        {
            Enabled = true,
            Rules = new Dictionary<string, AlertRuleSettings>
            {
                ["BybitDisconnected"] = new()
                {
                    Enabled = true,
                    Severity = "WARNING"
                }
            }
        };

        var engine = new AlertEngine(serviceProvider, options, loggerMock.Object);

        var @event = new MonitoringEvent(
            eventType: "BybitDisconnected",
            severity: "WARNING",
            source: "Exchange",
            component: "Bybit",
            status: "Disconnected",
            message: "Bybit connection lost."
        );

        // First call to GetActiveByDeduplicationKeyAsync returns null (thinking we should insert a new alert)
        alertRepoMock.SetupSequence(x => x.GetActiveByDeduplicationKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Alert)null!) // insert path
            .ReturnsAsync(new Alert("BybitDisconnected", "BybitDisconnected", "WARNING", "Triggered", "Exchange", "Bybit", "Bybit connection lost.", "BybitDisconnected:Bybit:BybitDisconnected")); // fallback path

        // Simulate unique key constraint violation exception
        var innerException = new Exception("IX_Alerts_DeduplicationKey unique constraint error");
        var dbUpdateException = new Microsoft.EntityFrameworkCore.DbUpdateException("Duplicate Alert!", innerException);

        unitOfWorkMock.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(dbUpdateException)
            .ReturnsAsync(1);

        // Act
        Func<Task> action = async () => await engine.ProcessEventAsync(@event);

        // Assert: ProcessEventAsync should catch DbUpdateException, fallback to loading the existing alert, and complete without throwing!
        await action.Should().NotThrowAsync();
        alertRepoMock.Verify(x => x.GetActiveByDeduplicationKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AlertEngine_ShouldRetryOnDbUpdateConcurrencyException_DuringDeduplicationUpdate()
    {
        // Arrange
        var alertRepoMock = new Mock<IAlertRepository>();
        var alertEventRepoMock = new Mock<IAlertEventRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var notificationEngineMock = new Mock<INotificationEngine>();
        var metricsServiceMock = new Mock<IMetricsService>();
        var loggerMock = new Mock<ILogger<AlertEngine>>();

        var services = new ServiceCollection();
        services.AddSingleton(alertRepoMock.Object);
        services.AddSingleton(alertEventRepoMock.Object);
        services.AddSingleton(unitOfWorkMock.Object);
        services.AddSingleton(notificationEngineMock.Object);
        services.AddSingleton(metricsServiceMock.Object);

        var serviceProvider = services.BuildServiceProvider();

        var options = new AlertOptions
        {
            Enabled = true,
            Rules = new Dictionary<string, AlertRuleSettings>
            {
                ["BybitDisconnected"] = new()
                {
                    Enabled = true,
                    Severity = "WARNING"
                }
            }
        };

        var engine = new AlertEngine(serviceProvider, options, loggerMock.Object);

        var @event = new MonitoringEvent(
            eventType: "BybitDisconnected",
            severity: "WARNING",
            source: "Exchange",
            component: "Bybit",
            status: "Disconnected",
            message: "Bybit connection lost."
        );

        var activeAlert = new Alert("BybitDisconnected", "BybitDisconnected", "WARNING", "Triggered", "Exchange", "Bybit", "Bybit connection lost.", "BybitDisconnected:Bybit:BybitDisconnected");

        // Mock sequences for alertRepo to return activeAlert
        alertRepoMock.SetupSequence(x => x.GetActiveByDeduplicationKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeAlert)
            .ReturnsAsync(activeAlert);

        // Throw Concurrency exception on first call, succeed on second call
        unitOfWorkMock.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("Concurrency conflict!", new List<Microsoft.EntityFrameworkCore.Update.IUpdateEntry>()))
            .ReturnsAsync(1);

        // Act
        Func<Task> action = async () => await engine.ProcessEventAsync(@event);

        // Assert: Retries successfully, saves changes twice, reloads active alert
        await action.Should().NotThrowAsync();
        unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        alertRepoMock.Verify(x => x.GetActiveByDeduplicationKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
