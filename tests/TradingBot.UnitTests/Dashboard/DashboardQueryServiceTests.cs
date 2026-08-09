using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using TradingBot.Application.Dashboard.Interfaces;
using TradingBot.Application.Exceptions;
using TradingBot.Application.Monitoring;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Queries;
using Xunit;

// Avoid ambiguity by aliasing ValueObject Symbol
using SymbolValueObject = TradingBot.Domain.ValueObjects.Symbol;

namespace TradingBot.UnitTests.Dashboard;

public class DashboardQueryServiceTests : IDisposable
{
    private readonly TradingDbContext _dbContext;
    private readonly Mock<IHealthStatusProvider> _healthStatusProviderMock;
    private readonly Mock<IMetricsService> _metricsServiceMock;
    private readonly IDashboardQueryService _queryService;

    public DashboardQueryServiceTests()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TradingDbContext(options);
        _healthStatusProviderMock = new Mock<IHealthStatusProvider>();
        _metricsServiceMock = new Mock<IMetricsService>();

        _queryService = new DashboardQueryService(
            _dbContext,
            _healthStatusProviderMock.Object,
            _metricsServiceMock.Object
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetOverviewAsync_WithEmptyDatabase_ShouldReturnZerosAndNulls()
    {
        // Arrange
        _metricsServiceMock.Setup(m => m.GetUptime()).Returns(TimeSpan.FromHours(2));
        _healthStatusProviderMock.Setup(h => h.GetOverallStatus()).Returns(HealthStatus.Healthy);

        // Act
        var overview = await _queryService.GetOverviewAsync(CancellationToken.None);

        // Assert
        overview.Should().NotBeNull();
        overview.System.ApplicationStatus.Should().Be("Healthy");
        overview.System.Uptime.Should().Be("02:00:00");

        // Orders empty state
        overview.Orders.TotalOrders.Should().Be(0);
        overview.Orders.OpenOrders.Should().Be(0);
        overview.Orders.FilledOrders.Should().Be(0);
        overview.Orders.CancelledOrders.Should().Be(0);
        overview.Orders.FailedOrders.Should().Be(0);

        // Positions empty state
        overview.Positions.OpenPositionCount.Should().Be(0);
        overview.Positions.LongPositionCount.Should().Be(0);
        overview.Positions.ShortPositionCount.Should().Be(0);

        // Trades empty state
        overview.Trades.TotalTrades.Should().Be(0);
        overview.Trades.WinningTrades.Should().Be(0);
        overview.Trades.LosingTrades.Should().Be(0);

        // PnL empty state
        overview.Pnl.RealizedPnL.Should().Be(0m);
        overview.Pnl.TotalFees.Should().Be(0m);
        overview.Pnl.NetPnL.Should().Be(0m);

        // Account empty state
        overview.Account.Equity.Should().BeNull();
        overview.Account.Balance.Should().BeNull();
        overview.Account.AvailableBalance.Should().BeNull();
        overview.Account.UsedMargin.Should().BeNull();
        overview.Account.UnrealizedPnL.Should().BeNull();
    }

    [Fact]
    public async Task GetOverviewAsync_WithOrders_ShouldAggregateCorrectly()
    {
        // Arrange
        // Add 5 orders: 1 open (Pending), 1 filled (Filled), 1 cancelled (Cancelled), 2 failed (Failed & ValidationFailed)
        var order1 = new Order("CL-1", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(40000m));
        order1.UpdateStatus(OrderStatus.Pending);

        var order2 = new Order("CL-2", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(40000m));
        order2.UpdateStatus(OrderStatus.Pending);
        order2.UpdateStatus(OrderStatus.Submitting);
        order2.UpdateStatus(OrderStatus.Submitted);
        order2.UpdateStatus(OrderStatus.Filled);

        var order3 = new Order("CL-3", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(40000m));
        order3.UpdateStatus(OrderStatus.Pending);
        order3.UpdateStatus(OrderStatus.Submitting);
        order3.UpdateStatus(OrderStatus.Submitted);
        order3.UpdateStatus(OrderStatus.Cancelled);

        var order4 = new Order("CL-4", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(40000m));
        order4.UpdateStatus(OrderStatus.Pending);
        order4.UpdateStatus(OrderStatus.Failed);

        var order5 = new Order("CL-5", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(40000m));
        order5.UpdateStatus(OrderStatus.ValidationFailed);

        _dbContext.Orders.AddRange(order1, order2, order3, order4, order5);
        await _dbContext.SaveChangesAsync();

        // Act
        var overview = await _queryService.GetOverviewAsync(CancellationToken.None);

        // Assert
        overview.Orders.TotalOrders.Should().Be(5);
        overview.Orders.OpenOrders.Should().Be(1);
        overview.Orders.FilledOrders.Should().Be(1);
        overview.Orders.CancelledOrders.Should().Be(1);
        overview.Orders.FailedOrders.Should().Be(2); // Failed + ValidationFailed
    }

    [Fact]
    public async Task GetOverviewAsync_WithPositions_ShouldAggregateCorrectly()
    {
        // Arrange
        // Add 3 positions: 2 open (1 LONG, 1 SHORT), 1 closed
        var pos1 = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 40000m, 1m, margin: 100m, initialStatus: PositionStatus.Open);
        pos1.UpdatePrice(41000m); // Unrealized PnL = (41000 - 40000) * 1 = 1000m

        var pos2 = new Position(Guid.NewGuid(), "ETHUSDT", OrderSide.Sell, 2000m, 2m, margin: 50m, initialStatus: PositionStatus.PartiallyClosed);
        pos2.UpdatePrice(1900m); // Unrealized PnL = (2000 - 1900) * 2 = 200m

        var pos3 = new Position(Guid.NewGuid(), "SOLUSDT", OrderSide.Buy, 100m, 5m, margin: 10m, initialStatus: PositionStatus.Closed);

        _dbContext.Positions.AddRange(pos1, pos2, pos3);
        await _dbContext.SaveChangesAsync();

        // Act
        var overview = await _queryService.GetOverviewAsync(CancellationToken.None);

        // Assert
        overview.Positions.OpenPositionCount.Should().Be(2); // Open + PartiallyClosed
        overview.Positions.LongPositionCount.Should().Be(1);
        overview.Positions.ShortPositionCount.Should().Be(1);

        // Margin and Unrealized PnL are summed from open positions only
        overview.Account.UsedMargin.Should().Be(150m); // 100 + 50
        overview.Account.UnrealizedPnL.Should().Be(1200m); // 1000 + 200
    }

    [Fact]
    public async Task GetOverviewAsync_WithTrades_ShouldAggregatePnLAndFeesCorrectly()
    {
        // Arrange
        // Add 3 trades: 2 winning, 1 losing
        var trade1 = new Trade(Guid.NewGuid(), 40000m, 41000m, 0.1m, 100m, 10m, DateTime.UtcNow); // Win
        var trade2 = new Trade(Guid.NewGuid(), 2000m, 2050m, 1.0m, 50m, 5m, DateTime.UtcNow); // Win
        var trade3 = new Trade(Guid.NewGuid(), 40000m, 39000m, 0.1m, -120m, 8m, DateTime.UtcNow); // Loss

        _dbContext.Trades.AddRange(trade1, trade2, trade3);
        await _dbContext.SaveChangesAsync();

        // Act
        var overview = await _queryService.GetOverviewAsync(CancellationToken.None);

        // Assert
        overview.Trades.TotalTrades.Should().Be(3);
        overview.Trades.WinningTrades.Should().Be(2);
        overview.Trades.LosingTrades.Should().Be(1);

        overview.Pnl.RealizedPnL.Should().Be(30m); // 100 + 50 - 120 = 30
        overview.Pnl.TotalFees.Should().Be(23m); // 10 + 5 + 8 = 23
        overview.Pnl.NetPnL.Should().Be(7m); // Gross PnL (30) - Fees (23) = 7
    }

    [Fact]
    public async Task GetOverviewAsync_WithHealthProvider_ShouldMapStatusesCorrectly()
    {
        // Arrange
        _healthStatusProviderMock.Setup(h => h.GetOverallStatus()).Returns(HealthStatus.Degraded);

        // Database health
        var dbResult = new HealthCheckResult("Database", HealthStatus.Healthy, DateTime.UtcNow, 15);
        _healthStatusProviderMock.Setup(h => h.GetComponentStatus("Database")).Returns(dbResult);

        // Telegram health (with metadata)
        var tgResult = new HealthCheckResult("Telegram", HealthStatus.Healthy, DateTime.UtcNow, 100,
            metadata: "{\"ConnectionStatus\":\"Connected\",\"RawState\":\"Listening\"}");
        _healthStatusProviderMock.Setup(h => h.GetComponentStatus("Telegram")).Returns(tgResult);

        // Exchange health (REST degraded, WS healthy)
        var restResult = new HealthCheckResult("Bybit REST", HealthStatus.Degraded, DateTime.UtcNow, 250);
        var wsResult = new HealthCheckResult("Bybit WebSocket", HealthStatus.Healthy, DateTime.UtcNow, 12,
            metadata: "{\"ConnectionStatus\":\"Connected\",\"RawState\":\"Connected\"}");

        _healthStatusProviderMock.Setup(h => h.GetComponentStatus("Bybit REST")).Returns(restResult);
        _healthStatusProviderMock.Setup(h => h.GetComponentStatus("Bybit WebSocket")).Returns(wsResult);

        // Act
        var overview = await _queryService.GetOverviewAsync(CancellationToken.None);

        // Assert
        overview.System.ApplicationStatus.Should().Be("Degraded");
        overview.Database.DatabaseStatus.Should().Be("Healthy");
        overview.Telegram.TelegramStatus.Should().Be("Healthy");
        overview.Telegram.ConnectionStatus.Should().Be("Connected");
        overview.Exchange.ExchangeStatus.Should().Be("Degraded"); // REST (Degraded) + WS (Healthy) -> Degraded
        overview.Exchange.ConnectionStatus.Should().Be("Connected");
    }

    [Fact]
    public async Task GetOverviewAsync_WithUnavailableData_ShouldReturnNullsAndDefaults()
    {
        // Arrange
        var serviceWithoutMocks = new DashboardQueryService(_dbContext, null, null);

        // Act
        var overview = await serviceWithoutMocks.GetOverviewAsync(CancellationToken.None);

        // Assert
        overview.System.ApplicationStatus.Should().Be("Unknown");
        overview.System.Uptime.Should().Be("00:00:00");
        overview.Database.DatabaseStatus.Should().Be("Unknown");
        overview.Exchange.ExchangeStatus.Should().Be("Unknown");
        overview.Exchange.ConnectionStatus.Should().Be("Unknown");
        overview.Telegram.TelegramStatus.Should().Be("Unknown");
        overview.Telegram.ConnectionStatus.Should().Be("Unknown");
    }

    [Fact]
    public async Task GetOverviewAsync_ReadOnlyGuarantee_ShouldNotModifyDatabaseState()
    {
        // Arrange
        var order = new Order("CL-READONLY", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(40000m));
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        var initialCount = await _dbContext.Orders.CountAsync();
        var initialOrderState = await _dbContext.Orders.AsNoTracking().FirstAsync();

        // Act
        var overview = await _queryService.GetOverviewAsync(CancellationToken.None);

        // Assert
        var finalCount = await _dbContext.Orders.CountAsync();
        var finalOrderState = await _dbContext.Orders.AsNoTracking().FirstAsync();

        finalCount.Should().Be(initialCount);
        finalOrderState.Status.Should().Be(initialOrderState.Status);
        _dbContext.ChangeTracker.HasChanges().Should().BeFalse();
    }
}
