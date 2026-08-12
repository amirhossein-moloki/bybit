using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Streams;
using TradingBot.Persistence.Context;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;
using TradingBot.Worker;

namespace TradingBot.IntegrationTests;

public class FakeExchangeStreamClient : IExchangeStreamClient
{
    public TradingBot.Application.Enums.ConnectionState State => TradingBot.Application.Enums.ConnectionState.Connected;
    public bool IsRecoveryIncomplete => false;

    public IMarketStream MarketStream { get; } = new Mock<IMarketStream>().Object;
    public IOrderStream OrderStream { get; } = new Mock<IOrderStream>().Object;
    public IPositionStream PositionStream { get; } = new Mock<IPositionStream>().Object;

    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class FakeWebSocketClient : IExchangeStreamClient
{
    public TradingBot.Application.Enums.ConnectionState State => TradingBot.Application.Enums.ConnectionState.Connected;
    public bool IsRecoveryIncomplete => false;

    public IMarketStream MarketStream { get; }
    public IOrderStream OrderStream { get; }
    public IPositionStream PositionStream { get; }

    public FakeWebSocketClient(IMarketStream marketStream, IOrderStream orderStream, IPositionStream positionStream)
    {
        MarketStream = marketStream;
        OrderStream = orderStream;
        PositionStream = positionStream;
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public SqliteConnection SqliteConnection { get; }

    public CustomWebApplicationFactory()
    {
        SqliteConnection = new SqliteConnection("DataSource=:memory:");
        SqliteConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            var mockExchangeClient = new Mock<IExchangeClient>();
            mockExchangeClient.Setup(x => x.ExchangeName).Returns("Bybit");
            mockExchangeClient.Setup(x => x.PingAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
            services.AddSingleton(mockExchangeClient.Object);

            services.AddSingleton<IExchangeStreamClient, FakeExchangeStreamClient>();

            var mockTelegramClient = new Mock<ITelegramClient>();
            mockTelegramClient.Setup(x => x.CurrentState).Returns(TelegramConnectionState.Connected);
            services.AddSingleton(mockTelegramClient.Object);

            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<TradingDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<TradingDbContext>(options =>
            {
                options.UseSqlite(SqliteConnection);
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SqliteConnection.Close();
            SqliteConnection.Dispose();
        }
        base.Dispose(disposing);
    }
}
