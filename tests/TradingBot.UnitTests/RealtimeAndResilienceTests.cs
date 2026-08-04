using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TradingBot.Application.Enums;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Streams;
using TradingBot.Application.Models.Events;
using TradingBot.Domain.Enums;
using TradingBot.Exchange.Bybit;
using TradingBot.Exchange.Bybit.Streams;
using TradingBot.Exchange.Bybit.WebSocket;
using TradingBot.Infrastructure.Resilience;
using Xunit;

namespace TradingBot.UnitTests;

public class RealtimeAndResilienceTests
{
    private readonly Mock<ILogger<ResilienceService>> _loggerMock = new();

    [Fact]
    public async Task ResilienceService_ShouldRetryOnTransientHttpFailure_AndEventuallySucceed()
    {
        // Arrange
        var service = new ResilienceService(_loggerMock.Object);
        int attempts = 0;

        Func<CancellationToken, Task<string>> action = ct =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new System.Net.Http.HttpRequestException("Simulated transient network failure.");
            }
            return Task.FromResult("Success");
        };

        // Act
        var result = await service.ExecuteHttpAsync(action, CancellationToken.None);

        // Assert
        attempts.Should().Be(3);
        result.Should().Be("Success");
    }

    [Fact]
    public async Task SubscriptionManager_ShouldTrackSubscriptions_AndClearCorrectly()
    {
        // Arrange
        var manager = new SubscriptionManager();

        // Act
        manager.AddPublicSubscription("tickers.BTCUSDT");
        manager.AddPublicSubscription("tickers.ETHUSDT");
        manager.AddPrivateSubscription("order");

        // Assert
        manager.GetPublicSubscriptions().Should().HaveCount(2).And.Contain(new[] { "tickers.BTCUSDT", "tickers.ETHUSDT" });
        manager.GetPrivateSubscriptions().Should().ContainSingle().And.Contain("order");

        manager.Clear();
        manager.GetPublicSubscriptions().Should().BeEmpty();
        manager.GetPrivateSubscriptions().Should().BeEmpty();
    }

    [Fact]
    public async Task MessageHandler_ShouldParseTickerMessage_AndPushToMarketStream()
    {
        // Arrange
        var serviceProviderMock = new Mock<IServiceProvider>();
        var marketStream = new BybitMarketStream(serviceProviderMock.Object);
        var orderStream = new BybitOrderStream(serviceProviderMock.Object);
        var positionStream = new BybitPositionStream(serviceProviderMock.Object);

        var loggerMock = new Mock<ILogger<MessageHandler>>();
        var handler = new MessageHandler(marketStream, orderStream, positionStream, loggerMock.Object);

        var tickerJson = @"
        {
          ""topic"": ""tickers.BTCUSDT"",
          ""ts"": 1673853746000,
          ""type"": ""snapshot"",
          ""data"": {
            ""symbol"": ""BTCUSDT"",
            ""lastPrice"": ""20543.50"",
            ""highPrice24h"": ""20600.00"",
            ""lowPrice24h"": ""19800.00"",
            ""prevPrice24h"": ""19900.00"",
            ""volume24h"": ""12030.12"",
            ""turnover24h"": ""243000.00"",
            ""price24hPcnt"": ""0.03"",
            ""bid1Price"": ""20542.50"",
            ""bid1Size"": ""0.5"",
            ""ask1Price"": ""20544.50"",
            ""ask1Size"": ""1.2""
          }
        }";

        // Act
        await handler.HandleMessageAsync(tickerJson, CancellationToken.None);

        // Retrieve event from stream
        using var cts = new CancellationTokenSource(1000);
        var events = new List<MarketTickerUpdateEvent>();
        await foreach (var ev in marketStream.ReceiveEventsAsync(cts.Token))
        {
            events.Add(ev);
            break; // We only expect one
        }

        // Assert
        events.Should().ContainSingle();
        var tickerEvent = events.First();
        tickerEvent.Symbol.Should().Be("BTCUSDT");
        tickerEvent.Price.Should().Be(20543.50m);
        tickerEvent.BidPrice.Should().Be(20542.50m);
        tickerEvent.AskPrice.Should().Be(20544.50m);
        tickerEvent.Volume.Should().Be(12030.12m);
    }

    [Fact]
    public async Task MessageHandler_ShouldParseOrderMessage_AndPushToOrderStream()
    {
        // Arrange
        var serviceProviderMock = new Mock<IServiceProvider>();
        var marketStream = new BybitMarketStream(serviceProviderMock.Object);
        var orderStream = new BybitOrderStream(serviceProviderMock.Object);
        var positionStream = new BybitPositionStream(serviceProviderMock.Object);

        var loggerMock = new Mock<ILogger<MessageHandler>>();
        var handler = new MessageHandler(marketStream, orderStream, positionStream, loggerMock.Object);

        var orderJson = @"
        {
          ""topic"": ""order"",
          ""id"": ""some_id"",
          ""creationTime"": 1672349382000,
          ""data"": [
            {
              ""category"": ""spot"",
              ""orderId"": ""1312312"",
              ""orderLinkId"": ""BOT-12345"",
              ""symbol"": ""BTCUSDT"",
              ""price"": ""20543.00"",
              ""qty"": ""0.025"",
              ""side"": ""Buy"",
              ""orderType"": ""Limit"",
              ""orderStatus"": ""New"",
              ""cumExecQty"": ""0.01"",
              ""cumExecValue"": ""205.43"",
              ""cumExecFee"": ""0.0001"",
              ""rejectReason"": ""EC_NoError""
            }
          ]
        }";

        // Act
        await handler.HandleMessageAsync(orderJson, CancellationToken.None);

        // Retrieve event from stream
        using var cts = new CancellationTokenSource(1000);
        var events = new List<OrderUpdateEvent>();
        await foreach (var ev in orderStream.ReceiveEventsAsync(cts.Token))
        {
            events.Add(ev);
            break;
        }

        // Assert
        events.Should().ContainSingle();
        var orderEvent = events.First();
        orderEvent.ClientOrderId.Should().Be("BOT-12345");
        orderEvent.ExchangeOrderId.Should().Be("1312312");
        orderEvent.Symbol.Should().Be("BTCUSDT");
        orderEvent.Status.Should().Be(OrderStatus.Accepted); // NEW is mapped to Accepted
        orderEvent.Price.Should().Be(20543.00m);
        orderEvent.Quantity.Should().Be(0.025m);
        orderEvent.FilledQuantity.Should().Be(0.01m);
    }

    [Fact]
    public async Task MessageHandler_ShouldParsePositionMessage_AndPushToPositionStream()
    {
        // Arrange
        var serviceProviderMock = new Mock<IServiceProvider>();
        var marketStream = new BybitMarketStream(serviceProviderMock.Object);
        var orderStream = new BybitOrderStream(serviceProviderMock.Object);
        var positionStream = new BybitPositionStream(serviceProviderMock.Object);

        var loggerMock = new Mock<ILogger<MessageHandler>>();
        var handler = new MessageHandler(marketStream, orderStream, positionStream, loggerMock.Object);

        var positionJson = @"
        {
          ""topic"": ""position"",
          ""id"": ""pos_id"",
          ""creationTime"": 1672349382000,
          ""data"": [
            {
              ""symbol"": ""BTCUSDT"",
              ""size"": ""0.025"",
              ""entryPrice"": ""20543.00"",
              ""side"": ""Buy"",
              ""leverage"": ""10"",
              ""positionStatus"": ""Normal""
            }
          ]
        }";

        // Act
        await handler.HandleMessageAsync(positionJson, CancellationToken.None);

        // Retrieve event from stream
        using var cts = new CancellationTokenSource(1000);
        var events = new List<PositionUpdateEvent>();
        await foreach (var ev in positionStream.ReceiveEventsAsync(cts.Token))
        {
            events.Add(ev);
            break;
        }

        // Assert
        events.Should().ContainSingle();
        var positionEvent = events.First();
        positionEvent.Symbol.Should().Be("BTCUSDT");
        positionEvent.Size.Should().Be(0.025m);
        positionEvent.EntryPrice.Should().Be(20543.00m);
        positionEvent.Side.Should().Be("Buy");
        positionEvent.Leverage.Should().Be(10m);
    }
}
