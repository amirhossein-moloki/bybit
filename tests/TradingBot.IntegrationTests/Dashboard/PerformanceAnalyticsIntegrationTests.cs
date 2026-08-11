using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Analytics.DTOs;
using TradingBot.Application.Analytics.Interfaces;
using TradingBot.Application.Analytics.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Queries;
using Xunit;

using SymbolValueObject = TradingBot.Domain.ValueObjects.Symbol;

namespace TradingBot.IntegrationTests.Dashboard;

public class PerformanceAnalyticsIntegrationTests : IAsyncLifetime
{
    private SqliteConnection? _sqliteConnection;
    private TradingDbContext? _dbContext;
    private IPerformanceAnalyticsQueryService? _queryService;
    private IPerformanceAnalyticsService? _analyticsService;

    public async Task InitializeAsync()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        await _sqliteConnection.OpenAsync();

        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _dbContext = new TradingDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _queryService = new PerformanceAnalyticsQueryService(_dbContext);
        _analyticsService = new PerformanceAnalyticsService(
            _queryService,
            new DrawdownCalculator(),
            new StreakCalculator(),
            new PnLCalculator()
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

    private static void SetFieldOrProperty(object obj, string name, object? value)
    {
        var type = obj.GetType();
        var field = type.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (field != null)
        {
            field.SetValue(obj, value);
            return;
        }

        var prop = type.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(obj, value);
        }
    }

    [Fact]
    public async Task GetCompletedTradesAsync_WithNoTradeSymbolAndSide_ShouldJoinPositionAndResolveSymbolAndSide()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;

        var o1 = new Order("INT-PE-O1", new SymbolValueObject("ETHUSDT"), OrderSide.Sell, OrderType.Limit, new Quantity(1m), new Money(3000m));
        o1.UpdateStatus(OrderStatus.Filled);
        _dbContext!.Orders.Add(o1);
        await _dbContext.SaveChangesAsync();

        var pos1 = new Position(o1.Id, "ETHUSDT", OrderSide.Sell, 3000m, 1m, margin: 100m, initialStatus: PositionStatus.Closed);
        _dbContext.Positions.Add(pos1);
        await _dbContext.SaveChangesAsync();

        // Create completed trade using constructor that sets empty Symbol and SignalType.Buy as Side
        var trade1 = new Trade(
            positionId: pos1.Id,
            entryPrice: 3000m,
            exitPrice: 2900m,
            quantity: 1m,
            grossPnL: 100m,
            tradingFee: 2m,
            fundingFee: 0m,
            netPnL: 98m,
            closeReason: CloseReason.TakeProfit,
            openedAt: baseTime.AddMinutes(-30),
            closedAt: baseTime
        );

        _dbContext.Trades.Add(trade1);
        await _dbContext.SaveChangesAsync();

        // Act
        var query = new GetAnalyticsQuery();
        var result = await _queryService!.GetCompletedTradesAsync(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].Symbol.Should().Be("ETHUSDT");
        result[0].Side.Should().Be(OrderSide.Sell); // Resolved from Position.Side!
        result[0].NetPnL.Should().Be(98m);
    }

    [Fact]
    public async Task GetPerformanceMetricsAsync_WithRealDataInSqlite_ShouldEvaluateAccurately()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;

        var o1 = new Order("O1", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(50000m));
        o1.UpdateStatus(OrderStatus.Filled);
        var o2 = new Order("O2", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(50000m));
        o2.UpdateStatus(OrderStatus.Filled);
        _dbContext!.Orders.AddRange(o1, o2);
        await _dbContext.SaveChangesAsync();

        var pos1 = new Position(o1.Id, "BTCUSDT", OrderSide.Buy, 50000m, 1m, margin: 100m, initialStatus: PositionStatus.Closed);
        var pos2 = new Position(o2.Id, "BTCUSDT", OrderSide.Buy, 50000m, 1m, margin: 100m, initialStatus: PositionStatus.Closed);
        _dbContext.Positions.AddRange(pos1, pos2);
        await _dbContext.SaveChangesAsync();

        var t1 = new Trade(pos1.Id, 50000m, 51000m, 1m, 1000m, 10m, baseTime.AddMinutes(-10));
        SetFieldOrProperty(t1, "OpenedAt", baseTime.AddMinutes(-40));
        SetFieldOrProperty(t1, "ClosedAt", baseTime.AddMinutes(-10));
        SetFieldOrProperty(t1, "NetPnL", 990m); // Win

        var t2 = new Trade(pos2.Id, 50000m, 49500m, 1m, -500m, 10m, baseTime);
        SetFieldOrProperty(t2, "OpenedAt", baseTime.AddMinutes(-20));
        SetFieldOrProperty(t2, "ClosedAt", baseTime);
        SetFieldOrProperty(t2, "NetPnL", -510m); // Loss

        _dbContext.Trades.AddRange(t1, t2);
        await _dbContext.SaveChangesAsync();

        // Act
        var performance = await _analyticsService!.GetPerformanceMetricsAsync(new GetAnalyticsQuery());
        var drawdown = await _analyticsService!.GetDrawdownMetricsAsync(new GetAnalyticsQuery(InitialBalance: 10000m));
        var streaks = await _analyticsService!.GetStreakMetricsAsync(new GetAnalyticsQuery());
        var durations = await _analyticsService!.GetDurationMetricsAsync(new GetAnalyticsQuery());
        var sidePerformance = await _analyticsService!.GetLongShortPerformanceAsync(new GetAnalyticsQuery());

        // Assert
        performance.TotalTrades.Should().Be(2);
        performance.WinningTrades.Should().Be(1);
        performance.LosingTrades.Should().Be(1);
        performance.NetPnL.Should().Be(480m); // 990 - 510

        drawdown.PeakEquity.Should().Be(10990m); // 10000 + 990
        drawdown.CurrentEquity.Should().Be(10480m);
        drawdown.MaximumDrawdown.Should().Be(510m);

        streaks.MaximumWinStreak.Should().Be(1);
        streaks.MaximumLossStreak.Should().Be(1);

        durations.AverageDuration.Should().Be(TimeSpan.FromMinutes(25)); // (30 + 20) / 2 = 25

        sidePerformance.Long.Trades.Should().Be(2);
        sidePerformance.Long.TotalPnL.Should().Be(480m);
        sidePerformance.Short.Trades.Should().Be(0);
    }
}
