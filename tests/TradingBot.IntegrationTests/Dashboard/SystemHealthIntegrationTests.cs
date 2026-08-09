using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using TradingBot.Application.Dashboard.DTOs;
using TradingBot.Application.Dashboard.Interfaces;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Monitoring.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Queries;
using TradingBot.IntegrationTests.Dashboard; // to reuse MockHealthStatusProvider and MockMetricsService
using Xunit;

namespace TradingBot.IntegrationTests.Dashboard;

public class SystemHealthIntegrationTests : IAsyncLifetime
{
    private SqliteConnection? _sqliteConnection;
    private TradingDbContext? _dbContext;
    private ISystemHealthQueryService? _queryService;
    private MockHealthStatusProvider? _healthStatusProvider;
    private MockMetricsService? _metricsService;
    private Mock<IWorkerHealthRegistry>? _workerHealthRegistryMock;
    private IEventSanitizer? _eventSanitizer;

    public async Task InitializeAsync()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        await _sqliteConnection.OpenAsync();

        using var command = _sqliteConnection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync();

        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _dbContext = new TradingDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _healthStatusProvider = new MockHealthStatusProvider();
        _metricsService = new MockMetricsService();
        _workerHealthRegistryMock = new Mock<IWorkerHealthRegistry>();
        _eventSanitizer = new EventSanitizer();

        _queryService = new SystemHealthQueryService(
            _dbContext,
            _healthStatusProvider,
            _metricsService,
            _workerHealthRegistryMock.Object,
            _eventSanitizer
        );
    }

    public async Task DisposeAsync()
    {
        if (_dbContext != null)
        {
            await _dbContext.DisposeAsync();
        }
        if (_sqliteConnection != null)
        {
            await _sqliteConnection.CloseAsync();
            await _sqliteConnection.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetOverviewAsync_E2E_WithFullDataStore_ShouldSucceedAndSanitize()
    {
        // 1. Arrange - Setup historical Database health checks
        var now = DateTime.UtcNow;
        var checkDb = new HealthCheckResult("Database", HealthStatus.Healthy, now.AddMinutes(-5), 12);
        var checkRest = new HealthCheckResult("Bybit REST", HealthStatus.Healthy, now.AddMinutes(-4), 140, metadata: "{\"ResponseTimeMs\":140,\"Authenticated\":true}");
        var checkWs = new HealthCheckResult("Bybit WebSocket", HealthStatus.Healthy, now.AddMinutes(-3), 5);
        var checkTg = new HealthCheckResult("Telegram", HealthStatus.Healthy, now.AddMinutes(-2), 90);
        var checkWorkers = new HealthCheckResult("Workers", HealthStatus.Healthy, now.AddMinutes(-1), 0, metadata: "{\"SignalStorageWorker\":{\"Status\":\"Running\"}}");

        _dbContext!.HealthCheckResults.AddRange(checkDb, checkRest, checkWs, checkTg, checkWorkers);

        // 2. Arrange - Setup persistent Alerts
        var alert1 = new Alert("R1", "ConnFailed", "ERROR", "Active", "Worker", "CompA", "Error with password=SuperSecretPassword", "D1");
        var alert2 = new Alert("R2", "HighLatency", "WARNING", "Active", "Bybit REST", "CompB", "Latency is 500ms for api_key=MyApiKey", "D2");
        _dbContext.Alerts.AddRange(alert1, alert2);

        // 3. Arrange - Setup persistent MonitoringEvents
        var ev1 = new MonitoringEvent("EvTypeA", "INFORMATION", "SourceA", "CompA", "StatusA", "My normal message", timestamp: now.AddSeconds(-30));
        var ev2 = new MonitoringEvent("EvTypeB", "ERROR", "SourceB", "CompB", "StatusB", "My secret token is secret_key=BotTokenValue", timestamp: now.AddSeconds(-10));
        _dbContext.MonitoringEvents.AddRange(ev1, ev2);

        await _dbContext.SaveChangesAsync();

        // 4. Arrange - Setup worker heartbeats in-memory
        var heartbeats = new Dictionary<string, WorkerHeartbeat>
        {
            ["SignalStorageWorker"] = new WorkerHeartbeat
            {
                WorkerName = "SignalStorageWorker",
                Status = "Running",
                LastHeartbeatAt = now,
                StartedAt = now.AddHours(-1)
            }
        };
        _workerHealthRegistryMock!.Setup(r => r.GetWorkerHeartbeats()).Returns(heartbeats);

        // 5. Arrange - Setup metrics
        _metricsService!.SetUptime(TimeSpan.FromHours(10));

        // Act
        var overview = await _queryService!.GetOverviewAsync(
            recentAlertsLimit: 1, // bound to 1
            recentEventsLimit: 1, // bound to 1
            healthHistoryLimit: 5,
            cancellationToken: CancellationToken.None
        );

        // Assert
        overview.Should().NotBeNull();
        overview.OverallStatus.Should().Be("Healthy");

        // Application status
        overview.Application.Status.Should().Be("Healthy");
        overview.Application.Uptime.Should().Be(TimeSpan.FromHours(10).ToString());

        // Database status from persisted results
        overview.Database.Status.Should().Be("Healthy");
        overview.Database.ResponseTime.Should().Be(12);

        // Bybit status
        overview.Bybit.Rest.Status.Should().Be("Healthy");
        overview.Bybit.WebSocket.Status.Should().Be("Healthy");
        overview.Bybit.AuthenticationStatus.Should().Be("Healthy");

        // Telegram status
        overview.Telegram.Status.Should().Be("Healthy");

        // Worker status
        overview.Workers.Should().HaveCount(1);
        overview.Workers[0].Name.Should().Be("SignalStorageWorker");
        overview.Workers[0].Status.Should().Be("Running");

        // Alerts summary & active alerts (bounded to 1)
        overview.AlertSummary.ActiveAlertCount.Should().Be(2);
        overview.ActiveAlerts.Should().HaveCount(1);
        overview.ActiveAlerts[0].Severity.Should().Be("ERROR"); // Higher severity (ERROR > WARNING)
        overview.ActiveAlerts[0].Message.Should().NotContain("SuperSecretPassword");
        overview.ActiveAlerts[0].Message.Should().Contain("[REDACTED]");

        // Recent events (bounded to 1, newest first)
        overview.RecentEvents.Should().HaveCount(1);
        overview.RecentEvents[0].Type.Should().Be("EvTypeB");
        overview.RecentEvents[0].Message.Should().NotContain("BotTokenValue");
        overview.RecentEvents[0].Message.Should().Contain("[REDACTED]");

        // Health history limit of 5
        overview.HealthHistory.Should().HaveCount(5);
        overview.HealthHistory[0].Service.Should().Be("Workers"); // Newest
    }

    [Fact]
    public async Task GetOverviewAsync_WithNoSensitiveLeakInExceptions_ShouldBeSafe()
    {
        // Act & Assert
        // Verify that the sanitizer is robust
        var sanitizedMsg = _eventSanitizer!.Sanitize("Db connection string: Username=admin;Password=extremelySecretPassword;");
        sanitizedMsg.Should().NotContain("extremelySecretPassword");
        sanitizedMsg.Should().Contain("[REDACTED]");
    }
}
