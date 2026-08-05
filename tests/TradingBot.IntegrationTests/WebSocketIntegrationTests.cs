using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Streams;
using TradingBot.Application.Models.Events;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using Symbol = TradingBot.Domain.ValueObjects.Symbol;
using TradingBot.Persistence.Context;
using Xunit;

namespace TradingBot.IntegrationTests;

public class WebSocketIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly WebApplicationFactory<TradingBot.Worker.Program> _factory;

    public WebSocketIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Register FakeWebSocketClient which leverages real DI-registered streams
                services.AddSingleton<IExchangeStreamClient>(sp =>
                    new FakeWebSocketClient(
                        sp.GetRequiredService<IMarketStream>(),
                        sp.GetRequiredService<IOrderStream>(),
                        sp.GetRequiredService<IPositionStream>()
                    ));
            });
        });
    }

    [Fact]
    public async Task AppStartup_ShouldReceiveWebSocketEvent_AndSyncOrderStateToDatabase()
    {
        // Seed an order in the database
        var order = new Order(
            clientOrderId: "BOT-SYNC-999",
            symbol: new Symbol("BTCUSDT"),
            side: OrderSide.Buy,
            type: OrderType.Limit,
            quantity: new Quantity(0.05m),
            price: new Money(42000m)
        );

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
            dbContext.Database.EnsureCreated();

            // Clear any old record
            var existing = await dbContext.Orders.FirstOrDefaultAsync(o => o.ClientOrderId == "BOT-SYNC-999");
            if (existing != null)
            {
                dbContext.Orders.Remove(existing);
                await dbContext.SaveChangesAsync();
            }

            // Setup state: Created -> Submitted
            order.Submit();
            await dbContext.Orders.AddAsync(order);
            await dbContext.SaveChangesAsync();
        }

        // Retrieve stream client & trigger a simulated order filled event
        using (var scope = _factory.Services.CreateScope())
        {
            var orderStream = scope.ServiceProvider.GetRequiredService<IOrderStream>();

            // Cast or simulate order status update
            var pushMethod = orderStream.GetType().GetMethod("Push");
            if (pushMethod != null)
            {
                var filledEvent = new OrderUpdateEvent(
                    ClientOrderId: "BOT-SYNC-999",
                    ExchangeOrderId: "EXCH-999",
                    Symbol: "BTCUSDT",
                    Status: OrderStatus.Filled,
                    Price: 42000m,
                    Quantity: 0.05m,
                    FilledQuantity: 0.05m,
                    RejectReason: null,
                    Timestamp: DateTime.UtcNow
                );

                pushMethod.Invoke(orderStream, new object[] { filledEvent });
            }
        }

        // Wait briefly for background service processing
        await Task.Delay(2000);

        // Verify state is successfully transitioned to Filled in DB!
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
            var retrievedOrder = await dbContext.Orders.FirstOrDefaultAsync(o => o.ClientOrderId == "BOT-SYNC-999");

            retrievedOrder.Should().NotBeNull();
            retrievedOrder!.Status.Should().Be(OrderStatus.Filled);
        }
    }
}
