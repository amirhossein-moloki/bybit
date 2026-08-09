using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
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

namespace TradingBot.UnitTests.Dashboard;

public class TradingDashboardQueryServiceTests : IDisposable
{
    private readonly TradingDbContext _dbContext;
    private readonly ITradingDashboardQueryService _queryService;

    public TradingDashboardQueryServiceTests()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TradingDbContext(options);
        _queryService = new TradingDashboardQueryService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetOverviewAsync_WithEmptyDatabase_ShouldReturnEmptyStatesAndZeroes()
    {
        // Arrange
        var query = new TradingDashboardQuery();

        // Act
        var overview = await _queryService.GetOverviewAsync(query, CancellationToken.None);

        // Assert
        overview.Should().NotBeNull();
        overview.Orders.TotalOrders.Should().Be(0);
        overview.Orders.OpenOrders.Should().Be(0);
        overview.Orders.FilledOrders.Should().Be(0);
        overview.Orders.CancelledOrders.Should().Be(0);
        overview.Orders.RejectedOrders.Should().Be(0);
        overview.Orders.FailedOrders.Should().Be(0);

        overview.Positions.OpenPositionCount.Should().Be(0);
        overview.Positions.LongPositionCount.Should().Be(0);
        overview.Positions.ShortPositionCount.Should().Be(0);
        overview.Positions.TotalOpenQuantity.Should().Be(0m);
        overview.Positions.TotalUnrealizedPnL.Should().Be(0m);

        overview.Trades.TotalTrades.Should().Be(0);
        overview.Trades.WinningTrades.Should().Be(0);
        overview.Trades.LosingTrades.Should().Be(0);
        overview.Trades.BreakEvenTrades.Should().Be(0);
        overview.Trades.WinRate.Should().Be(0m);

        overview.Performance.TotalTrades.Should().Be(0);
        overview.Performance.WinningTrades.Should().Be(0);
        overview.Performance.LosingTrades.Should().Be(0);
        overview.Performance.WinRate.Should().Be(0m);
        overview.Performance.GrossPnL.Should().Be(0m);
        overview.Performance.Fees.Should().Be(0m);
        overview.Performance.NetPnL.Should().Be(0m);

        overview.Pnl.GrossPnL.Should().Be(0m);
        overview.Pnl.TotalFees.Should().Be(0m);
        overview.Pnl.NetPnL.Should().Be(0m);

        overview.Fees.TotalFees.Should().Be(0m);

        overview.OpenPositions.Items.Should().BeEmpty();
        overview.OpenPositions.TotalCount.Should().Be(0);

        overview.ActiveOrders.Items.Should().BeEmpty();
        overview.ActiveOrders.TotalCount.Should().Be(0);

        overview.RecentTrades.Items.Should().BeEmpty();
        overview.RecentTrades.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOverviewAsync_WithOrders_ShouldAggregateCorrectly()
    {
        // Arrange: 6 orders (1 pending, 1 filled, 1 cancelled, 1 rejected, 1 failed, 1 validationFailed)
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
        order4.UpdateStatus(OrderStatus.Rejected);

        var order5 = new Order("CL-5", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(40000m));
        order5.UpdateStatus(OrderStatus.Pending);
        order5.UpdateStatus(OrderStatus.Failed);

        var order6 = new Order("CL-6", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(40000m));
        order6.UpdateStatus(OrderStatus.ValidationFailed);

        _dbContext.Orders.AddRange(order1, order2, order3, order4, order5, order6);
        await _dbContext.SaveChangesAsync();

        // Act
        var overview = await _queryService.GetOverviewAsync(new TradingDashboardQuery(), CancellationToken.None);

        // Assert
        overview.Orders.TotalOrders.Should().Be(6);
        overview.Orders.OpenOrders.Should().Be(1);
        overview.Orders.FilledOrders.Should().Be(1);
        overview.Orders.CancelledOrders.Should().Be(1);
        overview.Orders.RejectedOrders.Should().Be(1);
        overview.Orders.FailedOrders.Should().Be(2); // Failed + ValidationFailed
    }

    [Fact]
    public async Task GetOverviewAsync_WithPositions_ShouldSumMarginAndUnrealizedPnL()
    {
        // Arrange: 3 positions (1 LONG open, 1 SHORT partially closed, 1 LONG closed)
        var pos1 = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 40000m, 1m, margin: 100m, initialStatus: PositionStatus.Open);
        pos1.UpdatePrice(41000m); // Unrealized PnL = (41000 - 40000) * 1 = 1000m

        var pos2 = new Position(Guid.NewGuid(), "ETHUSDT", OrderSide.Sell, 2000m, 2m, margin: 50m, initialStatus: PositionStatus.PartiallyClosed);
        pos2.UpdatePrice(1900m); // Unrealized PnL = (2000 - 1900) * 2 = 200m

        var pos3 = new Position(Guid.NewGuid(), "SOLUSDT", OrderSide.Buy, 100m, 5m, margin: 10m, initialStatus: PositionStatus.Closed);

        _dbContext.Positions.AddRange(pos1, pos2, pos3);
        await _dbContext.SaveChangesAsync();

        // Act
        var overview = await _queryService.GetOverviewAsync(new TradingDashboardQuery(), CancellationToken.None);

        // Assert
        overview.Positions.OpenPositionCount.Should().Be(2); // Open + PartiallyClosed
        overview.Positions.LongPositionCount.Should().Be(1);
        overview.Positions.ShortPositionCount.Should().Be(1);
        overview.Positions.TotalOpenQuantity.Should().Be(3m); // 1m + 2m
        overview.Positions.TotalUnrealizedPnL.Should().Be(1200m); // 1000 + 200
    }

    [Fact]
    public async Task GetOverviewAsync_WithTrades_ShouldCalculatePerformanceMetricsCorrectly()
    {
        // Arrange: 4 trades: 2 wins, 1 loss, 1 break-even
        var trade1 = new Trade(Guid.NewGuid(), 40000m, 41000m, 0.1m, 100m, 10m, DateTime.UtcNow); // Win (PnL=100, Net=90)
        var trade2 = new Trade(Guid.NewGuid(), 2000m, 2050m, 1.0m, 50m, 5m, DateTime.UtcNow); // Win (PnL=50, Net=45)
        var trade3 = new Trade(Guid.NewGuid(), 40000m, 39000m, 0.1m, -120m, 8m, DateTime.UtcNow); // Loss (PnL=-120, Net=-128)
        var trade4 = new Trade(Guid.NewGuid(), 100m, 100m, 10m, 0m, 2m, DateTime.UtcNow); // Break-Even (PnL=0, Net=-2)

        _dbContext.Trades.AddRange(trade1, trade2, trade3, trade4);
        await _dbContext.SaveChangesAsync();

        // Act
        var overview = await _queryService.GetOverviewAsync(new TradingDashboardQuery(), CancellationToken.None);

        // Assert
        overview.Trades.TotalTrades.Should().Be(4);
        overview.Trades.WinningTrades.Should().Be(2);
        overview.Trades.LosingTrades.Should().Be(1); // trade3 is losing (ProfitLoss < 0)
        overview.Trades.BreakEvenTrades.Should().Be(1); // trade4 is break-even (ProfitLoss == 0)
        overview.Trades.WinRate.Should().Be(50m); // 2 / 4 = 50%

        overview.Pnl.GrossPnL.Should().Be(30m); // 100 + 50 - 120 + 0 = 30
        overview.Pnl.TotalFees.Should().Be(25m); // 10 + 5 + 8 + 2 = 25
        overview.Pnl.NetPnL.Should().Be(5m); // Gross (30) - Fees (25) = 5
    }

    [Fact]
    public async Task GetOverviewAsync_WithFilters_ShouldRestrictResultsAndAggregates()
    {
        // Arrange
        var orderBtc = new Order("CL-BTC", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(40000m));
        orderBtc.UpdateStatus(OrderStatus.Pending);

        var orderEth = new Order("CL-ETH", new SymbolValueObject("ETHUSDT"), OrderSide.Sell, OrderType.Limit, new Quantity(2m), new Money(2000m));
        orderEth.UpdateStatus(OrderStatus.Pending);

        _dbContext.Orders.AddRange(orderBtc, orderEth);
        await _dbContext.SaveChangesAsync();

        // Act 1: Filter by Symbol
        var overviewBtc = await _queryService.GetOverviewAsync(new TradingDashboardQuery(Symbol: "BTCUSDT"), CancellationToken.None);
        overviewBtc.Orders.TotalOrders.Should().Be(1);
        overviewBtc.ActiveOrders.Items.Should().ContainSingle(o => o.Symbol == "BTCUSDT");

        // Act 2: Filter by Side
        var overviewSell = await _queryService.GetOverviewAsync(new TradingDashboardQuery(Side: OrderSide.Sell), CancellationToken.None);
        overviewSell.Orders.TotalOrders.Should().Be(1);
        overviewSell.ActiveOrders.Items.Should().ContainSingle(o => o.Symbol == "ETHUSDT");
    }

    [Fact]
    public async Task GetOverviewAsync_WithInvalidDateRange_ShouldThrowArgumentException()
    {
        // Arrange
        var query = new TradingDashboardQuery(From: DateTime.UtcNow, To: DateTime.UtcNow.AddMinutes(-5));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _queryService.GetOverviewAsync(query, CancellationToken.None));
    }

    [Fact]
    public async Task GetOverviewAsync_WithPagination_ShouldBoundReturnedCollections()
    {
        // Arrange: Add 5 orders
        for (int i = 1; i <= 5; i++)
        {
            var o = new Order($"CL-PAG-{i}", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(40000m));
            o.UpdateStatus(OrderStatus.Pending);
            _dbContext.Orders.Add(o);
        }
        await _dbContext.SaveChangesAsync();

        // Act: Page 1 with size 2
        var query = new TradingDashboardQuery(Page: 1, PageSize: 2);
        var overview = await _queryService.GetOverviewAsync(query, CancellationToken.None);

        // Assert
        overview.ActiveOrders.Items.Should().HaveCount(2);
        overview.ActiveOrders.TotalCount.Should().Be(5);
        overview.ActiveOrders.PageNumber.Should().Be(1);
        overview.ActiveOrders.PageSize.Should().Be(2);

        // Act: Page 3 with size 2 (should have last remaining order)
        var queryPage3 = new TradingDashboardQuery(Page: 3, PageSize: 2);
        var overviewPage3 = await _queryService.GetOverviewAsync(queryPage3, CancellationToken.None);
        overviewPage3.ActiveOrders.Items.Should().HaveCount(1);
    }
}
