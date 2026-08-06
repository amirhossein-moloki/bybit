using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Application.Models;
using TradingBot.Application.Services;
using TradingBot.Telegram.Models;
using Xunit;

namespace TradingBot.UnitTests.Services;

public class MessageFilterServiceTests
{
    private readonly Mock<ILogger<MessageFilterService>> _loggerMock;
    private readonly SignalDetectionSettings _settings;
    private readonly IOptions<SignalDetectionSettings> _options;

    public MessageFilterServiceTests()
    {
        _loggerMock = new Mock<ILogger<MessageFilterService>>();
        _settings = new SignalDetectionSettings();
        _options = Options.Create(_settings);
    }

    [Fact]
    public async Task AnalyzeAsync_WithValidSignal_ShouldReturnCandidateWithHighScore()
    {
        // Arrange
        var service = new MessageFilterService(_loggerMock.Object, _options);
        var message = new TelegramMessageDto
        {
            ChannelId = 12345,
            ChannelName = "Crypto Alerts",
            MessageId = 1,
            Text = "🚀 BTC LONG \nEntry: 60000 \nSL: 59000",
            Date = DateTime.UtcNow
        };

        // Act
        var result = await service.AnalyzeAsync(message);

        // Assert
        result.Should().NotBeNull();
        result!.ChannelId.Should().Be(message.ChannelId);
        result.MessageId.Should().Be(message.MessageId);
        result.RawText.Should().Be(message.Text);
        result.DetectedSymbol.Should().Be("BTCUSDT");
        result.DetectedSide.Should().Be("LONG");
        result.DetectionScore.Should().Be(100); // Symbol (30) + Side (30) + Price (20) + Risk (20)
    }

    [Fact]
    public async Task AnalyzeAsync_WithInvalidMessage_ShouldReturnNull()
    {
        // Arrange
        var service = new MessageFilterService(_loggerMock.Object, _options);
        var message = new TelegramMessageDto
        {
            ChannelId = 12345,
            ChannelName = "Crypto Alerts",
            MessageId = 2,
            Text = "Hello everyone, hope you are having a great day!",
            Date = DateTime.UtcNow
        };

        // Act
        var result = await service.AnalyzeAsync(message);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("LONG", "LONG", 60)]
    [InlineData("BUY", "LONG", 80)] // BUY is both Direction (+30) and Price Indicator (+20) -> 30 + 30 + 20 = 80
    [InlineData("BULLISH", "LONG", 60)]
    [InlineData("🟢", "LONG", 60)]
    [InlineData("SHORT", "SHORT", 60)]
    [InlineData("SELL", "SHORT", 80)] // SELL is both Direction (+30) and Price Indicator (+20) -> 30 + 30 + 20 = 80
    [InlineData("BEARISH", "SHORT", 60)]
    [InlineData("🔴", "SHORT", 60)]
    public async Task AnalyzeAsync_ShouldDetectCorrectSide(string keyword, string expectedSide, int expectedScore)
    {
        // Arrange
        var service = new MessageFilterService(_loggerMock.Object, _options);
        var message = new TelegramMessageDto
        {
            ChannelId = 12345,
            ChannelName = "Crypto Alerts",
            MessageId = 3,
            Text = $"BTC {keyword}",
            Date = DateTime.UtcNow
        };

        // Act
        var result = await service.AnalyzeAsync(message);

        // Assert
        result.Should().NotBeNull();
        result!.DetectedSide.Should().Be(expectedSide);
        result.DetectionScore.Should().Be(expectedScore);
    }

    [Theory]
    [InlineData("BTC", "BTCUSDT")]
    [InlineData("ETH", "ETHUSDT")]
    [InlineData("BTCUSDT", "BTCUSDT")]
    [InlineData("ethusdt", "ETHUSDT")]
    public async Task AnalyzeAsync_ShouldDetectAndMapCorrectSymbol(string rawSymbolText, string expectedSymbol)
    {
        // Arrange
        var service = new MessageFilterService(_loggerMock.Object, _options);
        var message = new TelegramMessageDto
        {
            ChannelId = 12345,
            ChannelName = "Crypto Alerts",
            MessageId = 4,
            Text = $"{rawSymbolText} BUY", // Symbol (30) + Side (30) + Price (20) = 80
            Date = DateTime.UtcNow
        };

        // Act
        var result = await service.AnalyzeAsync(message);

        // Assert
        result.Should().NotBeNull();
        result!.DetectedSymbol.Should().Be(expectedSymbol);
    }

    [Fact]
    public async Task AnalyzeAsync_WithEmptyOrNullMessage_ShouldReturnNullAndNotCrash()
    {
        // Arrange
        var service = new MessageFilterService(_loggerMock.Object, _options);

        // Act & Assert
        var resultNullDto = await service.AnalyzeAsync(null!);
        resultNullDto.Should().BeNull();

        var messageEmptyText = new TelegramMessageDto { Text = "" };
        var resultEmptyText = await service.AnalyzeAsync(messageEmptyText);
        resultEmptyText.Should().BeNull();

        var messageWhitespace = new TelegramMessageDto { Text = "   " };
        var resultWhitespace = await service.AnalyzeAsync(messageWhitespace);
        resultWhitespace.Should().BeNull();
    }

    [Fact]
    public async Task AnalyzeAsync_WithScoreBelowThreshold_ShouldReturnNull()
    {
        // Arrange
        var service = new MessageFilterService(_loggerMock.Object, _options);
        var message = new TelegramMessageDto
        {
            ChannelId = 12345,
            ChannelName = "Crypto Alerts",
            MessageId = 5,
            Text = "BTC Only", // Symbol only -> Score 30, which is < 60
            Date = DateTime.UtcNow
        };

        // Act
        var result = await service.AnalyzeAsync(message);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task AnalyzeAsync_WithCustomRulesConfigured_ShouldRespectThem()
    {
        // Arrange
        _settings.MinimumScore = 50; // Lower min score
        _settings.DetectionRules.Custom.LongKeywords.Add("MOON");
        _settings.DetectionRules.Custom.PriceKeywords.Add("COST");

        var service = new MessageFilterService(_loggerMock.Object, _options);
        var message = new TelegramMessageDto
        {
            ChannelId = 12345,
            ChannelName = "Crypto Alerts",
            MessageId = 6,
            Text = "SOL MOON COST", // SOL (30) + MOON (30) + COST (20) = 80
            Date = DateTime.UtcNow
        };

        // Act
        var result = await service.AnalyzeAsync(message);

        // Assert
        result.Should().NotBeNull();
        result!.DetectedSymbol.Should().Be("SOLUSDT");
        result.DetectedSide.Should().Be("LONG");
        result.DetectionScore.Should().Be(80);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenBothSidesPresent_ShouldChooseEarliestKeyword()
    {
        // Arrange
        var service = new MessageFilterService(_loggerMock.Object, _options);
        var message = new TelegramMessageDto
        {
            ChannelId = 12345,
            ChannelName = "Crypto Alerts",
            MessageId = 7,
            Text = "BTC LONG but wait, we might SELL if it drops.", // LONG is at index 4, SELL is at index 30.
            Date = DateTime.UtcNow
        };

        // Act
        var result = await service.AnalyzeAsync(message);

        // Assert
        result.Should().NotBeNull();
        result!.DetectedSide.Should().Be("LONG");
    }
}
