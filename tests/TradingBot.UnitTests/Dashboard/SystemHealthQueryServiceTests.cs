using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using TradingBot.Application.Dashboard.DTOs;
using TradingBot.Application.Dashboard.Interfaces;
using TradingBot.Application.Monitoring;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Queries;
using Xunit;

namespace TradingBot.UnitTests.Dashboard;

public class SystemHealthQueryServiceTests : IDisposable
{
    private readonly TradingDbContext _dbContext;
    private readonly Mock<IHealthStatusProvider> _healthStatusProviderMock;
    private readonly Mock<IMetricsService> _metricsServiceMock;
    private readonly Mock<IWorkerHealthRegistry> _workerHealthRegistryMock;
    private readonly Mock<IEventSanitizer> _eventSanitizerMock;
    private readonly ISystemHealthQueryService _queryService;

    public SystemHealthQueryServiceTests()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TradingDbContext(options);
        _healthStatusProviderMock = new Mock<IHealthStatusProvider>();
        _metricsServiceMock = new Mock<IMetricsService>();
        _workerHealthRegistryMock = new Mock<IWorkerHealthRegistry>();
        _eventSanitizerMock = new Mock<IEventSanitizer>();

        // Default setup for sanitizer to return the input if not explicitly mocked
        _eventSanitizerMock.Setup(s => s.Sanitize(It.IsAny<string>()))
            .Returns<string>(input => input);

        _queryService = new SystemHealthQueryService(
            _dbContext,
            _healthStatusProviderMock.Object,
            _metricsServiceMock.Object,
            _workerHealthRegistryMock.Object,
            _eventSanitizerMock.Object
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetOverviewAsync_UptimeAndStatus_ShouldMapCorrectly()
    {
        // Arrange
        var expectedUptime = TimeSpan.FromHours(5) + TimeSpan.FromMinutes(30);
        _metricsServiceMock.Setup(m => m.GetUptime()).Returns(expectedUptime);
        _healthStatusProviderMock.Setup(h => h.GetOverallStatus()).Returns(HealthStatus.Healthy);

        _metricsServiceMock.Setup(m => m.GetAggregatedMetrics()).Returns(new Dictionary<string, object>());

        // Act
        var overview = await _queryService.GetOverviewAsync(cancellationToken: CancellationToken.None);

        // Assert
        overview.Application.Status.Should().Be("Healthy");
        overview.Application.Uptime.Should().Be(expectedUptime.ToString());
        overview.Application.StartedAt.Should().BeBefore(DateTime.UtcNow);
        overview.Application.Environment.Should().Be("Development");
    }

    [Theory]
    [InlineData(HealthStatus.Healthy, "Healthy")]
    [InlineData(HealthStatus.Degraded, "Degraded")]
    [InlineData(HealthStatus.Unhealthy, "Unhealthy")]
    [InlineData(HealthStatus.Unknown, "Unknown")]
    public async Task GetOverviewAsync_DatabaseStatus_ShouldMapCorrectly(HealthStatus status, string expectedStatus)
    {
        // Arrange
        var checkResult = new HealthCheckResult("Database", status, DateTime.UtcNow, 45);
        _healthStatusProviderMock.Setup(h => h.GetComponentStatus("Database")).Returns(checkResult);
        _metricsServiceMock.Setup(m => m.GetAggregatedMetrics()).Returns(new Dictionary<string, object>());

        // Act
        var overview = await _queryService.GetOverviewAsync(cancellationToken: CancellationToken.None);

        // Assert
        overview.Database.Status.Should().Be(expectedStatus);
        overview.Database.LastCheck.Should().Be(checkResult.CheckedAt);
        overview.Database.ResponseTime.Should().Be(45);
    }

    [Fact]
    public async Task GetOverviewAsync_BybitRESTAndWS_ShouldMapCorrectly()
    {
        // Arrange
        var restCheck = new HealthCheckResult("Bybit REST", HealthStatus.Healthy, DateTime.UtcNow, 150, metadata: "{\"ResponseTimeMs\":150,\"Authenticated\":true}");
        var wsCheck = new HealthCheckResult("Bybit WebSocket", HealthStatus.Degraded, DateTime.UtcNow, 0);

        _healthStatusProviderMock.Setup(h => h.GetComponentStatus("Bybit REST")).Returns(restCheck);
        _healthStatusProviderMock.Setup(h => h.GetComponentStatus("Bybit WebSocket")).Returns(wsCheck);
        _metricsServiceMock.Setup(m => m.GetAggregatedMetrics()).Returns(new Dictionary<string, object>());

        // Act
        var overview = await _queryService.GetOverviewAsync(cancellationToken: CancellationToken.None);

        // Assert
        overview.Bybit.Rest.Status.Should().Be("Healthy");
        overview.Bybit.Rest.ResponseTime.Should().Be(150);
        overview.Bybit.WebSocket.Status.Should().Be("Degraded");
        overview.Bybit.AuthenticationStatus.Should().Be("Healthy");
    }

    [Fact]
    public async Task GetOverviewAsync_TelegramHealth_ShouldMapCorrectly()
    {
        // Arrange
        var tgCheck = new HealthCheckResult("Telegram", HealthStatus.Healthy, DateTime.UtcNow, 80);
        _healthStatusProviderMock.Setup(h => h.GetComponentStatus("Telegram")).Returns(tgCheck);
        _metricsServiceMock.Setup(m => m.GetAggregatedMetrics()).Returns(new Dictionary<string, object>());

        // Act
        var overview = await _queryService.GetOverviewAsync(cancellationToken: CancellationToken.None);

        // Assert
        overview.Telegram.Status.Should().Be("Healthy");
        overview.Telegram.LastCheck.Should().Be(tgCheck.CheckedAt);
    }

    [Fact]
    public async Task GetOverviewAsync_WorkersStatus_ShouldMapCorrectly()
    {
        // Arrange
        var heartbeats = new Dictionary<string, WorkerHeartbeat>
        {
            ["TelegramWorker"] = new WorkerHeartbeat
            {
                WorkerName = "TelegramWorker",
                Status = "Running",
                LastHeartbeatAt = DateTime.UtcNow.AddMinutes(-1),
                StartedAt = DateTime.UtcNow.AddHours(-2)
            },
            ["NotificationWorker"] = new WorkerHeartbeat
            {
                WorkerName = "NotificationWorker",
                Status = "Failed",
                LastHeartbeatAt = DateTime.UtcNow.AddMinutes(-5),
                LastErrorAt = DateTime.UtcNow.AddMinutes(-5),
                LastErrorMessage = "Network timed out"
            }
        };

        _workerHealthRegistryMock.Setup(r => r.GetWorkerHeartbeats()).Returns(heartbeats);
        _metricsServiceMock.Setup(m => m.GetAggregatedMetrics()).Returns(new Dictionary<string, object>());

        // Act
        var overview = await _queryService.GetOverviewAsync(cancellationToken: CancellationToken.None);

        // Assert
        overview.Workers.Should().HaveCount(2);
        var tgWorker = overview.Workers.First(w => w.Name == "TelegramWorker");
        tgWorker.Status.Should().Be("Running");
        tgWorker.LastActivityAt.Should().Be(heartbeats["TelegramWorker"].LastHeartbeatAt);
        tgWorker.LastSuccessfulExecutionAt.Should().Be(heartbeats["TelegramWorker"].LastHeartbeatAt);
        tgWorker.LastFailureAt.Should().BeNull();

        var notifWorker = overview.Workers.First(w => w.Name == "NotificationWorker");
        notifWorker.Status.Should().Be("Failed");
        notifWorker.LastFailureAt.Should().Be(heartbeats["NotificationWorker"].LastErrorAt);
    }

    [Fact]
    public async Task GetOverviewAsync_AlertSummaryAndOrdering_ShouldSortCorrectly()
    {
        // Arrange
        var alert1 = new Alert("R1", "ConnectionLost", "WARNING", "Active", "Worker", "CompA", "Minor issue", "K1");
        var alert2 = new Alert("R2", "DbCrash", "CRITICAL", "Active", "Database", "CompB", "Critical DB crash", "K2");
        var alert3 = new Alert("R3", "ApiTimeout", "ERROR", "Active", "Exchange", "CompC", "API connection timeout", "K3");

        _dbContext.Alerts.AddRange(alert1, alert2, alert3);
        await _dbContext.SaveChangesAsync();
        _metricsServiceMock.Setup(m => m.GetAggregatedMetrics()).Returns(new Dictionary<string, object>());

        // Act
        var overview = await _queryService.GetOverviewAsync(recentAlertsLimit: 5, cancellationToken: CancellationToken.None);

        // Assert
        overview.AlertSummary.ActiveAlertCount.Should().Be(3);
        overview.AlertSummary.CriticalAlertCount.Should().Be(1);
        overview.AlertSummary.ErrorAlertCount.Should().Be(1);
        overview.AlertSummary.WarningAlertCount.Should().Be(1);

        overview.ActiveAlerts.Should().HaveCount(3);
        // Order must be Critical -> Error -> Warning
        overview.ActiveAlerts[0].Severity.Should().Be("CRITICAL");
        overview.ActiveAlerts[0].Message.Should().Be("Critical DB crash");

        overview.ActiveAlerts[1].Severity.Should().Be("ERROR");
        overview.ActiveAlerts[1].Message.Should().Be("API connection timeout");

        overview.ActiveAlerts[2].Severity.Should().Be("WARNING");
        overview.ActiveAlerts[2].Message.Should().Be("Minor issue");
    }

    [Fact]
    public async Task GetOverviewAsync_RecentEventsBoundedAndOrdered_ShouldBeCorrect()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var ev1 = new MonitoringEvent("TypeA", "INFORMATION", "SourceA", "CompA", "StatusA", "Message A", timestamp: now.AddMinutes(-5));
        var ev2 = new MonitoringEvent("TypeB", "WARNING", "SourceB", "CompB", "StatusB", "Message B", timestamp: now.AddMinutes(-2));
        var ev3 = new MonitoringEvent("TypeC", "ERROR", "SourceC", "CompC", "StatusC", "Message C", timestamp: now);

        _dbContext.MonitoringEvents.AddRange(ev1, ev2, ev3);
        await _dbContext.SaveChangesAsync();
        _metricsServiceMock.Setup(m => m.GetAggregatedMetrics()).Returns(new Dictionary<string, object>());

        // Act
        var overview = await _queryService.GetOverviewAsync(recentEventsLimit: 2, cancellationToken: CancellationToken.None);

        // Assert
        overview.RecentEvents.Should().HaveCount(2); // bounded
        // Ordered by Timestamp descending (ev3 is newest, ev2 is second newest)
        overview.RecentEvents[0].Id.Should().Be(ev3.Id);
        overview.RecentEvents[1].Id.Should().Be(ev2.Id);
    }

    [Fact]
    public async Task GetOverviewAsync_Sanitizer_ShouldRedactSensitivePayloads()
    {
        // Arrange
        var rawMessage = "Connection failed for api_key=superSecretSecret password=secretPassword";
        _eventSanitizerMock.Setup(s => s.Sanitize(rawMessage))
            .Returns("Connection failed for api_key=[REDACTED] password=[REDACTED]");

        var alert = new Alert("R1", "ConnFailed", "ERROR", "Active", "Worker", "CompA", rawMessage, "K1");
        _dbContext.Alerts.Add(alert);

        var ev = new MonitoringEvent("ConnFailed", "ERROR", "Worker", "CompA", "Failed", rawMessage);
        _dbContext.MonitoringEvents.Add(ev);

        await _dbContext.SaveChangesAsync();
        _metricsServiceMock.Setup(m => m.GetAggregatedMetrics()).Returns(new Dictionary<string, object>());

        // Act
        var overview = await _queryService.GetOverviewAsync(cancellationToken: CancellationToken.None);

        // Assert
        overview.ActiveAlerts.First().Message.Should().NotContain("superSecretSecret");
        overview.ActiveAlerts.First().Message.Should().Contain("[REDACTED]");

        overview.RecentEvents.First().Message.Should().NotContain("superSecretSecret");
        overview.RecentEvents.First().Message.Should().Contain("[REDACTED]");
    }

    [Fact]
    public async Task GetOverviewAsync_OverallHealthAggregation_ShouldCombineCorrectly()
    {
        // Setup scenarios where _healthStatusProvider is null, verifying our fallback deterministic rules
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new TradingDbContext(options);
        var serviceWithoutProvider = new SystemHealthQueryService(dbContext, healthStatusProvider: null, metricsService: null, workerHealthRegistry: null, eventSanitizer: null);

        // 1. All Healthy
        var res1 = new HealthCheckResult("Database", HealthStatus.Healthy, DateTime.UtcNow, 10);
        var res2 = new HealthCheckResult("Bybit REST", HealthStatus.Healthy, DateTime.UtcNow, 10);
        var res3 = new HealthCheckResult("Bybit WebSocket", HealthStatus.Healthy, DateTime.UtcNow, 10);
        var res4 = new HealthCheckResult("Telegram", HealthStatus.Healthy, DateTime.UtcNow, 10);
        dbContext.HealthCheckResults.AddRange(res1, res2, res3, res4);
        await dbContext.SaveChangesAsync();

        var overview1 = await serviceWithoutProvider.GetOverviewAsync();
        overview1.OverallStatus.Should().Be("Healthy");

        // 2. One Degraded
        dbContext.HealthCheckResults.RemoveRange(res1, res2, res3, res4);
        await dbContext.SaveChangesAsync();

        var res5 = new HealthCheckResult("Database", HealthStatus.Healthy, DateTime.UtcNow, 10);
        var res6 = new HealthCheckResult("Bybit REST", HealthStatus.Degraded, DateTime.UtcNow, 10);
        var res7 = new HealthCheckResult("Bybit WebSocket", HealthStatus.Healthy, DateTime.UtcNow, 10);
        var res8 = new HealthCheckResult("Telegram", HealthStatus.Healthy, DateTime.UtcNow, 10);
        dbContext.HealthCheckResults.AddRange(res5, res6, res7, res8);
        await dbContext.SaveChangesAsync();

        var overview2 = await serviceWithoutProvider.GetOverviewAsync();
        overview2.OverallStatus.Should().Be("Degraded");

        // 3. One Critical Unhealthy
        dbContext.HealthCheckResults.RemoveRange(res5, res6, res7, res8);
        await dbContext.SaveChangesAsync();

        var res9 = new HealthCheckResult("Database", HealthStatus.Unhealthy, DateTime.UtcNow, 10);
        var res10 = new HealthCheckResult("Bybit REST", HealthStatus.Healthy, DateTime.UtcNow, 10);
        var res11 = new HealthCheckResult("Bybit WebSocket", HealthStatus.Healthy, DateTime.UtcNow, 10);
        var res12 = new HealthCheckResult("Telegram", HealthStatus.Healthy, DateTime.UtcNow, 10);
        dbContext.HealthCheckResults.AddRange(res9, res10, res11, res12);
        await dbContext.SaveChangesAsync();

        var overview3 = await serviceWithoutProvider.GetOverviewAsync();
        overview3.OverallStatus.Should().Be("Unhealthy");
    }
}
