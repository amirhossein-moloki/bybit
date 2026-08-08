using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Services;
using TradingBot.Application.Trading.Execution.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;

namespace TradingBot.IntegrationTests.Services;

public class PositionProtectionIntegrationTests : IAsyncLifetime
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
            .LogTo(msg => File.AppendAllText("ef_sql.log", msg + Environment.NewLine), Microsoft.Extensions.Logging.LogLevel.Information)
            .EnableSensitiveDataLogging()
            .Options;

        var context = new TradingDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task FullStopLossTakeProfitAndPartialCloseWorkflow_ShouldSucceed()
    {
        var instrumentRules = new TestExchangeInstrumentRules();
        var exchangeGateway = new TestExchangeTradingGateway();

        // 1. Create and Save an Order
        var orderId = Guid.Empty;
        using (var context = CreateDbContext())
        {
            var orderRepo = new OrderRepository(context);
            var order = new Order("INT-CLIENT-INT-001", new Domain.ValueObjects.Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Domain.ValueObjects.Quantity(0.01m), new Domain.ValueObjects.Money(60000m));
            order.Submit();
            order.Accept("ex-order-001");
            order.RecordExecution(0.01m, 60000m);
            await orderRepo.AddAsync(order);
            await context.SaveChangesAsync();
            orderId = order.Id;
        }

        // 2. Create and Save a Position
        Guid positionId;
        using (var context = CreateDbContext())
        {
            var positionRepo = new PositionRepository(context);
            var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);
            var position = new Position(orderId, "BTCUSDT", OrderSide.Buy, 60000m, 0.01m, exchangePositionId: "ex-order-001");
            await positionRepo.AddAsync(position);
            await unitOfWork.SaveChangesAsync();
            positionId = position.Id;
        }

        // 3. Set Stop Loss
        using (var context = CreateDbContext())
        {
            var positionRepo = new PositionRepository(context);
            var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);
            var slManager = new StopLossManager(positionRepo, exchangeGateway, instrumentRules, unitOfWork, NullLogger<StopLossManager>.Instance);
            var slResult = await slManager.UpdateStopLossAsync(positionId, 59000m);
            slResult.Should().BeTrue();
        }

        // Retrieve position to confirm SL is persisted
        using (var context = CreateDbContext())
        {
            var positionRepo = new PositionRepository(context);
            var positionAfterSL = await positionRepo.GetByIdAsync(positionId);
            positionAfterSL.Should().NotBeNull();
            positionAfterSL!.StopLoss.Should().Be(59000m);
            positionAfterSL.StopLossHistories.Should().HaveCount(1);
            positionAfterSL.StopLossHistories.First().NewPrice.Should().Be(59000m);
        }

        // 4. Set Multi Take Profit targets
        using (var context = CreateDbContext())
        {
            var positionRepo = new PositionRepository(context);
            var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);
            var tpManager = new TakeProfitManager(positionRepo, exchangeGateway, instrumentRules, unitOfWork, NullLogger<TakeProfitManager>.Instance);
            var targetsInput = new List<(decimal Price, decimal Percentage)>
            {
                (62000m, 50m),
                (63000m, 50m)
            };
            var createdTargets = await tpManager.CreateTakeProfitTargetsAsync(positionId, targetsInput);
            createdTargets.Should().HaveCount(2);
        }

        // Retrieve position to confirm targets are persisted
        string tp1ExchangeOrderId;
        decimal tp1Quantity;
        decimal tp1Price;

        string tp2ExchangeOrderId;
        decimal tp2Quantity;
        decimal tp2Price;

        using (var context = CreateDbContext())
        {
            var positionRepo = new PositionRepository(context);
            var positionAfterTP = await positionRepo.GetByIdAsync(positionId);
            positionAfterTP.Should().NotBeNull();
            positionAfterTP!.Targets.Should().HaveCount(2);
            positionAfterTP.Targets.All(t => t.Status == "Active").Should().BeTrue();

            var tp1 = positionAfterTP.Targets.First(t => t.TargetNumber == 1);
            tp1ExchangeOrderId = tp1.ExchangeOrderId!;
            tp1Quantity = tp1.Quantity;
            tp1Price = tp1.Price;

            var tp2 = positionAfterTP.Targets.First(t => t.TargetNumber == 2);
            tp2ExchangeOrderId = tp2.ExchangeOrderId!;
            tp2Quantity = tp2.Quantity;
            tp2Price = tp2.Price;
        }

        // 5. Trigger TP1 (Process Take Profit Hit)
        using (var context = CreateDbContext())
        {
            var positionRepo = new PositionRepository(context);
            var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);
            var pcManager = new PartialCloseManager(positionRepo, exchangeGateway, instrumentRules, unitOfWork, NullLogger<PartialCloseManager>.Instance);
            var hitResult = await pcManager.ProcessTakeProfitHitAsync(tp1ExchangeOrderId, tp1Quantity, tp1Price);
            hitResult.Should().BeTrue();
        }

        // Retrieve position to confirm partial close state
        using (var context = CreateDbContext())
        {
            var positionRepo = new PositionRepository(context);
            var positionAfterHit = await positionRepo.GetByIdAsync(positionId);
            positionAfterHit.Should().NotBeNull();
            positionAfterHit!.RemainingQuantity.Should().Be(0.005m);
            positionAfterHit.Status.Should().Be(PositionStatus.PartiallyClosed);

            var savedTp1 = positionAfterHit.Targets.First(t => t.TargetNumber == 1);
            savedTp1.Status.Should().Be("Executed");
            savedTp1.ExecutedQuantity.Should().Be(0.005m);
            savedTp1.ExecutedAt.Should().NotBeNull();
        }

        // 6. Trigger TP2 (Process Take Profit Hit)
        using (var context = CreateDbContext())
        {
            var positionRepo = new PositionRepository(context);
            var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);
            var pcManager = new PartialCloseManager(positionRepo, exchangeGateway, instrumentRules, unitOfWork, NullLogger<PartialCloseManager>.Instance);
            var hitResult2 = await pcManager.ProcessTakeProfitHitAsync(tp2ExchangeOrderId, tp2Quantity, tp2Price);
            hitResult2.Should().BeTrue();
        }

        // Retrieve position to confirm closed state
        using (var context = CreateDbContext())
        {
            var positionRepo = new PositionRepository(context);
            var positionAfterClose = await positionRepo.GetByIdAsync(positionId);
            positionAfterClose.Should().NotBeNull();
            positionAfterClose!.RemainingQuantity.Should().Be(0m);
            positionAfterClose.Status.Should().Be(PositionStatus.Closed);

            var savedTp2 = positionAfterClose.Targets.First(t => t.TargetNumber == 2);
            savedTp2.Status.Should().Be("Executed");
            savedTp2.ExecutedQuantity.Should().Be(0.005m);
        }
    }
}
