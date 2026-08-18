using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using Symbol = TradingBot.Domain.ValueObjects.Symbol;
using TradingBot.Application.Interfaces;
using TradingBot.Exchange.Bybit;
using TradingBot.Exchange.Bybit.Dtos;
using TradingBot.Exchange.Bybit.Exceptions;
using TradingBot.Infrastructure.Configuration;
using Xunit;

namespace TradingBot.UnitTests;

public class FakeResilienceService : IResilienceService
{
    public Task<T> ExecuteHttpAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        return action(cancellationToken);
    }

    public Task<T> ExecuteHttpAsync<T>(Func<CancellationToken, Task<T>> action, Func<Exception, bool>? isRetryable, CancellationToken cancellationToken = default)
    {
        return action(cancellationToken);
    }

    public Task ExecuteWebSocketAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        return action(cancellationToken);
    }
}

public class BybitClientTests
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly BybitSettings _settings;
    private readonly FakeResilienceService _resilienceService;
    private readonly Mock<ILogger<BybitExchangeClient>> _loggerMock;

    public BybitClientTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://api-testnet.bybit.com")
        };

        _settings = new BybitSettings
        {
            ApiKey = "test_api_key",
            ApiSecret = "test_api_secret",
            UseSandbox = true
        };

        _resilienceService = new FakeResilienceService();
        _loggerMock = new Mock<ILogger<BybitExchangeClient>>();
    }

    private BybitExchangeClient CreateClient()
    {
        return new BybitExchangeClient(_httpClient, _settings, _resilienceService, _loggerMock.Object);
    }

    private void SetupMockResponse(HttpStatusCode statusCode, string content)
    {
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content)
            });
    }

    [Fact]
    public async Task PingAsync_ShouldReturnTrue_WhenServerTimeReturnedWithSuccess()
    {
        // Arrange
        var mockResponse = new BybitResponse<BybitServerTime>
        {
            RetCode = 0,
            RetMsg = "OK",
            Result = new BybitServerTime { TimeSecond = "1688639403", TimeNano = "1688639403423213947" }
        };
        SetupMockResponse(HttpStatusCode.OK, JsonSerializer.Serialize(mockResponse));

        var client = CreateClient();

        // Act
        var result = await client.PingAsync(CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task PingAsync_ShouldReturnFalse_WhenServerReturnsErrorCode()
    {
        // Arrange
        var mockResponse = new BybitResponse<BybitServerTime>
        {
            RetCode = 10001,
            RetMsg = "Error",
            Result = null
        };
        SetupMockResponse(HttpStatusCode.OK, JsonSerializer.Serialize(mockResponse));

        var client = CreateClient();

        // Act
        var result = await client.PingAsync(CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetAccountBalanceAsync_ShouldReturnCorrectBalance_WhenCoinExistsInUnifiedAccount()
    {
        // Arrange
        var coinBalance = new BybitCoinBalance
        {
            CoinName = "USDT",
            WalletBalance = "1050.25",
            AvailableToWithdraw = "1050.25"
        };
        var accountBalance = new BybitAccountBalance
        {
            AccountType = "UNIFIED",
            Coin = new() { coinBalance }
        };
        var mockResponse = new BybitResponse<BybitWalletBalanceResponse>
        {
            RetCode = 0,
            RetMsg = "OK",
            Result = new BybitWalletBalanceResponse { List = new() { accountBalance } }
        };
        SetupMockResponse(HttpStatusCode.OK, JsonSerializer.Serialize(mockResponse));

        var client = CreateClient();

        // Act
        var balance = await client.GetAccountBalanceAsync("USDT", CancellationToken.None);

        // Assert
        balance.Should().Be(1050.25m);
    }

    [Fact]
    public async Task GetAccountBalanceAsync_ShouldReturnZero_WhenCoinDoesNotExistInResponse()
    {
        // Arrange
        var coinBalance = new BybitCoinBalance
        {
            CoinName = "BTC",
            WalletBalance = "0.5",
            AvailableToWithdraw = "0.5"
        };
        var accountBalance = new BybitAccountBalance
        {
            AccountType = "UNIFIED",
            Coin = new() { coinBalance }
        };
        var mockResponse = new BybitResponse<BybitWalletBalanceResponse>
        {
            RetCode = 0,
            RetMsg = "OK",
            Result = new BybitWalletBalanceResponse { List = new() { accountBalance } }
        };
        SetupMockResponse(HttpStatusCode.OK, JsonSerializer.Serialize(mockResponse));

        var client = CreateClient();

        // Act
        var balance = await client.GetAccountBalanceAsync("USDT", CancellationToken.None);

        // Assert
        balance.Should().Be(0m);
    }

    [Fact]
    public async Task IsSymbolValidAsync_ShouldReturnTrue_WhenSymbolIsTrading()
    {
        // Arrange
        var instrument = new BybitInstrumentInfo
        {
            Symbol = "BTCUSDT",
            Status = "Trading"
        };
        var mockResponse = new BybitResponse<BybitInstrumentsResponse>
        {
            RetCode = 0,
            RetMsg = "OK",
            Result = new BybitInstrumentsResponse { List = new() { instrument } }
        };
        SetupMockResponse(HttpStatusCode.OK, JsonSerializer.Serialize(mockResponse));

        var client = CreateClient();

        // Act
        var isValid = await client.IsSymbolValidAsync("BTCUSDT", CancellationToken.None);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldThrowExchangeException_WhenRetCodeIsNonZero()
    {
        // Arrange
        var mockResponse = new BybitResponse<BybitOrderResult>
        {
            RetCode = 10004,
            RetMsg = "Signature for this request is not valid."
        };
        SetupMockResponse(HttpStatusCode.OK, JsonSerializer.Serialize(mockResponse));

        var client = CreateClient();
        var order = new Order("test-link-id", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.01m), new Money(28000m));

        // Act
        Func<Task> act = async () => await client.PlaceOrderAsync(order, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ExchangeException>()
            .WithMessage("*Signature for this request is not valid.*");
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldReturnNewOrder_WhenResponseIsSuccessful()
    {
        // Arrange
        var mockResponse = new BybitResponse<BybitOrderResult>
        {
            RetCode = 0,
            RetMsg = "OK",
            Result = new BybitOrderResult
            {
                OrderId = "1321003749386327552",
                OrderLinkId = "test-link-id"
            }
        };
        SetupMockResponse(HttpStatusCode.OK, JsonSerializer.Serialize(mockResponse));

        var client = CreateClient();
        var order = new Order("test-link-id", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.01m), new Money(28000m));

        // Act
        var result = await client.PlaceOrderAsync(order, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ClientOrderId.Should().Be("test-link-id");
        result.Status.Should().Be(OrderStatus.Accepted);
        result.ExchangeOrderId.Should().Be("1321003749386327552");
    }

    [Fact]
    public void BybitSettings_ProxyUrl_ShouldDefaultToEmpty_AndCanBeConfigured()
    {
        // Arrange & Act
        var settings = new BybitSettings
        {
            ProxyUrl = "socks5://host.docker.internal:10808"
        };

        // Assert
        settings.ProxyUrl.Should().Be("socks5://host.docker.internal:10808");
    }

    [Fact]
    public void AddBybitExchange_ShouldRegisterServices_WithOrWithoutProxyUrl()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IResilienceService, FakeResilienceService>();

        // Act
        services.AddBybitExchange(options =>
        {
            options.ApiKey = "key";
            options.ApiSecret = "secret";
            options.ProxyUrl = "socks5://host.docker.internal:10808";
        });

        var provider = services.BuildServiceProvider();

        // Assert
        var registeredSettings = provider.GetRequiredService<BybitSettings>();
        registeredSettings.ProxyUrl.Should().Be("socks5://host.docker.internal:10808");

        var exchangeClient = provider.GetService<IExchangeClient>();
        exchangeClient.Should().NotBeNull();
    }
}
