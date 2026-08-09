using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Repositories;
using TradingBot.Application.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using TradingBot.Infrastructure.Configuration;
using Xunit;

namespace TradingBot.UnitTests;

public class WorkflowAndDITests
{
    [Fact]
    public async Task SignalProcessor_ShouldSaveAndPlaceOrder_WhenValidSignalProcessed()
    {
        // Arrange
        var mockSignalRepo = new Mock<ISignalRepository>();
        var mockOrderRepo = new Mock<IOrderRepository>();
        var mockExchangeClient = new Mock<IExchangeClient>();
        var mockLogger = new Mock<ILogger<SignalProcessor>>();

        mockExchangeClient.Setup(x => x.ExchangeName).Returns("Bybit");

        // Mock PlaceOrderAsync to return an order with filled status
        mockExchangeClient
            .Setup(x => x.PlaceOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order o, CancellationToken ct) =>
            {
                var placed = new Order(o.ClientOrderId, o.Symbol, o.Side, o.Type, o.Quantity, o.Price);
                placed.Submit();
                placed.Accept("EXCHANGE-ORDER-ID");
                placed.MarkFilled();
                return placed;
            });

        var processor = new SignalProcessor(
            mockSignalRepo.Object,
            mockOrderRepo.Object,
            mockExchangeClient.Object,
            mockLogger.Object
        );

        var signal = new Signal("SOLUSDT", SignalType.Buy, 110.50m, 2.0m);

        // Act
        await processor.ProcessSignalAsync(signal, CancellationToken.None);

        // Assert
        mockSignalRepo.Verify(x => x.SaveAsync(signal, It.IsAny<CancellationToken>()), Times.Once);
        mockOrderRepo.Verify(x => x.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Once);
        mockOrderRepo.Verify(x => x.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Once);
        mockExchangeClient.Verify(x => x.PlaceOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ServiceRegistration_ShouldResolveRegisteredServices_WhenDIContainerBuilt()
    {
        // Arrange
        var services = new ServiceCollection();

        // Build mock configuration
        var myConfiguration = new Dictionary<string, string>
        {
            {"Application:Environment", "Development"},
            {"Application:BotName", "TestBot"},
            {"Database:ConnectionString", "Host=localhost;Database=testdb"},
            {"Exchange:SelectedExchange", "Bybit"},
            {"Exchange:ApiKey", "test-key"},
            {"Exchange:ApiSecret", "test-secret"},
            {"Logging:LogLevel", "Information"},
            {"Security:EncryptionKey", "12345678123456781234567812345678"},
            {"Notification:Telegram:ChatId", "test-chat-id"}
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(myConfiguration!)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        // Act
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddBybitExchange(options =>
        {
            options.ApiKey = "test-key";
            options.ApiSecret = "test-secret";
            options.UseSandbox = true;
        });

        var provider = services.BuildServiceProvider();

        // Assert
        var signalProcessor = provider.GetService<ISignalProcessor>();
        var exchangeClient = provider.GetService<IExchangeClient>();
        var signalRepo = provider.GetService<ISignalRepository>();
        var settings = provider.GetService<TradingBotSettings>();

        signalProcessor.Should().NotBeNull();
        exchangeClient.Should().NotBeNull();
        signalRepo.Should().NotBeNull();
        settings.Should().NotBeNull();

        settings!.Application.BotName.Should().Be("TestBot");
        settings.Database.ConnectionString.Should().Be("Host=localhost;Database=testdb");
        settings.Exchange.ApiKey.Should().Be("test-key");
    }

    [Fact]
    public async Task OrderService_ShouldAtomicallyCreateOrderAndSaveOnExchange()
    {
        // Arrange
        var mockOrderRepo = new Mock<IOrderRepository>();
        var mockExchangeClient = new Mock<IExchangeClient>();
        var mockUnitOfWork = new Mock<IUnitOfWork>();
        var mockLogger = new Mock<ILogger<OrderService>>();

        mockExchangeClient.Setup(x => x.ExchangeName).Returns("Bybit");
        mockExchangeClient
            .Setup(x => x.PlaceOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order o, CancellationToken ct) =>
            {
                var placed = new Order(o.ClientOrderId, o.Symbol, o.Side, o.Type, o.Quantity, o.Price);
                placed.Submit();
                placed.Accept("EXCHANGE-111");
                return placed;
            });

        var orderService = new OrderService(
            mockOrderRepo.Object,
            mockExchangeClient.Object,
            mockUnitOfWork.Object,
            mockLogger.Object
        );

        // Act
        var order = await orderService.CreateOrderAsync("BTCUSDT", OrderSide.Buy, OrderType.Limit, 0.05m, 42000m);

        // Assert
        order.Should().NotBeNull();
        order.Symbol.Value.Should().Be("BTCUSDT");
        order.Status.Should().Be(OrderStatus.Accepted);
        order.ExchangeOrderId.Should().Be("EXCHANGE-111");

        mockUnitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockUnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockOrderRepo.Verify(r => r.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Once);
        mockOrderRepo.Verify(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task OrderService_ShouldRollbackAndReject_WhenExchangeFails()
    {
        // Arrange
        var mockOrderRepo = new Mock<IOrderRepository>();
        var mockExchangeClient = new Mock<IExchangeClient>();
        var mockUnitOfWork = new Mock<IUnitOfWork>();
        var mockLogger = new Mock<ILogger<OrderService>>();

        mockExchangeClient.Setup(x => x.ExchangeName).Returns("Bybit");
        mockExchangeClient
            .Setup(x => x.PlaceOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Exchange down"));

        var orderService = new OrderService(
            mockOrderRepo.Object,
            mockExchangeClient.Object,
            mockUnitOfWork.Object,
            mockLogger.Object
        );

        // Act & Assert
        Func<Task> act = async () => await orderService.CreateOrderAsync("BTCUSDT", OrderSide.Buy, OrderType.Limit, 0.05m, 42000m);
        await act.Should().ThrowAsync<Exception>().WithMessage("Exchange down");

        mockUnitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockUnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockUnitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
