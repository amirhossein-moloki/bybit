using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using TradingBot.Application.Interfaces;
using TradingBot.Exchange.Bybit;
using TradingBot.Exchange.Bybit.Dtos;
using TradingBot.Exchange.Bybit.Services;
using Xunit;

namespace TradingBot.UnitTests.Services;

public class BybitTimeProviderTests
{
    private readonly Mock<ILogger<BybitTimeProvider>> _loggerMock = new();

    private Mock<IHttpClientFactory> CreateHttpClientFactoryMock(HttpResponseMessage response)
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(response);

        var httpClient = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("BybitTimeProvider")).Returns(httpClient);

        return factoryMock;
    }

    [Fact]
    public async Task SyncTimeAsync_WithPositiveOffset_ShouldCalculateCorrectOffset()
    {
        // Arrange
        var localUtcNowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var serverTimeMs = localUtcNowMs + 30000; // Bybit is 30 seconds ahead

        var bybitResponse = new BybitResponse<BybitServerTime>
        {
            RetCode = 0,
            RetMsg = "OK",
            Time = serverTimeMs,
            Result = new BybitServerTime
            {
                TimeSecond = (serverTimeMs / 1000).ToString(),
                TimeNano = (serverTimeMs * 1_000_000L).ToString()
            }
        };

        var json = JsonSerializer.Serialize(bybitResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        var factoryMock = CreateHttpClientFactoryMock(httpResponse);
        var settings = new BybitSettings
        {
            TimeSync = new BybitTimeSyncOptions { Enabled = true }
        };

        var provider = new BybitTimeProvider(factoryMock.Object, settings, _loggerMock.Object);

        // Act
        await provider.SyncTimeAsync();

        // Assert
        provider.OffsetMilliseconds.Should().BeInRange(29000, 31000);

        var currentMs = provider.GetCurrentMilliseconds();
        currentMs.Should().BeGreaterThanOrEqualTo(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 29000);
    }

    [Fact]
    public async Task SyncTimeAsync_WithNegativeOffset_ShouldCalculateCorrectOffset()
    {
        // Arrange
        var localUtcNowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var serverTimeMs = localUtcNowMs - 15000; // Bybit is 15 seconds behind

        var bybitResponse = new BybitResponse<BybitServerTime>
        {
            RetCode = 0,
            RetMsg = "OK",
            Time = serverTimeMs,
            Result = new BybitServerTime
            {
                TimeSecond = (serverTimeMs / 1000).ToString(),
                TimeNano = (serverTimeMs * 1_000_000L).ToString()
            }
        };

        var json = JsonSerializer.Serialize(bybitResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        var factoryMock = CreateHttpClientFactoryMock(httpResponse);
        var settings = new BybitSettings
        {
            TimeSync = new BybitTimeSyncOptions { Enabled = true }
        };

        var provider = new BybitTimeProvider(factoryMock.Object, settings, _loggerMock.Object);

        // Act
        await provider.SyncTimeAsync();

        // Assert
        provider.OffsetMilliseconds.Should().BeInRange(-16000, -14000);

        var currentMs = provider.GetCurrentMilliseconds();
        currentMs.Should().BeLessThanOrEqualTo(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 14000);
    }

    [Fact]
    public async Task SyncTimeAsync_WhenBybitApiFails_ShouldRetainPreviousOffsetWithoutCrashing()
    {
        // Arrange: Step 1 - Successful initial sync (+5000ms)
        var localUtcNowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var initialServerMs = localUtcNowMs + 5000;

        var bybitResponse = new BybitResponse<BybitServerTime>
        {
            RetCode = 0,
            Time = initialServerMs,
            Result = new BybitServerTime { TimeNano = (initialServerMs * 1_000_000L).ToString() }
        };

        var initialHttpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(bybitResponse), System.Text.Encoding.UTF8, "application/json")
        };

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(initialHttpResponse)
            .ThrowsAsync(new HttpRequestException("Bybit API Temporarily Unavailable"));

        var httpClient = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("BybitTimeProvider")).Returns(httpClient);

        var settings = new BybitSettings
        {
            TimeSync = new BybitTimeSyncOptions { Enabled = true }
        };

        var provider = new BybitTimeProvider(factoryMock.Object, settings, _loggerMock.Object);

        // First Sync
        await provider.SyncTimeAsync();
        var firstOffset = provider.OffsetMilliseconds;
        firstOffset.Should().BeInRange(4000, 6000);

        // Second Sync (Fails)
        var act = async () => await provider.SyncTimeAsync();
        await act.Should().NotThrowAsync();

        // Offset should still equal the first valid offset
        provider.OffsetMilliseconds.Should().Be(firstOffset);
    }

    [Fact]
    public async Task SyncTimeAsync_WhenDisabled_ShouldNotPerformHttpCallAndKeepZeroOffset()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var httpClient = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("BybitTimeProvider")).Returns(httpClient);

        var settings = new BybitSettings
        {
            TimeSync = new BybitTimeSyncOptions { Enabled = false }
        };

        var provider = new BybitTimeProvider(factoryMock.Object, settings, _loggerMock.Object);

        // Act
        await provider.SyncTimeAsync();

        // Assert
        provider.OffsetMilliseconds.Should().Be(0);
        handlerMock.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public void GetCurrentMilliseconds_WithCustomTimeProvider_ShouldReturnLocalPlusOffset()
    {
        // Arrange
        var timeProviderMock = new Mock<IExchangeTimeProvider>();
        timeProviderMock.Setup(tp => tp.GetCurrentMilliseconds()).Returns(1700000000000L);

        // Act
        var timestamp = timeProviderMock.Object.GetCurrentMilliseconds();

        // Assert
        timestamp.Should().Be(1700000000000L);
    }
}
