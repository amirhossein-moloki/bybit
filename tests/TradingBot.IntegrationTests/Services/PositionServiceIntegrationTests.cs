using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;
using TradingBot.Domain.ValueObjects;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;
using Symbol = TradingBot.Domain.ValueObjects.Symbol;

namespace TradingBot.IntegrationTests.Services;

public class PositionServiceIntegrationTests : IAsyncLifetime
{
    private SqliteConnection? _sqliteConnection;

    public async Task InitializeAsync()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        await _sqliteConnection.OpenAsync();
        using var command = _sqliteConnection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_sqliteConnection != null)
        {
            await _sqliteConnection.CloseAsync();
            await _sqliteConnection.DisposeAsync();
        }
    }

    private TradingDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseSqlite(_sqliteConnection!)
            .Options;

        var context = new TradingDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task CreatePositionFromOrderAsync_ShouldSucceed_WhenValidOrderFilled()
    {
        // Arrange
        using var context = CreateDbContext();
        var positionRepo = new PositionRepository(context);
        var signalRepo = new SignalRepository(context);
        var orderRepo = new OrderRepository(context);
        var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);
        var service = new PositionService(positionRepo, signalRepo, unitOfWork, NullLogger<PositionService>.Instance);

        // 1. Save Signal
        var signal = new Signal("TELEGRAM", "BUY BTCUSDT @ 50000", "BTCUSDT", OrderSide.Buy, 50000m, 0.5m, stopLoss: 48000m, takeProfit: 55000m, leverage: 10);
        await signalRepo.AddAsync(signal);
        await context.SaveChangesAsync();

        // 2. Save Order
        var order = new Order("INT-CLIENT-123", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(50000m), signalId: signal.Id);
        order.Submit();
        order.Accept("ex-order-999");
        order.RecordExecution(0.5m, 50000m); // Marks order as Filled
        await orderRepo.AddAsync(order);
        await context.SaveChangesAsync();

        var targets = new List<PositionTarget>
        {
            new PositionTarget(Guid.Empty, 1, 55000m, 0.25m, 50m),
            new PositionTarget(Guid.Empty, 2, 60000m, 0.25m, 50m)
        };

        // Act
        var position = await service.CreatePositionFromOrderAsync(order, targets, CancellationToken.None);

        // Assert
        position.Should().NotBeNull();
        position.OrderId.Should().Be(order.Id);
        position.Symbol.Should().Be("BTCUSDT");
        position.Side.Should().Be(OrderSide.Buy);
        position.EntryPrice.Should().Be(50000m);
        position.Quantity.Should().Be(0.5m);
        position.StopLoss.Should().Be(48000m);
        position.TakeProfit.Should().Be(55000m);
        position.Leverage.Should().Be(10m);
        position.Status.Should().Be(PositionStatus.Open);
        position.ExchangePositionId.Should().Be("ex-order-999");

        // Verify saved targets and events
        position.Targets.Should().HaveCount(2);
        position.Events.Should().HaveCount(1);
        position.Events.First().EventType.Should().Be("PositionOpened");

        // Act - Retrieve from database
        using var context2 = CreateDbContext();
        var positionRepo2 = new PositionRepository(context2);
        var savedPosition = await positionRepo2.GetByOrderIdAsync(order.Id, CancellationToken.None);

        savedPosition.Should().NotBeNull();
        savedPosition!.Id.Should().Be(position.Id);
        savedPosition.Targets.Should().HaveCount(2);
        savedPosition.Events.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreatePositionFromOrderAsync_ShouldBeIdempotent_WhenSameOrderProcessedTwice()
    {
        // Arrange
        using var context = CreateDbContext();
        var positionRepo = new PositionRepository(context);
        var signalRepo = new SignalRepository(context);
        var orderRepo = new OrderRepository(context);
        var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);
        var service = new PositionService(positionRepo, signalRepo, unitOfWork, NullLogger<PositionService>.Instance);

        var order = new Order("INT-CLIENT-IDEM", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(50000m));
        order.Submit();
        order.Accept("ex-order-idem");
        order.RecordExecution(0.5m, 50000m); // Marks as Filled
        await orderRepo.AddAsync(order);
        await context.SaveChangesAsync();

        // Act - Process 1st time
        var position1 = await service.CreatePositionFromOrderAsync(order, null, CancellationToken.None);

        // Act - Process 2nd time
        var position2 = await service.CreatePositionFromOrderAsync(order, null, CancellationToken.None);

        // Assert - Should return same instance, not create a new one
        position2.Should().NotBeNull();
        position2.Id.Should().Be(position1.Id);

        // Verify only 1 position exists in database
        using var context2 = CreateDbContext();
        var allPositions = await context2.Positions.ToListAsync();
        allPositions.Should().ContainSingle();
    }

    [Fact]
    public async Task CreatePositionFromOrderAsync_ShouldThrowDomainException_WhenOrderNotFilled()
    {
        // Arrange
        using var context = CreateDbContext();
        var positionRepo = new PositionRepository(context);
        var signalRepo = new SignalRepository(context);
        var orderRepo = new OrderRepository(context);
        var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);
        var service = new PositionService(positionRepo, signalRepo, unitOfWork, NullLogger<PositionService>.Instance);

        var order = new Order("INT-CLIENT-UNFILLED", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(50000m));
        order.Submit(); // Order status is Submitted (not Filled)
        await orderRepo.AddAsync(order);
        await context.SaveChangesAsync();

        // Act & Assert
        Func<Task> act = async () => await service.CreatePositionFromOrderAsync(order, null, CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*Cannot create a position from an order with status Submitted*");
    }

    [Fact]
    public async Task UpdatePositionStatusAsync_ShouldPersistStateAndEventsCorrectly()
    {
        // Arrange
        using var context = CreateDbContext();
        var positionRepo = new PositionRepository(context);
        var signalRepo = new SignalRepository(context);
        var orderRepo = new OrderRepository(context);
        var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);
        var service = new PositionService(positionRepo, signalRepo, unitOfWork, NullLogger<PositionService>.Instance);

        var order = new Order("INT-CLIENT-LIFECYCLE", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(50000m));
        order.Submit();
        order.Accept("ex-order-lifecycle");
        order.RecordExecution(0.5m, 50000m);
        await orderRepo.AddAsync(order);
        await context.SaveChangesAsync();

        var position = await service.CreatePositionFromOrderAsync(order, null, CancellationToken.None);

        // Act - Close position in a separate context to simulate realistic lifecycle persistence where the entity is loaded fresh
        using var updateContext = CreateDbContext();
        var positionRepoU = new PositionRepository(updateContext);
        var signalRepoU = new SignalRepository(updateContext);
        var unitOfWorkU = new UnitOfWork(updateContext, NullLogger<UnitOfWork>.Instance);
        var updateService = new PositionService(positionRepoU, signalRepoU, unitOfWorkU, NullLogger<PositionService>.Instance);

        try
        {
            await updateService.UpdatePositionStatusAsync(position.Id, PositionStatus.Closed, "Target 1 Hit", CancellationToken.None);
        }
        catch (Exception ex)
        {
            using (var writer = new StreamWriter("diagnostic_output.txt"))
            {
                writer.WriteLine($"[DIAGNOSTIC] EXCEPTION THROWN: {ex}");
                if (ex.InnerException != null)
                {
                    writer.WriteLine($"[DIAGNOSTIC] INNER EXCEPTION: {ex.InnerException}");
                    if (ex.InnerException is DbUpdateConcurrencyException cex)
                    {
                        foreach (var entry in cex.Entries)
                        {
                            writer.WriteLine($"[DIAGNOSTIC] ENTRY: {entry.Entity.GetType().Name}, State: {entry.State}");
                            foreach (var prop in entry.OriginalValues.Properties)
                            {
                                writer.WriteLine($"[DIAGNOSTIC] PROP {prop.Name}: Original = {entry.OriginalValues[prop]}, Current = {entry.CurrentValues[prop]}");
                            }
                        }
                    }
                }
            }
            throw;
        }

        // Assert - Retrieve from separate context
        using var context2 = CreateDbContext();
        var positionRepo2 = new PositionRepository(context2);
        var savedPosition = await positionRepo2.GetByOrderIdAsync(order.Id, CancellationToken.None);

        savedPosition.Should().NotBeNull();
        savedPosition!.Status.Should().Be(PositionStatus.Closed);
        savedPosition.Events.Should().HaveCount(2); // PositionOpened, PositionClosed
        savedPosition.Events.Any(e => e.EventType == "PositionClosed").Should().BeTrue();
    }
}
