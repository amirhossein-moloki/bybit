using System;
using System.Collections.Generic;
using System.Linq;
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
using TradingBot.Application.Repositories;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Exchange.Bybit;
using TradingBot.Exchange.Bybit.Dtos;
using TradingBot.Infrastructure.Configuration;
using Xunit;

namespace TradingBot.UnitTests;

public class BybitMultiAccountTests
{
    private readonly Mock<IExchangeAccountRepository> _accountRepositoryMock;
    private readonly Mock<IEncryptionService> _encryptionServiceMock;
    private readonly BybitSettings _settings;

    public BybitMultiAccountTests()
    {
        _accountRepositoryMock = new Mock<IExchangeAccountRepository>();
        _encryptionServiceMock = new Mock<IEncryptionService>();
        _settings = new BybitSettings
        {
            ApiKey = "primary_api_key",
            ApiSecret = "primary_secret",
            Environment = "Testnet"
        };
    }

    [Fact]
    public async Task GetActiveAccountsAsync_ShouldReturnMergedAccounts_FromSettingsAndDb()
    {
        // Arrange
        _settings.Accounts = new List<BybitAccountSettings>
        {
            new BybitAccountSettings
            {
                Name = "ConfigDemo",
                ApiKey = "config_demo_key",
                ApiSecret = "config_demo_secret",
                Environment = "Demo",
                IsActive = true
            }
        };

        var dbAccount = new ExchangeAccount(
            "BYBIT",
            "production",
            "encrypted_db_key",
            "encrypted_db_secret"
        );

        _accountRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExchangeAccount> { dbAccount });

        _encryptionServiceMock
            .Setup(e => e.Decrypt("encrypted_db_key"))
            .Returns("decrypted_db_key");

        _encryptionServiceMock
            .Setup(e => e.Decrypt("encrypted_db_secret"))
            .Returns("decrypted_db_secret");

        var provider = new BybitAccountProvider(_settings, _accountRepositoryMock.Object, _encryptionServiceMock.Object);

        // Act
        var activeAccounts = await provider.GetActiveAccountsAsync(CancellationToken.None);

        // Assert
        activeAccounts.Should().HaveCount(3);

        // Primary
        activeAccounts[0].Name.Should().Be("Default");
        activeAccounts[0].ApiKey.Should().Be("primary_api_key");
        activeAccounts[0].Environment.Should().Be("Testnet");

        // Config account
        activeAccounts[1].Name.Should().Be("ConfigDemo");
        activeAccounts[1].ApiKey.Should().Be("config_demo_key");
        activeAccounts[1].Environment.Should().Be("Demo");

        // DB account
        activeAccounts[2].Name.Should().StartWith("DBAccount");
        activeAccounts[2].ApiKey.Should().Be("decrypted_db_key");
        activeAccounts[2].Environment.Should().Be("production");
    }

    [Fact]
    public async Task ExecutionAdapter_ShouldRouteToDemo_WhenEnvironmentIsDemo()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var httpClient = new HttpClient(mockHandler.Object);

        var demoAccount = new BybitAccountInfo
        {
            Name = "DemoAccount",
            ApiKey = "demo_key",
            ApiSecret = "demo_secret",
            Environment = "Demo"
        };

        var accountProviderMock = new Mock<IBybitAccountProvider>();
        accountProviderMock
            .Setup(p => p.GetActiveAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BybitAccountInfo> { demoAccount });

        // Setup mock response for Bybit Order Creation
        var orderResponse = new BybitResponse<BybitOrderResult>
        {
            RetCode = 0,
            RetMsg = "OK",
            Result = new BybitOrderResult
            {
                OrderId = "demo_order_id_123",
                OrderLinkId = "TB-unique-order"
            }
        };

        HttpRequestMessage? capturedRequest = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(orderResponse))
            });

        var loggerMock = new Mock<ILogger<BybitExecutionAdapter>>();
        var adapter = new BybitExecutionAdapter(httpClient, _settings, new FakeResilienceService(), loggerMock.Object, accountProviderMock.Object);

        var request = new OrderRequest
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 0.05m,
            ClientOrderId = "TB-unique-order"
        };

        // Act
        var result = await adapter.CreateOrderAsync(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.ExchangeOrderId.Should().Be("demo_order_id_123");

        capturedRequest.Should().NotBeNull();
        // Verify URI starts with demo endpoint!
        capturedRequest!.RequestUri!.AbsoluteUri.Should().StartWith("https://api-demo.bybit.com");
    }
}
