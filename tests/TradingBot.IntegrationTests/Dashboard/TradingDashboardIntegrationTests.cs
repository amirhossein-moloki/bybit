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
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Queries;
using Xunit;

using SymbolValueObject = TradingBot.Domain.ValueObjects.Symbol;

namespace TradingBot.IntegrationTests.Dashboard;

public class TradingDashboardIntegrationTests : IAsyncLifetime
{
    private SqliteConnection? _sqliteConnection;
    private TradingDbContext? _dbContext;
    private ITradingDashboardQueryService? _queryService;

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

        _queryService = new TradingDashboardQueryService(_dbContext);
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
    public async Task GetOverviewAsync_ShouldAggregateAndReturnCorrectData_WithPersistedData()
    {
        // 1. Populate Orders
        // Standalone active order
        var activeOrder = new Order("INT-CL-ACTIVE", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(40000m));
        activeOrder.UpdateStatus(OrderStatus.Pending);

        // Filled order for open position
        var filledOrder1 = new Order("INT-CL-FILLED1", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(40000m));
        filledOrder1.UpdateStatus(OrderStatus.Pending);
        filledOrder1.UpdateStatus(OrderStatus.Submitting);
        filledOrder1.UpdateStatus(OrderStatus.Submitted);
        filledOrder1.UpdateStatus(OrderStatus.Filled);

        // Filled order for another open position
        var filledOrder2 = new Order("INT-CL-FILLED2", new SymbolValueObject("ETHUSDT"), OrderSide.Sell, OrderType.Limit, new Quantity(5m), new Money(2000m));
        filledOrder2.UpdateStatus(OrderStatus.Pending);
        filledOrder2.UpdateStatus(OrderStatus.Submitting);
        filledOrder2.UpdateStatus(OrderStatus.Submitted);
        filledOrder2.UpdateStatus(OrderStatus.Filled);

        _dbContext!.Orders.AddRange(activeOrder, filledOrder1, filledOrder2);
        await _dbContext.SaveChangesAsync();

        // 2. Populate Open Positions
        var pos1 = new Position(filledOrder1.Id, "BTCUSDT", OrderSide.Buy, 40000m, 0.5m, margin: 200m, initialStatus: PositionStatus.Open);
        pos1.UpdatePrice(41000m); // Unrealized PnL = (41000 - 40000) * 0.5 = 500m

        var pos2 = new Position(filledOrder2.Id, "ETHUSDT", OrderSide.Sell, 2000m, 5m, margin: 100m, initialStatus: PositionStatus.PartiallyClosed);
        pos2.UpdatePrice(2010m); // Unrealized PnL = (2000 - 2010) * 5 = -50m

        _dbContext.Positions.AddRange(pos1, pos2);
        await _dbContext.SaveChangesAsync();

        // 3. Populate Completed Trades (1-to-1 unique positions for trades)
        // 3 winning trades
        for (int i = 1; i <= 3; i++)
        {
            var ord = new Order($"INT-WIN-O-{i}", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(40000m));
            ord.UpdateStatus(OrderStatus.Pending);
            ord.UpdateStatus(OrderStatus.Submitting);
            ord.UpdateStatus(OrderStatus.Submitted);
            ord.UpdateStatus(OrderStatus.Filled);
            _dbContext.Orders.Add(ord);
            await _dbContext.SaveChangesAsync();

            var pos = new Position(ord.Id, "BTCUSDT", OrderSide.Buy, 40000m, 0.5m, margin: 50m, initialStatus: PositionStatus.Closed);
            _dbContext.Positions.Add(pos);
            await _dbContext.SaveChangesAsync();

            var winTrade = new Trade(pos.Id, 40000m, 40500m, 0.5m, 250m, 5m, DateTime.UtcNow);
            _dbContext.Trades.Add(winTrade);
            await _dbContext.SaveChangesAsync();
        }

        // 2 losing trades
        for (int i = 1; i <= 2; i++)
        {
            var ord = new Order($"INT-LOSS-O-{i}", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(40000m));
            ord.UpdateStatus(OrderStatus.Pending);
            ord.UpdateStatus(OrderStatus.Submitting);
            ord.UpdateStatus(OrderStatus.Submitted);
            ord.UpdateStatus(OrderStatus.Filled);
            _dbContext.Orders.Add(ord);
            await _dbContext.SaveChangesAsync();

            var pos = new Position(ord.Id, "BTCUSDT", OrderSide.Buy, 40000m, 0.5m, margin: 50m, initialStatus: PositionStatus.Closed);
            _dbContext.Positions.Add(pos);
            await _dbContext.SaveChangesAsync();

            var lossTrade = new Trade(pos.Id, 40000m, 39800m, 0.5m, -100m, 5m, DateTime.UtcNow);
            _dbContext.Trades.Add(lossTrade);
            await _dbContext.SaveChangesAsync();
        }

        // Act
        var overview = await _queryService!.GetOverviewAsync(new TradingDashboardQuery(), CancellationToken.None);

        // Assert
        overview.Should().NotBeNull();

        // Order Summary
        overview.Orders.TotalOrders.Should().Be(8); // 3 standalone + 3 wins + 2 losses
        overview.Orders.OpenOrders.Should().Be(1);
        overview.Orders.FilledOrders.Should().Be(7);

        // Position Summary
        overview.Positions.OpenPositionCount.Should().Be(2);
        overview.Positions.LongPositionCount.Should().Be(1);
        overview.Positions.ShortPositionCount.Should().Be(1);
        overview.Positions.TotalOpenQuantity.Should().Be(5.5m);
        overview.Positions.TotalUnrealizedPnL.Should().Be(450m);

        // Trade & Performance & PnL Summaries
        overview.Trades.TotalTrades.Should().Be(5);
        overview.Trades.WinningTrades.Should().Be(3);
        overview.Trades.LosingTrades.Should().Be(2);
        overview.Trades.WinRate.Should().Be(60m);

        overview.Pnl.GrossPnL.Should().Be(550m); // 3 * 250 - 2 * 100 = 550
        overview.Pnl.TotalFees.Should().Be(25m); // 5 * 5 = 25
        overview.Pnl.NetPnL.Should().Be(525m); // 550 - 25 = 525

        // Bounded list verifications
        overview.ActiveOrders.Items.Should().ContainSingle(o => o.Id == activeOrder.Id);
        overview.OpenPositions.Items.Should().HaveCount(2);
        overview.RecentTrades.Items.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetOverviewAsync_VerifyReadOnly_ShouldNotMutateState()
    {
        // Arrange
        var ord = new Order("INT-READONLY-CHK", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(40000m));
        _dbContext!.Orders.Add(ord);
        await _dbContext.SaveChangesAsync();

        var stateBefore = await _dbContext.Orders.AsNoTracking().FirstAsync();

        // Act
        await _queryService!.GetOverviewAsync(new TradingDashboardQuery(), CancellationToken.None);

        // Assert
        var stateAfter = await _dbContext.Orders.AsNoTracking().FirstAsync();
        stateAfter.Status.Should().Be(stateBefore.Status);
        stateAfter.UpdatedAt.Should().Be(stateBefore.UpdatedAt);
    }

    [Fact]
    public async Task GetOverviewAsync_VerifyBoundedQueryPerformance()
    {
        // Arrange: Populate 120 completed trades
        for (int i = 1; i <= 120; i++)
        {
            var ord = new Order($"INT-PERF-O-{i}", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(40000m));
            _dbContext!.Orders.Add(ord);
            await _dbContext.SaveChangesAsync();

            var pos = new Position(ord.Id, "BTCUSDT", OrderSide.Buy, 40000m, 0.5m, margin: 50m, initialStatus: PositionStatus.Closed);
            _dbContext.Positions.Add(pos);
            await _dbContext.SaveChangesAsync();

            var winTrade = new Trade(pos.Id, 40000m, 40500m, 0.5m, 250m, 5m, DateTime.UtcNow);
            _dbContext.Trades.Add(winTrade);
            await _dbContext.SaveChangesAsync();
        }

        // Act: Request page 1 with page size 50 (should only return 50 items)
        var query = new TradingDashboardQuery(Page: 1, PageSize: 50);
        var overview = await _queryService!.GetOverviewAsync(query, CancellationToken.None);

        // Assert: Ensure pagination is applied on database-side and only 50 items are materialized
        overview.RecentTrades.Items.Should().HaveCount(50);
        overview.RecentTrades.TotalCount.Should().Be(120);
    }
}
