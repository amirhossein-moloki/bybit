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

    public IMarketStream MarketStream { get; } = new Mock<IMarketStream>().Object;
    public IOrderStream OrderStream { get; } = new Mock<IOrderStream>().Object;
    public IPositionStream PositionStream { get; } = new Mock<IPositionStream>().Object;

    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class FakeWebSocketClient : IExchangeStreamClient
{
    public TradingBot.Application.Enums.ConnectionState State => TradingBot.Application.Enums.ConnectionState.Connected;

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

            // Remove real ITelegramClient registration and substitute with mock for E2E tests
            var tgClientDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITelegramClient));
            if (tgClientDescriptor != null)
            {
                services.Remove(tgClientDescriptor);
            }
            var tgDiscoveryDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITelegramDiscoveryClient));
            if (tgDiscoveryDescriptor != null)
            {
                services.Remove(tgDiscoveryDescriptor);
            }

            var mockTelegramClient = new Mock<ITelegramClient>();
            mockTelegramClient.Setup(x => x.CurrentState).Returns(TelegramConnectionState.Connected);
            mockTelegramClient.Setup(x => x.IsConnected()).Returns(true);
            mockTelegramClient.Setup(x => x.LoginWithQrCodeAsync(It.IsAny<Action<string>>(), It.IsAny<CancellationToken>()))
                .Callback<Action<string>, CancellationToken>((qrDisplay, ct) =>
                {
                    qrDisplay("tg://login?token=test_integration_qr_token");
                })
                .ReturnsAsync(new TL.User { id = 123456, username = "integration_test_user", first_name = "Test", last_name = "User" });
            mockTelegramClient.Setup(x => x.GetConnectedAccount()).Returns(new TelegramAccountDto { Id = 123456, Username = "integration_test_user", FirstName = "Test", LastName = "User" });
            services.AddSingleton(mockTelegramClient.Object);

            var mockDiscoveryClient = new Mock<ITelegramDiscoveryClient>();
            mockDiscoveryClient.Setup(x => x.IsConnected()).Returns(true);
            mockDiscoveryClient.Setup(x => x.GetCurrentState()).Returns("Connected");
            mockDiscoveryClient.Setup(x => x.GetDialogsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new System.Collections.Generic.List<DiscoveredTelegramChatDto>
                {
                    new DiscoveredTelegramChatDto(1001, "Test Discovery Channel", "test_discovery", true, false)
                });
            services.AddSingleton<ITelegramDiscoveryClient>(mockDiscoveryClient.Object);

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
