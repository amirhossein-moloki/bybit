using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Monitoring.Configuration;
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
}
