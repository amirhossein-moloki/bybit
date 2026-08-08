using System;
using System.Collections.Generic;
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
using TradingBot.Application.Trading.Execution.Enums;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Enums;
using TradingBot.Exchange.Bybit;
using TradingBot.Exchange.Bybit.Dtos;
using TradingBot.Exchange.Bybit.Services;
using Xunit;

namespace TradingBot.UnitTests.Execution;

public class BybitExecutionAdapterTests
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly BybitSettings _settings;
    private readonly IResilienceService _resilienceService;
    private readonly Mock<ILogger<BybitExecutionAdapter>> _loggerMock;

    public BybitExecutionAdapterTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);

        _settings = new BybitSettings
        {
            ApiKey = "test_api_key",
            ApiSecret = "test_api_secret",
            Environment = "Testnet",
            RecvWindow = 5000
        };

        _resilienceService = new FakeResilienceService();
        _loggerMock = new Mock<ILogger<BybitExecutionAdapter>>();
    }

    private BybitExecutionAdapter CreateAdapter()
    {
        return new BybitExecutionAdapter(_httpClient, _settings, _resilienceService, _loggerMock.Object);
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

    #region Configuration Tests

    [Theory]
    [InlineData("Testnet", "https://api-testnet.bybit.com/")]
    [InlineData("Production", "https://api.bybit.com/")]
    [InlineData("testnet", "https://api-testnet.bybit.com/")]
    [InlineData("production", "https://api.bybit.com/")]
    [InlineData("RandomEnv", "https://api-testnet.bybit.com/")] // Invalid or unhandled defaults to Testnet
    public void Configuration_ShouldResolveCorrectBaseUrl(string environment, string expectedUrl)
    {
        // Arrange
        _settings.Environment = environment;
        var client = new HttpClient(_httpMessageHandlerMock.Object); // fresh httpClient

        // Act
        var adapter = new BybitExecutionAdapter(client, _settings, _resilienceService, _loggerMock.Object);

        // Assert
        client.BaseAddress.Should().NotBeNull();
        client.BaseAddress!.ToString().Should().Be(expectedUrl);
    }

    #endregion

    #region Signature Validation Tests

    [Fact]
    public void BybitSignatureGenerator_ShouldProduceDeterministicSignature()
    {
        // Arrange
        var apiSecret = "test_secret";
        var apiKey = "test_key";
        var timestamp = "1688639403423";
        var recvWindow = "5000";
        var payload = "{\"category\":\"linear\",\"symbol\":\"BTCUSDT\"}";

        // Act
        var signature = BybitSignatureGenerator.GenerateSignature(apiSecret, apiKey, timestamp, recvWindow, payload);

        // Assert
        // Generate locally and compare
        var expectedRawData = $"{timestamp}{apiKey}{recvWindow}{payload}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(apiSecret));
        var signatureBytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(expectedRawData));
        var expectedSignature = BitConverter.ToString(signatureBytes).Replace("-", "").ToLower();

        signature.Should().Be(expectedSignature);
    }

    #endregion

    #region Error Mapping Tests

    [Theory]
    [InlineData(10001, ExchangeErrorType.InvalidRequest)]
    [InlineData(10017, ExchangeErrorType.InvalidRequest)]
    [InlineData(110043, ExchangeErrorType.InvalidRequest)]
    [InlineData(10003, ExchangeErrorType.AuthenticationFailed)]
    [InlineData(10004, ExchangeErrorType.AuthenticationFailed)]
    [InlineData(10005, ExchangeErrorType.AuthenticationFailed)]
    [InlineData(10018, ExchangeErrorType.RateLimited)]
    [InlineData(33004, ExchangeErrorType.RateLimited)]
    [InlineData(110004, ExchangeErrorType.InsufficientBalance)]
    [InlineData(110007, ExchangeErrorType.InsufficientBalance)]
    [InlineData(10016, ExchangeErrorType.Unavailable)]
    [InlineData(3100000, ExchangeErrorType.Unavailable)]
    [InlineData(999999, ExchangeErrorType.Unknown)] // Unmapped error
    public void ErrorMapping_ShouldMapCorrectly(int retCode, ExchangeErrorType expectedErrorType)
    {
        // Act
        var mapped = BybitExecutionAdapter.MapBybitErrorCode(retCode);

        // Assert
        mapped.Should().Be(expectedErrorType);
    }

    #endregion

    #region Status Mapping Tests

    [Theory]
    [InlineData("Created", OrderStatus.Created)]
    [InlineData("Submitted", OrderStatus.Submitted)]
    [InlineData("New", OrderStatus.New)]
    [InlineData("PartiallyFilled", OrderStatus.PartiallyFilled)]
    [InlineData("Filled", OrderStatus.Filled)]
    [InlineData("Cancelled", OrderStatus.Cancelled)]
    [InlineData("Rejected", OrderStatus.Rejected)]
    [InlineData("Failed", OrderStatus.Failed)]
    [InlineData("Pending", OrderStatus.Pending)]
    [InlineData("Triggered", OrderStatus.Pending)]
    [InlineData("Deactivated", OrderStatus.Cancelled)]
    [InlineData("UNKNOWN_STATE_X", OrderStatus.Unknown)] // Unknown
    [InlineData("", OrderStatus.Unknown)]
    [InlineData(null, OrderStatus.Unknown)]
    public void StatusMapping_ShouldMapCorrectly(string? status, OrderStatus expectedStatus)
    {
        // Act
        var mapped = BybitExecutionAdapter.MapBybitStatus(status);

        // Assert
        mapped.Should().Be(expectedStatus);
    }

    #endregion

    #region Request Mapping Tests

    [Fact]
    public async Task CreateOrderAsync_MarketOrder_ShouldPrepareCorrectPayload()
    {
        // Arrange
        var request = new OrderRequest
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 0.05m,
            ClientOrderId = "BOT-12345"
        };

        var mockResponse = new BybitResponse<BybitOrderResult>
        {
            RetCode = 0,
            RetMsg = "OK",
            Result = new BybitOrderResult
            {
                OrderId = "exchange-order-id-111",
                OrderLinkId = "BOT-12345"
            }
        };

        SetupMockResponse(HttpStatusCode.OK, JsonSerializer.Serialize(mockResponse));
        var adapter = CreateAdapter();

        // Act
        var result = await adapter.CreateOrderAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExchangeOrderId.Should().Be("exchange-order-id-111");
        result.Status.Should().Be(OrderStatus.New);
    }

    [Fact]
    public async Task CreateOrderAsync_LimitOrder_ShouldPrepareCorrectPayload()
    {
        // Arrange
        var request = new OrderRequest
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Sell,
            Type = OrderType.Limit,
            Quantity = 0.01m,
            Price = 65000m,
            ClientOrderId = "BOT-54321"
        };

        var mockResponse = new BybitResponse<BybitOrderResult>
        {
            RetCode = 0,
            RetMsg = "OK",
            Result = new BybitOrderResult
            {
                OrderId = "exchange-order-id-222",
                OrderLinkId = "BOT-54321"
            }
        };

        SetupMockResponse(HttpStatusCode.OK, JsonSerializer.Serialize(mockResponse));
        var adapter = CreateAdapter();

        // Act
        var result = await adapter.CreateOrderAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExchangeOrderId.Should().Be("exchange-order-id-222");
        result.Status.Should().Be(OrderStatus.New);
    }

    #endregion

    #region Response Mapping Tests

    [Fact]
    public async Task CreateOrderAsync_RejectedResponse_ShouldMapFailureAndErrorCode()
    {
        // Arrange
        var request = new OrderRequest
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 0.05m,
            ClientOrderId = "BOT-123"
        };

        var mockResponse = new BybitResponse<BybitOrderResult>
        {
            RetCode = 110004, // Insufficient balance
            RetMsg = "Insufficient Balance"
        };

        SetupMockResponse(HttpStatusCode.OK, JsonSerializer.Serialize(mockResponse));
        var adapter = CreateAdapter();

        // Act
        var result = await adapter.CreateOrderAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Status.Should().Be(OrderStatus.Rejected);
        result.ErrorMessage.Should().Be("Insufficient Balance");
        result.ErrorCode.Should().Be("110004");
        result.ErrorType.Should().Be(ExchangeErrorType.InsufficientBalance);
    }

    #endregion
}
