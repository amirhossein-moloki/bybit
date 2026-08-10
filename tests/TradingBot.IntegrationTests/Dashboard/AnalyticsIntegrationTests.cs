using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Analytics.DTOs;
using TradingBot.Application.Analytics.Interfaces;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Queries;
using Xunit;

using SymbolValueObject = TradingBot.Domain.ValueObjects.Symbol;

namespace TradingBot.IntegrationTests.Dashboard;

public class AnalyticsIntegrationTests : IAsyncLifetime
{
    private SqliteConnection? _sqliteConnection;
    private TradingDbContext? _dbContext;
    private IAnalyticsQueryService? _queryService;

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

        _queryService = new AnalyticsQueryService(_dbContext);
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

    private void SetFieldOrProperty(object obj, string name, object? value)
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
    public async Task GetTradeStatisticsAsync_WithRealSqlite_ShouldInsertAndRetrieveStatsSuccessfully()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;

        // 1. Create matching orders to satisfy Position -> Order foreign key constraint
        var o1 = new Order("INT-AN-O1", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.1m), new Money(40000m));
        o1.UpdateStatus(OrderStatus.Filled);
        var o2 = new Order("INT-AN-O2", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.1m), new Money(40000m));
        o2.UpdateStatus(OrderStatus.Filled);

        _dbContext!.Orders.AddRange(o1, o2);
        await _dbContext.SaveChangesAsync();

        // 2. Create matching positions
        var pos1 = new Position(o1.Id, "BTCUSDT", OrderSide.Buy, 40000m, 0.1m, margin: 50m, initialStatus: PositionStatus.Closed);
        var pos2 = new Position(o2.Id, "BTCUSDT", OrderSide.Buy, 40000m, 0.1m, margin: 50m, initialStatus: PositionStatus.Closed);

        _dbContext.Positions.AddRange(pos1, pos2);
        await _dbContext.SaveChangesAsync();

        // 3. Create completed trades linked to positions
        var trade1 = new Trade(pos1.Id, 40000m, 40500m, 0.1m, 50m, 2m, baseTime.AddMinutes(-10));
        var trade2 = new Trade(pos2.Id, 40000m, 39800m, 0.1m, -20m, 2m, baseTime);

        // Populate optional properties using reflection
        SetFieldOrProperty(trade1, "OpenedAt", (DateTime?)baseTime.AddMinutes(-40));
        SetFieldOrProperty(trade1, "Symbol", "BTCUSDT");
        SetFieldOrProperty(trade1, "Side", SignalType.Buy);
        SetFieldOrProperty(trade1, "NetPnL", 48m); // gross: 50, fee: 2, net: 48

        SetFieldOrProperty(trade2, "OpenedAt", (DateTime?)baseTime.AddMinutes(-20));
        SetFieldOrProperty(trade2, "Symbol", "BTCUSDT");
        SetFieldOrProperty(trade2, "Side", SignalType.Buy);
        SetFieldOrProperty(trade2, "NetPnL", -22m); // gross: -20, fee: 2, net: -22

        _dbContext.Trades.AddRange(trade1, trade2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _queryService!.GetTradeStatisticsAsync(new GetTradeStatisticsQuery(), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.TotalTrades.Should().Be(2);
        result.WinningTrades.Should().Be(1);
        result.LosingTrades.Should().Be(1);
        result.BreakevenTrades.Should().Be(0);
        result.WinRate.Should().Be(50m);
        result.LossRate.Should().Be(50m);
        result.GrossProfit.Should().Be(48m);
        result.GrossLoss.Should().Be(22m);
        result.NetPnL.Should().Be(26m); // 48 - 22
        result.AveragePnL.Should().Be(13m); // 26 / 2
        result.AverageWin.Should().Be(48m);
        result.AverageLoss.Should().Be(22m);
        result.LargestWin.Should().Be(48m);
        result.LargestLoss.Should().Be(22m);
        result.ProfitFactor.Should().Be(48m / 22m);
        result.AverageDuration.Should().Be(TimeSpan.FromMinutes(25)); // (30 + 20)/2 = 25m
        result.ShortestDuration.Should().Be(TimeSpan.FromMinutes(20));
        result.LongestDuration.Should().Be(TimeSpan.FromMinutes(30));
        result.CurrentWinStreak.Should().Be(0);
        result.CurrentLossStreak.Should().Be(1);
        result.MaximumWinStreak.Should().Be(1);
        result.MaximumLossStreak.Should().Be(1);
    }
}
