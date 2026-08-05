using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using Symbol = TradingBot.Domain.ValueObjects.Symbol;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;

namespace TradingBot.IntegrationTests;

public class DatabasePersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _postgresContainer;
    private SqliteConnection? _sqliteConnection;
    private bool _useSqlite = false;

    public DatabasePersistenceTests()
    {
        try
        {
            _postgresContainer = new PostgreSqlBuilder()
                .WithImage("postgres:15-alpine")
                .Build();
        }
        catch
        {
            _useSqlite = true;
        }
    }

    public async Task InitializeAsync()
    {
        if (!_useSqlite && _postgresContainer != null)
        {
            try
            {
                await _postgresContainer.StartAsync();
            }
            catch
            {
                _useSqlite = true;
            }
        }

        if (_useSqlite)
        {
            _sqliteConnection = new SqliteConnection("DataSource=:memory:");
            await _sqliteConnection.OpenAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgresContainer != null)
        {
            try
            {
                await _postgresContainer.DisposeAsync();
            }
            catch
            {
                // Ignore
            }
        }

        if (_sqliteConnection != null)
        {
            try
            {
                await _sqliteConnection.CloseAsync();
                await _sqliteConnection.DisposeAsync();
            }
            catch
            {
                // Ignore
            }
        }
    }

    private TradingDbContext CreateDbContext()
    {
        DbContextOptions<TradingDbContext> options;

        if (_useSqlite)
        {
            options = new DbContextOptionsBuilder<TradingDbContext>()
                .UseSqlite(_sqliteConnection!)
                .Options;
        }
        else
        {
            options = new DbContextOptionsBuilder<TradingDbContext>()
                .UseNpgsql(_postgresContainer!.GetConnectionString())
                .Options;
        }

        var context = new TradingDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task PersistAndRetrieveOrder_ShouldSucceed_WhenValidOrderSaved()
    {
        // Arrange
        using var context = CreateDbContext();
        var orderRepository = new OrderRepository(context);

        var order = new Order(
            clientOrderId: "INT-12345",
            symbol: new Symbol("BTCUSDT"),
            side: OrderSide.Buy,
            type: OrderType.Limit,
            quantity: new Quantity(0.025m),
            price: new Money(43500m)
        );

        // Act - Save
        await orderRepository.AddAsync(order, CancellationToken.None);
        await context.SaveChangesAsync();

        // Act - Retrieve
        using var context2 = CreateDbContext();
        var orderRepository2 = new OrderRepository(context2);
        var retrievedOrder = await orderRepository2.GetByIdAsync(order.Id, CancellationToken.None);

        // Assert
        retrievedOrder.Should().NotBeNull();
        retrievedOrder!.Id.Should().Be(order.Id);
        retrievedOrder.ClientOrderId.Should().Be("INT-12345");
        retrievedOrder.Symbol.Value.Should().Be("BTCUSDT");
        retrievedOrder.Side.Should().Be(OrderSide.Buy);
        retrievedOrder.Type.Should().Be(OrderType.Limit);
        retrievedOrder.Quantity.Value.Should().Be(0.025m);
        retrievedOrder.Price.Amount.Should().Be(43500m);
        retrievedOrder.Status.Should().Be(OrderStatus.Created);
    }

    [Fact]
    public async Task UpdateOrderStatus_ShouldSucceed_AndPersistToDatabase()
    {
        // Arrange
        using var context = CreateDbContext();
        var orderRepository = new OrderRepository(context);

        var order = new Order(
            clientOrderId: "INT-22222",
            symbol: new Symbol("ETHUSDT"),
            side: OrderSide.Sell,
            type: OrderType.Limit,
            quantity: new Quantity(1.5m),
            price: new Money(3120m)
        );

        await orderRepository.AddAsync(order, CancellationToken.None);
        await context.SaveChangesAsync();

        // Act - Transition Order and Update in DB
        order.Submit();
        order.Accept("EXCHANGE-ORDER-777");
        order.MarkFilled();

        await orderRepository.UpdateAsync(order, CancellationToken.None);
        await context.SaveChangesAsync();

        // Act - Retrieve and Verify Status
        using var context2 = CreateDbContext();
        var orderRepository2 = new OrderRepository(context2);
        var retrievedOrder = await orderRepository2.GetByIdAsync(order.Id, CancellationToken.None);

        // Assert
        retrievedOrder.Should().NotBeNull();
        retrievedOrder!.Status.Should().Be(OrderStatus.Filled);
        retrievedOrder.ExchangeOrderId.Should().Be("EXCHANGE-ORDER-777");
    }

    [Fact]
    public async Task TransactionRollback_ShouldRevertDatabaseChanges_WhenExceptionOccurs()
    {
        // Arrange
        using var context = CreateDbContext();
        var orderRepository = new OrderRepository(context);
        var unitOfWork = new UnitOfWork(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<UnitOfWork>.Instance);

        var order = new Order(
            clientOrderId: "INT-33333",
            symbol: new Symbol("SOLUSDT"),
            side: OrderSide.Buy,
            type: OrderType.Limit,
            quantity: new Quantity(10m),
            price: new Money(120m)
        );

        // Act - Begin Transaction and add order
        await unitOfWork.BeginTransactionAsync(CancellationToken.None);
        await orderRepository.AddAsync(order, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Simulate failure and rollback
        await unitOfWork.RollbackTransactionAsync(CancellationToken.None);

        // Assert - Order should not exist in database
        using var context2 = CreateDbContext();
        var retrievedOrder = await context2.Orders.FindAsync(order.Id);
        retrievedOrder.Should().BeNull();
    }

    [Fact]
    public async Task PersistTradeHistory_ShouldSucceed_WhenTradeIsSaved()
    {
        // Arrange
        using var context = CreateDbContext();
        var tradeRepository = new TradeRepository(context);

        var trade = new Trade(
            tradeId: "TX-9999",
            orderId: "INT-12345",
            symbol: "BTCUSDT",
            side: SignalType.Buy,
            price: 43500m,
            quantity: 0.025m,
            fee: 0.000025m,
            feeAsset: "BTC"
        );

        // Act - Save
        await tradeRepository.SaveAsync(trade, CancellationToken.None);
        await context.SaveChangesAsync();

        // Act - Retrieve
        using var context2 = CreateDbContext();
        var tradeRepository2 = new TradeRepository(context2);
        var retrievedTrade = await tradeRepository2.GetByIdAsync(trade.Id, CancellationToken.None);

        // Assert
        retrievedTrade.Should().NotBeNull();
        retrievedTrade!.Id.Should().Be(trade.Id);
        retrievedTrade.TradeId.Should().Be("TX-9999");
        retrievedTrade.OrderId.Should().Be("INT-12345");
        retrievedTrade.Symbol.Should().Be("BTCUSDT");
        retrievedTrade.Price.Should().Be(43500m);
        retrievedTrade.Quantity.Should().Be(0.025m);
        retrievedTrade.Fee.Should().Be(0.000025m);
        retrievedTrade.FeeAsset.Should().Be("BTC");
    }

    [Fact]
    public async Task Database_ShouldThrowException_WhenOrderQuantityIsZeroOrNegative()
    {
        // Arrange
        using var context = CreateDbContext();
        var orderRepository = new OrderRepository(context);

        // Act & Assert
        // We bypass Domain rules (which already block <= 0 quantity) by writing directly using reflection, or
        // by creating an Order and altering its private backing field / using EF shadow or reflection.
        var order = new Order(
            clientOrderId: "INT-INVALID-QTY",
            symbol: new Symbol("BTCUSDT"),
            side: OrderSide.Buy,
            type: OrderType.Limit,
            quantity: new Quantity(1m), // initially valid
            price: new Money(40000m)
        );

        // Modify backing value to violate check constraint
        var quantityField = typeof(Quantity).GetField("<Value>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        quantityField?.SetValue(order.Quantity, -0.5m);

        await orderRepository.AddAsync(order, CancellationToken.None);

        Func<Task> saveAct = async () => await context.SaveChangesAsync();

        // Should throw DbUpdateException due to check constraint CK_Orders_Quantity
        await saveAct.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Database_ShouldThrowException_WhenOrderPriceIsNegative()
    {
        // Arrange
        using var context = CreateDbContext();
        var orderRepository = new OrderRepository(context);

        var order = new Order(
            clientOrderId: "INT-INVALID-PRICE",
            symbol: new Symbol("BTCUSDT"),
            side: OrderSide.Buy,
            type: OrderType.Limit,
            quantity: new Quantity(0.1m),
            price: new Money(40000m)
        );

        // Modify backing value to violate check constraint
        var priceField = typeof(Money).GetField("<Amount>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        priceField?.SetValue(order.Price, -500m);

        await orderRepository.AddAsync(order, CancellationToken.None);

        Func<Task> saveAct = async () => await context.SaveChangesAsync();

        // Should throw DbUpdateException due to check constraint CK_Orders_Price
        await saveAct.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Database_ShouldThrowException_WhenOptimisticConcurrencyConflictOccurs()
    {
        // Arrange
        // Create and save an initial order
        var initialId = Guid.NewGuid();
        {
            using var initContext = CreateDbContext();
            var initRepo = new OrderRepository(initContext);
            var order = new Order(
                clientOrderId: "CONC-111",
                symbol: new Symbol("BTCUSDT"),
                side: OrderSide.Buy,
                type: OrderType.Limit,
                quantity: new Quantity(1m),
                price: new Money(40000m)
            );
            // set id using reflection so we know it
            typeof(Order).GetProperty("Id")?.SetValue(order, initialId);

            await initRepo.AddAsync(order);
            await initContext.SaveChangesAsync();
        }

        // Act - Simulate concurrency by loading the same entity in two separate contexts
        using var context1 = CreateDbContext();
        using var context2 = CreateDbContext();

        var repo1 = new OrderRepository(context1);
        var repo2 = new OrderRepository(context2);

        var order1 = await repo1.GetByIdAsync(initialId);
        var order2 = await repo2.GetByIdAsync(initialId);

        order1.Should().NotBeNull();
        order2.Should().NotBeNull();

        // Update 1 and Save (this succeeds and advances the UpdatedAt timestamp / concurrency token)
        order1!.Submit();
        await repo1.UpdateAsync(order1);
        await context1.SaveChangesAsync();

        // Update 2 and Save (this should fail because context2 still has the old UpdatedAt timestamp)
        order2!.Submit();
        await repo2.UpdateAsync(order2);

        Func<Task> concurrentSaveAct = async () => await context2.SaveChangesAsync();

        // Assert - Expecting DbUpdateConcurrencyException
        await concurrentSaveAct.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Database_ShouldSupportLargeCollectionPagination()
    {
        // Arrange
        using var context = CreateDbContext();
        var repo = new OrderRepository(context);

        // Seed some unique orders
        for (int i = 0; i < 15; i++)
        {
            var order = new Order(
                clientOrderId: $"PAG-{Guid.NewGuid():N}",
                symbol: new Symbol("BTCUSDT"),
                side: OrderSide.Buy,
                type: OrderType.Limit,
                quantity: new Quantity(0.01m),
                price: new Money(40000m)
            );
            await repo.AddAsync(order);
        }
        await context.SaveChangesAsync();

        // Act
        var pagedResult = await repo.GetPagedOrdersAsync(pageNumber: 2, pageSize: 5);

        // Assert
        pagedResult.Should().NotBeNull();
        pagedResult.PageNumber.Should().Be(2);
        pagedResult.PageSize.Should().Be(5);
        pagedResult.TotalCount.Should().BeGreaterThanOrEqualTo(15);
        pagedResult.Items.Should().HaveCount(5);
    }
}
