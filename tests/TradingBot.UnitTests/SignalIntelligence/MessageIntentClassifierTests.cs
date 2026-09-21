using System;
using System.Threading.Tasks;
using FluentAssertions;
using TradingBot.Application.SignalIntelligence.Parser;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using Xunit;

namespace TradingBot.UnitTests.SignalIntelligence;

public class MessageIntentClassifierTests
{
    private readonly MessageClassifier _classifier;

    public MessageIntentClassifierTests()
    {
        var preprocessor = new MessagePreprocessor();
        _classifier = new MessageClassifier(preprocessor);
    }

    [Fact]
    public async Task ClassifyAsync_WithEntrySignal_ShouldClassifyAsEntrySignal()
    {
        // Arrange
        var content = "BUY BTCUSDT entry 81000 SL 80000 TP 83000";
        var message = new TelegramMessage(1001, 123, 456, content, DateTime.UtcNow);

        // Act
        var analysis = await _classifier.ClassifyAsync(message);

        // Assert
        analysis.Intent.Should().Be(TelegramMessageIntent.EntrySignal);
        analysis.TargetSymbol.Should().Be("BTCUSDT");
        analysis.ConfidenceScore.Should().BeGreaterThanOrEqualTo(0.90m);
    }

    [Theory]
    [InlineData("یورو ریسک فری شده", "EURUSD", "MoveStopLossToEntry")]
    [InlineData("یورو ریسک فری شد", "EURUSD", "MoveStopLossToEntry")]
    [InlineData("سر به سر شد طلا", "XAUUSD", "MoveStopLossToEntry")]
    [InlineData("حد ضرر منتقل شود بیت کوین", "BTCUSDT", "MoveStopLossToEntry")]
    public async Task ClassifyAsync_WithRiskFreeMessage_ShouldClassifyAsPositionManagement(string content, string expectedSymbol, string expectedAction)
    {
        // Arrange
        var message = new TelegramMessage(1001, 124, 456, content, DateTime.UtcNow);

        // Act
        var analysis = await _classifier.ClassifyAsync(message);

        // Assert
        analysis.Intent.Should().Be(TelegramMessageIntent.PositionManagement);
        analysis.Action.Should().Be(expectedAction);
        analysis.TargetSymbol.Should().Be(expectedSymbol);
        analysis.ConfidenceScore.Should().BeGreaterThanOrEqualTo(0.90m);
    }

    [Theory]
    [InlineData("بیت کوین تارگت اول خورد", "BTCUSDT", "TakePartialProfit")]
    [InlineData("تارگت دوم خورد یورو", "EURUSD", "TakePartialProfit")]
    [InlineData("بخشی از معامله بسته شد اتریوم", "ETHUSDT", "TakePartialProfit")]
    [InlineData("بخشی از حجم بسته شود طلا", "XAUUSD", "TakePartialProfit")]
    [InlineData("سیو سود پوند", "GBPUSD", "TakePartialProfit")]
    public async Task ClassifyAsync_WithTakeProfitHitMessage_ShouldClassifyAsPositionManagement(string content, string expectedSymbol, string expectedAction)
    {
        // Arrange
        var message = new TelegramMessage(1001, 125, 456, content, DateTime.UtcNow);

        // Act
        var analysis = await _classifier.ClassifyAsync(message);

        // Assert
        analysis.Intent.Should().Be(TelegramMessageIntent.PositionManagement);
        analysis.Action.Should().Be(expectedAction);
        analysis.TargetSymbol.Should().Be(expectedSymbol);
        analysis.ConfidenceScore.Should().BeGreaterThanOrEqualTo(0.90m);
    }

    [Theory]
    [InlineData("همه معاملات بسته شود", "CloseAllPositions")]
    [InlineData("خروج کامل", "ClosePosition")]
    [InlineData("معامله بسته شد", "ClosePosition")]
    [InlineData("معامله بسته شود", "ClosePosition")]
    public async Task ClassifyAsync_WithExitSignalMessage_ShouldClassifyAsExitSignal(string content, string expectedAction)
    {
        // Arrange
        var message = new TelegramMessage(1001, 126, 456, content, DateTime.UtcNow);

        // Act
        var analysis = await _classifier.ClassifyAsync(message);

        // Assert
        analysis.Intent.Should().Be(TelegramMessageIntent.ExitSignal);
        analysis.Action.Should().Be(expectedAction);
        analysis.ConfidenceScore.Should().BeGreaterThanOrEqualTo(0.90m);
    }

    [Fact]
    public async Task ClassifyAsync_WithNoiseMessage_ShouldClassifyAsNoise()
    {
        // Arrange
        var content = "سلام خوش آمدید به کانال پشتیبانی";
        var message = new TelegramMessage(1001, 127, 456, content, DateTime.UtcNow);

        // Act
        var analysis = await _classifier.ClassifyAsync(message);

        // Assert
        analysis.Intent.Should().Be(TelegramMessageIntent.Noise);
        analysis.Action.Should().Be("None");
        analysis.ProcessingStatus.Should().Be("Filtered");
    }
}
