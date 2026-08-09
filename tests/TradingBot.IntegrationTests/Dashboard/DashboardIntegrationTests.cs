using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Dashboard.DTOs;
using TradingBot.Application.Dashboard.Interfaces;
using TradingBot.Application.Monitoring;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Queries;
using Xunit;

namespace TradingBot.IntegrationTests.Dashboard;

public class DashboardIntegrationTests : IAsyncLifetime
{
    private SqliteConnection? _sqliteConnection;
    private TradingDbContext? _dbContext;
    private IDashboardQueryService? _queryService;
    private MockHealthStatusProvider? _healthStatusProvider;
    private MockMetricsService? _metricsService;

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

        _queryService = new DashboardQueryService(
            _dbContext,
            _healthStatusProvider,
            _metricsService
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
    public async Task DashboardOverview_ShouldReturnExpectedDtoData_WithPopulatedDb()
    {
        // 1. Setup Health Check Statuses
        _healthStatusProvider!.SetOverallStatus(HealthStatus.Healthy);
        _healthStatusProvider.SetComponentStatus("Database", new HealthCheckResult("Database", HealthStatus.Healthy, DateTime.UtcNow, 5));
        _healthStatusProvider.SetComponentStatus("Telegram", new HealthCheckResult("Telegram", HealthStatus.Healthy, DateTime.UtcNow, 85, metadata: "{\"ConnectionStatus\":\"Connected\",\"RawState\":\"Listening\"}"));
        _healthStatusProvider.SetComponentStatus("Bybit REST", new HealthCheckResult("Bybit REST", HealthStatus.Healthy, DateTime.UtcNow, 150));
        _healthStatusProvider.SetComponentStatus("Bybit WebSocket", new HealthCheckResult("Bybit WebSocket", HealthStatus.Healthy, DateTime.UtcNow, 10, metadata: "{\"ConnectionStatus\":\"Connected\",\"RawState\":\"Connected\"}"));

        // 2. Setup Uptime
        _metricsService!.SetUptime(TimeSpan.FromDays(1) + TimeSpan.FromHours(5));

        // 3. Populate 3 standalone Orders (1 open, 1 cancelled, 1 failed)
        var orderOpen = new Order("INT-CL-OPEN", new TradingBot.Domain.ValueObjects.Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(40000m));
        orderOpen.UpdateStatus(OrderStatus.Pending);

        var orderCancelled = new Order("INT-CL-CANCEL", new TradingBot.Domain.ValueObjects.Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(40000m));
        orderCancelled.UpdateStatus(OrderStatus.Pending);
        orderCancelled.UpdateStatus(OrderStatus.Submitting);
        orderCancelled.UpdateStatus(OrderStatus.Submitted);
        orderCancelled.UpdateStatus(OrderStatus.Cancelled);

        var orderFailed = new Order("INT-CL-FAIL", new TradingBot.Domain.ValueObjects.Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(40000m));
        orderFailed.UpdateStatus(OrderStatus.Pending);
        orderFailed.UpdateStatus(OrderStatus.Failed);

        _dbContext!.Orders.AddRange(orderOpen, orderCancelled, orderFailed);
        await _dbContext.SaveChangesAsync();

        // 4. Populate 2 separate Filled Orders to go with our 2 Open Positions
        var orderForPos1 = new Order("INT-CL-POS1", new TradingBot.Domain.ValueObjects.Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(40000m));
        orderForPos1.UpdateStatus(OrderStatus.Pending);
        orderForPos1.UpdateStatus(OrderStatus.Submitting);
        orderForPos1.UpdateStatus(OrderStatus.Submitted);
        orderForPos1.UpdateStatus(OrderStatus.Filled);

        var orderForPos2 = new Order("INT-CL-POS2", new TradingBot.Domain.ValueObjects.Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(40000m));
        orderForPos2.UpdateStatus(OrderStatus.Pending);
        orderForPos2.UpdateStatus(OrderStatus.Submitting);
        orderForPos2.UpdateStatus(OrderStatus.Submitted);
        orderForPos2.UpdateStatus(OrderStatus.Filled);

        _dbContext.Orders.AddRange(orderForPos1, orderForPos2);
        await _dbContext.SaveChangesAsync();

        // 5. Populate 2 Open Positions (both open: 1 long, 1 short)
        var pos1 = new Position(orderForPos1.Id, "BTCUSDT", OrderSide.Buy, 40000m, 0.5m, margin: 200m, initialStatus: PositionStatus.Open);
        pos1.UpdatePrice(41000m); // UnrealizedPnL = (41000 - 40000) * 0.5 = 500m

        var pos2 = new Position(orderForPos2.Id, "ETHUSDT", OrderSide.Sell, 2000m, 5m, margin: 100m, initialStatus: PositionStatus.PartiallyClosed);
        pos2.UpdatePrice(2010m); // UnrealizedPnL = (2000 - 2010) * 5 = -50m

        _dbContext.Positions.AddRange(pos1, pos2);
        await _dbContext.SaveChangesAsync();

        // 6. Populate 10 distinct Orders, Positions, and Trades to satisfy 1-to-1 UNIQUE and FOREIGN KEY constraints
        // 6 winning trades
        for (int i = 1; i <= 6; i++)
        {
            var ord = new Order($"INT-WIN-ORD-{i}", new TradingBot.Domain.ValueObjects.Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(40000m));
            ord.UpdateStatus(OrderStatus.Pending);
            ord.UpdateStatus(OrderStatus.Submitting);
            ord.UpdateStatus(OrderStatus.Submitted);
            ord.UpdateStatus(OrderStatus.Filled);
            _dbContext.Orders.Add(ord);
            await _dbContext.SaveChangesAsync();

            var pos = new Position(ord.Id, "BTCUSDT", OrderSide.Buy, 40000m, 0.5m, margin: 50m, initialStatus: PositionStatus.Closed);
            _dbContext.Positions.Add(pos);
            await _dbContext.SaveChangesAsync();

            var winTrade = new Trade(pos.Id, 40000m, 40500m, 0.1m, 50m, 2m, DateTime.UtcNow);
            _dbContext.Trades.Add(winTrade);
            await _dbContext.SaveChangesAsync();
        }

        // 4 losing trades
        for (int i = 1; i <= 4; i++)
        {
            var ord = new Order($"INT-LOSS-ORD-{i}", new TradingBot.Domain.ValueObjects.Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(40000m));
            ord.UpdateStatus(OrderStatus.Pending);
            ord.UpdateStatus(OrderStatus.Submitting);
            ord.UpdateStatus(OrderStatus.Submitted);
            ord.UpdateStatus(OrderStatus.Filled);
            _dbContext.Orders.Add(ord);
            await _dbContext.SaveChangesAsync();

            var pos = new Position(ord.Id, "BTCUSDT", OrderSide.Buy, 40000m, 0.5m, margin: 50m, initialStatus: PositionStatus.Closed);
            _dbContext.Positions.Add(pos);
            await _dbContext.SaveChangesAsync();

            var lossTrade = new Trade(pos.Id, 40000m, 39800m, 0.1m, -20m, 2m, DateTime.UtcNow);
            _dbContext.Trades.Add(lossTrade);
            await _dbContext.SaveChangesAsync();
        }

        // Act
        var overview = await _queryService!.GetOverviewAsync(CancellationToken.None);

        // Assert
        overview.Should().NotBeNull();

        // System
        overview.System.ApplicationStatus.Should().Be("Healthy");
        overview.System.Uptime.Should().Be("1.05:00:00");

        // Database
        overview.Database.DatabaseStatus.Should().Be("Healthy");

        // Exchange
        overview.Exchange.ExchangeStatus.Should().Be("Healthy");
        overview.Exchange.ConnectionStatus.Should().Be("Connected");

        // Telegram
        overview.Telegram.TelegramStatus.Should().Be("Healthy");
        overview.Telegram.ConnectionStatus.Should().Be("Connected");

        // Orders count:
        // Standalone: 1 open, 1 cancelled, 1 failed (3)
        // Linked to open positions: 2 filled (2)
        // Linked to winning trades: 6 filled (6)
        // Linked to losing trades: 4 filled (4)
        // Total = 15 orders.
        // Open = 1, Filled = 12, Cancelled = 1, Failed = 1
        overview.Orders.TotalOrders.Should().Be(15);
        overview.Orders.OpenOrders.Should().Be(1);
        overview.Orders.FilledOrders.Should().Be(12);
        overview.Orders.CancelledOrders.Should().Be(1);
        overview.Orders.FailedOrders.Should().Be(1);

        // Positions:
        // 2 open, 10 closed.
        // OpenCount = 2, Long = 1, Short = 1
        overview.Positions.OpenPositionCount.Should().Be(2);
        overview.Positions.LongPositionCount.Should().Be(1);
        overview.Positions.ShortPositionCount.Should().Be(1);

        // Trades: 10 total, 6 winning, 4 losing
        overview.Trades.TotalTrades.Should().Be(10);
        overview.Trades.WinningTrades.Should().Be(6);
        overview.Trades.LosingTrades.Should().Be(4);

        // PnL & Fees:
        // Gross Win: 6 * 50 = 300
        // Gross Loss: 4 * -20 = -80
        // Total Gross PnL (RealizedPnL) = 220
        // Total Fees = 10 * 2 = 20
        // Net PnL = 220 - 20 = 200
        overview.Pnl.RealizedPnL.Should().Be(220m);
        overview.Pnl.TotalFees.Should().Be(20m);
        overview.Pnl.NetPnL.Should().Be(200m);

        // Account / Open positions summary:
        // Total open margin = 200 (LONG) + 100 (SHORT) = 300
        // Total open unrealized PnL = 500 (LONG) + (-50) (SHORT) = 450
        overview.Account.UsedMargin.Should().Be(300m);
        overview.Account.UnrealizedPnL.Should().Be(450m);
        overview.Account.Equity.Should().BeNull();
        overview.Account.Balance.Should().BeNull();
    }

    [Fact]
    public async Task DashboardOverview_ShouldReturnEmptyData_WhenDatabaseIsNewlyInitialized()
    {
        // Act
        var overview = await _queryService!.GetOverviewAsync(CancellationToken.None);

        // Assert
        overview.Should().NotBeNull();
        overview.Orders.TotalOrders.Should().Be(0);
        overview.Positions.OpenPositionCount.Should().Be(0);
        overview.Trades.TotalTrades.Should().Be(0);
        overview.Pnl.RealizedPnL.Should().Be(0m);
    }
}

// Concrete Mocks to avoid Moq dependency overhead in integration contexts
public class MockHealthStatusProvider : IHealthStatusProvider
{
    private HealthStatus _overall = HealthStatus.Unknown;
    private readonly Dictionary<string, HealthCheckResult> _statuses = new(StringComparer.OrdinalIgnoreCase);

    public void SetOverallStatus(HealthStatus overall) => _overall = overall;

    public void SetComponentStatus(string name, HealthCheckResult result) => _statuses[name] = result;

    public HealthStatus GetOverallStatus() => _overall;

    public IReadOnlyDictionary<string, HealthCheckResult> GetComponentStatuses() => _statuses;

    public HealthCheckResult? GetComponentStatus(string componentName)
    {
        return _statuses.TryGetValue(componentName, out var res) ? res : null;
    }

    public void UpdateStatus(string componentName, HealthCheckResult result) => _statuses[componentName] = result;
}

public class MockMetricsService : IMetricsService
{
    private TimeSpan _uptime = TimeSpan.Zero;

    public void SetUptime(TimeSpan uptime) => _uptime = uptime;

    public TimeSpan GetUptime() => _uptime;

    public void IncrementAlertsTriggered() {}
    public void IncrementAlertsResolved() {}
    public void IncrementAlertsDeduplicated() {}
    public void IncrementNotificationsSuppressed() {}
    public void IncrementNotificationsCreated() {}
    public void IncrementNotificationsDelivered() {}
    public void IncrementNotificationsFailed() {}
    public void IncrementNotificationsRetried() {}
    public void IncrementSystemErrors() {}
    public void IncrementSystemWarnings() {}
    public void IncrementSystemCriticalErrors() {}
    public void IncrementSignalsReceived() {}
    public void IncrementSignalsAccepted() {}
    public void IncrementSignalsRejected() {}
    public void IncrementOrdersSubmitted() {}
    public void IncrementOrdersFilled() {}
    public void IncrementOrdersFailed() {}
    public void IncrementOrdersRejected() {}
    public void IncrementOrdersCancelled() {}
    public void IncrementPositionsOpened() {}
    public void IncrementPositionsClosed() {}
    public void IncrementTelegramMessagesReceived() {}
    public void IncrementTelegramMessagesProcessed() {}
    public void IncrementTelegramMessagesFailed() {}
    public void RecordConnectionAttempt(string serviceName) {}
    public void RecordConnectionSuccess(string serviceName) {}
    public void RecordConnectionFailure(string serviceName) {}
    public void RecordConnectionStatus(string serviceName, string status) {}
    public void RecordWorkerStart(string workerName) {}
    public void RecordWorkerFailure(string workerName, string error) {}
    public void RecordWorkerRestart(string workerName) {}
    public void RecordWorkerHeartbeat(string workerName, string state) {}
    public void RecordApiCall(string apiName, double latencyMs, bool success, bool isTimeout, bool isRateLimit) {}
    public void RecordLatency(string pathName, double latencyMs) {}
    public Dictionary<string, object> GetAggregatedMetrics() => new();
}
