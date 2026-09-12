using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using TradingBot.Application.Monitoring;
using TradingBot.Telegram;
using TradingBot.Telegram.Client;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Interfaces;
using Xunit;

namespace TradingBot.UnitTests.Telegram;

public class TelegramBotClientTests
{
    [Fact]
    public async Task SendTextMessageAsync_ShouldReturnSuccess_WhenApiReturns200Ok()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":123}}")
            })
            .Verifiable();

        var httpClient = new HttpClient(handlerMock.Object);
        var loggerMock = new Mock<ILogger<TelegramBotClient>>();
        var botClient = new TelegramBotClient(httpClient, loggerMock.Object);

        // Act
        var result = await botClient.SendTextMessageAsync("mock_bot_token_12345", "-1001234567890", "Test notification message");

        // Assert
        result.Success.Should().BeTrue();
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains("https://api.telegram.org/botmock_bot_token_12345/sendMessage")),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task SendTextMessageAsync_ShouldReturnPermanentFailure_WhenChatNotFound()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent("{\"ok\":false,\"error_code\":400,\"description\":\"Bad Request: chat not found\"}")
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var botClient = new TelegramBotClient(httpClient, Mock.Of<ILogger<TelegramBotClient>>());

        // Act
        var result = await botClient.SendTextMessageAsync("token123", "-99999", "Test");

        // Assert
        result.Success.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
        result.ErrorCode.Should().Be("CHAT_NOT_FOUND");
    }

    [Fact]
    public async Task SendTextMessageAsync_ShouldReturnRetryableFailure_WhenRateLimited429()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = (HttpStatusCode)429,
                Content = new StringContent("{\"ok\":false,\"error_code\":429,\"description\":\"Too Many Requests: retry after 5\"}")
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var botClient = new TelegramBotClient(httpClient, Mock.Of<ILogger<TelegramBotClient>>());

        // Act
        var result = await botClient.SendTextMessageAsync("token123", "-100123", "Test");

        // Assert
        result.Success.Should().BeFalse();
        result.IsRetryable.Should().BeTrue();
        result.ErrorCode.Should().Be("FLOOD_WAIT");
    }

    [Fact]
    public async Task SendTextMessageAsync_ShouldReturnFailure_WhenBotTokenOrRecipientMissing()
    {
        // Arrange
        var botClient = new TelegramBotClient(new HttpClient(), Mock.Of<ILogger<TelegramBotClient>>());

        // Act & Assert
        var resultNoToken = await botClient.SendTextMessageAsync("", "-100123", "Test");
        resultNoToken.Success.Should().BeFalse();
        resultNoToken.ErrorCode.Should().Be("MISSING_BOT_TOKEN");

        var resultNoRecipient = await botClient.SendTextMessageAsync("token123", "", "Test");
        resultNoRecipient.Success.Should().BeFalse();
        resultNoRecipient.ErrorCode.Should().Be("INVALID_RECIPIENT");
    }

    [Fact]
    public async Task TelegramNotificationChannel_ShouldUseBotClient_WhenBotTokenIsConfigured()
    {
        // Arrange
        var mockUserClient = new Mock<ITelegramClient>();
        var mockBotClient = new Mock<ITelegramBotClient>();

        mockBotClient
            .Setup(b => b.SendTextMessageAsync("my_bot_token", "-100123456789", "Hello Bot", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotificationDeliveryResult.AsSuccess());

        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions
        {
            Enabled = true,
            BotToken = "my_bot_token"
        });

        var channel = new TelegramNotificationChannel(
            mockUserClient.Object,
            options,
            Mock.Of<ILogger<TelegramNotificationChannel>>(),
            mockBotClient.Object);

        var notification = new TradingBot.Domain.Entities.Notification(
            eventId: Guid.NewGuid(),
            eventType: "OrderFilled",
            severity: "INFO",
            channel: "Telegram",
            recipient: "-100123456789",
            title: "Order Filled",
            message: "Hello Bot"
        );

        // Act
        var result = await channel.SendAsync(notification);

        // Assert
        result.Success.Should().BeTrue();
        mockBotClient.Verify(b => b.SendTextMessageAsync("my_bot_token", "-100123456789", "Hello Bot", It.IsAny<CancellationToken>()), Times.Once);
        mockUserClient.Verify(u => u.SendMessageAsync(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TelegramNotificationChannel_ShouldFallbackToUserClient_WhenBotTokenIsEmpty()
    {
        // Arrange
        var mockUserClient = new Mock<ITelegramClient>();
        mockUserClient.Setup(u => u.IsConnected()).Returns(true);
        mockUserClient.Setup(u => u.SendMessageAsync(100200300L, "Hello User")).Returns(Task.CompletedTask);

        var mockBotClient = new Mock<ITelegramBotClient>();

        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions
        {
            Enabled = true,
            BotToken = "" // Empty BotToken -> fallback
        });

        var channel = new TelegramNotificationChannel(
            mockUserClient.Object,
            options,
            Mock.Of<ILogger<TelegramNotificationChannel>>(),
            mockBotClient.Object);

        var notification = new TradingBot.Domain.Entities.Notification(
            eventId: Guid.NewGuid(),
            eventType: "OrderFilled",
            severity: "INFO",
            channel: "Telegram",
            recipient: "100200300",
            title: "Order Filled",
            message: "Hello User"
        );

        // Act
        var result = await channel.SendAsync(notification);

        // Assert
        result.Success.Should().BeTrue();
        mockUserClient.Verify(u => u.SendMessageAsync(100200300L, "Hello User"), Times.Once);
        mockBotClient.Verify(b => b.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
