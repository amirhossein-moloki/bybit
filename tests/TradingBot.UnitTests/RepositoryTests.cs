using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using Symbol = TradingBot.Domain.ValueObjects.Symbol;
using TradingBot.Domain.ValueObjects;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using Xunit;

namespace TradingBot.UnitTests;

public class RepositoryTests : IDisposable
{
    private readonly TradingDbContext _dbContext;

    public RepositoryTests()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TradingDbContext(options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task RepositoryBase_AddAndGetById_ShouldPersistEntity()
    {
        // Arrange
        var repo = new OrderRepository(_dbContext);
        var order = new Order(
            "CL-111",
            new Symbol("BTCUSDT"),
            OrderSide.Buy,
            OrderType.Limit,
            new Quantity(0.5m),
            new Money(40000m)
        );

        // Act
        await repo.AddAsync(order, CancellationToken.None);
        await _dbContext.SaveChangesAsync();

        var retrieved = await repo.GetByIdAsync(order.Id, CancellationToken.None);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.ClientOrderId.Should().Be("CL-111");
        retrieved.Symbol.Value.Should().Be("BTCUSDT");
    }

    [Fact]
    public async Task RepositoryBase_GetAll_ShouldReturnAllEntities()
    {
        // Arrange
        var repo = new OrderRepository(_dbContext);
        var order1 = new Order("CL-1", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(40000m));
        var order2 = new Order("CL-2", new Symbol("ETHUSDT"), OrderSide.Sell, OrderType.Limit, new Quantity(2m), new Money(2000m));

        await repo.AddAsync(order1);
        await repo.AddAsync(order2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await repo.GetAllAsync();

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task RepositoryBase_Update_ShouldModifyEntityState()
    {
        // Arrange
        var repo = new OrderRepository(_dbContext);
        var order = new Order("CL-U", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(40000m));
        await repo.AddAsync(order);
        await _dbContext.SaveChangesAsync();

        // Act
        order.Submit();
        order.Accept("EX-123");
        repo.Update(order);
        await _dbContext.SaveChangesAsync();

        var retrieved = await repo.GetByIdAsync(order.Id);

        // Assert
        retrieved!.Status.Should().Be(OrderStatus.Accepted);
        retrieved.ExchangeOrderId.Should().Be("EX-123");
    }

    [Fact]
    public async Task RepositoryBase_Remove_ShouldDeleteEntity()
    {
        // Arrange
        var repo = new OrderRepository(_dbContext);
        var order = new Order("CL-D", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(40000m));
        await repo.AddAsync(order);
        await _dbContext.SaveChangesAsync();

        // Act
        repo.Remove(order);
        await _dbContext.SaveChangesAsync();

        var retrieved = await repo.GetByIdAsync(order.Id);

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task OrderRepository_GetPagedOrders_ShouldReturnCorrectPages()
    {
        // Arrange
        var repo = new OrderRepository(_dbContext);
        for (int i = 1; i <= 15; i++)
        {
            var order = new Order($"CL-{i}", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(40000m));
            await repo.AddAsync(order);
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var pagedResult = await repo.GetPagedOrdersAsync(2, 5, CancellationToken.None);

        // Assert
        pagedResult.TotalCount.Should().Be(15);
        pagedResult.PageNumber.Should().Be(2);
        pagedResult.PageSize.Should().Be(5);
        pagedResult.Items.Should().HaveCount(5);
    }

    [Fact]
    public async Task SignalRepository_GetPendingSignals_ShouldOnlyReturnPendingStates()
    {
        // Arrange
        var repo = new SignalRepository(_dbContext);
        var sig1 = new Signal("TELEGRAM", "BUY BTCUSDT @ 40000", "BTCUSDT", OrderSide.Buy, 40000m, 1m);
        var sig2 = new Signal("TELEGRAM", "BUY ETHUSDT @ 2000", "ETHUSDT", OrderSide.Buy, 2000m, 2m);
        var sig3 = new Signal("TELEGRAM", "BUY SOLUSDT @ 100", "SOLUSDT", OrderSide.Buy, 100m, 5m);

        sig2.MarkParsed();
        sig2.MarkValidated();
        sig2.MarkExecuted(); // Non-pending

        sig3.MarkRejected(); // Non-pending

        await repo.AddAsync(sig1);
        await repo.AddAsync(sig2);
        await repo.AddAsync(sig3);
        await _dbContext.SaveChangesAsync();

        // Act
        var pending = await repo.GetPendingSignalsAsync();

        // Assert
        pending.Should().HaveCount(1);
        pending.First().Symbol.Should().Be("BTCUSDT");
    }

    [Fact]
    public async Task PositionRepository_GetOpenPositions_ShouldOnlyReturnOpenPositions()
    {
        // Arrange
        var repo = new PositionRepository(_dbContext);
        var pos1 = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 40000m, 0.5m);
        var pos2 = new Position(Guid.NewGuid(), "ETHUSDT", OrderSide.Sell, 2000m, 2m);

        pos2.Close(2100m);

        await repo.AddAsync(pos1);
        await repo.AddAsync(pos2);
        await _dbContext.SaveChangesAsync();

        // Act
        var openPositions = await repo.GetOpenPositionsAsync();

        // Assert
        openPositions.Should().HaveCount(1);
        openPositions.First().Symbol.Should().Be("BTCUSDT");
        openPositions.First().Status.Should().Be(PositionStatus.Open);
    }

    [Fact]
    public async Task TradeRepository_GetProfitLossReport_ShouldCalculateMetricsCorrectly()
    {
        // Arrange
        var repo = new TradeRepository(_dbContext);
        var t1 = new Trade(Guid.NewGuid(), 40000m, 41000m, 0.1m, 100m, 10m, DateTime.UtcNow); // Win (PnL = 100, Fee = 10)
        var t2 = new Trade(Guid.NewGuid(), 40000m, 39000m, 0.1m, -100m, 10m, DateTime.UtcNow); // Loss (PnL = -100, Fee = 10)
        var t3 = new Trade(Guid.NewGuid(), 2000m, 2050m, 1.0m, 50m, 5m, DateTime.UtcNow); // Win (PnL = 50, Fee = 5)

        await repo.AddAsync(t1);
        await repo.AddAsync(t2);
        await repo.AddAsync(t3);
        await _dbContext.SaveChangesAsync();

        // Act
        var report = await repo.GetProfitLossReportAsync();

        // Assert
        report.TotalTrades.Should().Be(3);
        report.WinTrades.Should().Be(2);
        report.LossTrades.Should().Be(1);
        report.TotalProfitLoss.Should().Be(50m); // 100 - 100 + 50 = 50
        report.TotalFee.Should().Be(25m); // 10 + 10 + 5 = 25
        report.WinRate.Should().Be(66.66666666666666666666666667m); // 2 / 3 * 100
    }
}
