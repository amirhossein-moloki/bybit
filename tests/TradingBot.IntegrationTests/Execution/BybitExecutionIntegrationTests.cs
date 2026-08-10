using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Application.Trading.Execution.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Exchange.Bybit;
using TradingBot.Exchange.Bybit.Dtos;
using Xunit;

namespace TradingBot.IntegrationTests.Execution;

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

public class BybitExecutionIntegrationTests
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly BybitSettings _settings;
    private readonly Mock<ILogger<BybitExecutionAdapter>> _adapterLoggerMock;
    private readonly Mock<ILogger<TradingExecutionService>> _serviceLoggerMock;

    public BybitExecutionIntegrationTests()
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
            Environment = "Testnet",
            RecvWindow = 5000
        };

        _adapterLoggerMock = new Mock<ILogger<BybitExecutionAdapter>>();
        _serviceLoggerMock = new Mock<ILogger<TradingExecutionService>>();
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
    public async Task E2E_Pipeline_WithMockHttp_ShouldSucceed()
    {
        // Arrange
        var mockResponse = new BybitResponse<BybitOrderResult>
        {
            RetCode = 0,
            RetMsg = "OK",
            Result = new BybitOrderResult
            {
                OrderId = "exchange-order-98765",
                OrderLinkId = "BOT-123"
            }
        };

        SetupMockResponse(HttpStatusCode.OK, JsonSerializer.Serialize(mockResponse));

        var fakeResilience = new FakeResilienceService();
        var adapter = new BybitExecutionAdapter(_httpClient, _settings, fakeResilience, _adapterLoggerMock.Object);

        var validator = new OrderValidator();
        var builder = new OrderBuilder();
        var instrumentRules = new TestExchangeInstrumentRules();

        var service = new TradingExecutionService(validator, builder, adapter, instrumentRules, _serviceLoggerMock.Object);

        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Market,
            Quantity = 0.01m,
            Price = 0m,
            RiskDecision = RiskDecisionStatus.Approved
        };

        // Act
        var executionResult = await service.ExecuteAsync(request);

        // Assert
        executionResult.Should().NotBeNull();
        executionResult.Success.Should().BeTrue();
        executionResult.ExchangeOrderId.Should().Be("exchange-order-98765");
        executionResult.Status.Should().Be(OrderStatus.New);
    }

    [Fact]
    public async Task Real_BybitTestnet_OrderSubmission_GatedByEnvironmentFlag()
    {
        // Check gating flag
        var integrationEnabledEnv = Environment.GetEnvironmentVariable("BYBIT_TESTNET_INTEGRATION");
        var isEnabled = string.Equals(integrationEnabledEnv, "true", StringComparison.OrdinalIgnoreCase);

        if (!isEnabled)
        {
            // Gated out, do not make real network calls
            return;
        }

        // Arrange: Real configuration from environment variables
        var apiKey = Environment.GetEnvironmentVariable("BYBIT_API_KEY") ?? throw new Exception("BYBIT_API_KEY missing for integration test");
        var apiSecret = Environment.GetEnvironmentVariable("BYBIT_SECRET_KEY") ?? throw new Exception("BYBIT_SECRET_KEY missing for integration test");

        var realSettings = new BybitSettings
        {
            ApiKey = apiKey,
            ApiSecret = apiSecret,
            Environment = "Testnet",
            RecvWindow = 5000
        };

        using var realHttpClient = new HttpClient();
        var realResilience = new FakeResilienceService();
        var realAdapterLogger = new Mock<ILogger<BybitExecutionAdapter>>().Object;

        var adapter = new BybitExecutionAdapter(realHttpClient, realSettings, realResilience, realAdapterLogger);

        var request = new OrderRequest
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 0.01m, // Safe quantity
            ClientOrderId = "BOT-INTEG-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // Act & Assert 1: Create Order
        var createResult = await adapter.CreateOrderAsync(request, CancellationToken.None);
        createResult.Should().NotBeNull();
        createResult.Success.Should().BeTrue($"Create order failed: {createResult.ErrorMessage} (Code: {createResult.ErrorCode})");
        createResult.ExchangeOrderId.Should().NotBeNullOrEmpty();

        var exchangeOrderId = createResult.ExchangeOrderId!;

        // Act & Assert 2: Query Order
        var queryResult = await adapter.GetOrderAsync(exchangeOrderId, "BTCUSDT", CancellationToken.None);
        queryResult.Should().NotBeNull();
        queryResult.Success.Should().BeTrue($"Query order failed: {queryResult.ErrorMessage}");
        queryResult.ExchangeOrderId.Should().Be(exchangeOrderId);
    }
}
