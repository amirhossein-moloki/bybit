using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Application.SignalIntelligence.Parser;
using TradingBot.Parser.Parsers;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Enums;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Events;
using TradingBot.Domain.SignalIntelligence.Interfaces;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using Xunit;

namespace TradingBot.UnitTests.SignalIntelligence;

public class RuleBasedParserTests
{
    private readonly IMessagePreprocessor _preprocessor = new MessagePreprocessor();
    private readonly IMessageClassifier _classifier;
    private readonly Mock<IMessageAnalysisRepository> _analysisRepoMock = new();
    private readonly Mock<IMessageRepository> _messageRepoMock = new();
    private readonly Mock<ISignalParser> _signalParserMock = new();
    private readonly Mock<IIntelligenceEventPublisher> _eventPublisherMock = new();
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly MessageParser _parser;

    public RuleBasedParserTests()
    {
        _classifier = new MessageClassifier(_preprocessor);
        _parser = new MessageParser(
            _preprocessor,
            _classifier,
            _analysisRepoMock.Object,
            _messageRepoMock.Object,
            _eventPublisherMock.Object,
            _uowMock.Object,
            NullLogger<MessageParser>.Instance,
            _signalParserMock.Object
        );
    }

    [Theory]
    // White spaces and Line breaks
    [InlineData("  BTC   LONG  \r\nEntry: 60000  ", "BTC LONG\nEntry:60000")]
    // Arabic/Persian digits
    [InlineData("ورود: ۱.۱۶۰۷۰", "ورود:1.16070")]
    [InlineData("ورود: ٠.١٢٣٤", "ورود:0.1234")]
    // Arabic kaf and ya
    [InlineData("يورو كنسل", "یورو کنسل")]
    // Separators spacing
    [InlineData("EUR / USD", "EUR/USD")]
    [InlineData("BTC - USDT", "BTC-USDT")]
    [InlineData("ورود : ۶۰۰۰۰", "ورود:60000")]
    public void Preprocessor_ShouldNormalizeTextCorrectly(string input, string expected)
    {
        // Act
        var result = _preprocessor.Preprocess(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    // Signals
    [InlineData("BTCUSDT BUY\nEntry: 60000\nSL: 59000\nTP: 62000", MessageType.SIGNAL)]
    [InlineData("EUR/USD SELL\nورود: 1.1600\nSL: 1.1620\nTP: 1.1580", MessageType.SIGNAL)]
    // Trade updates
    [InlineData("ریسک فری کنید", MessageType.TRADE_UPDATE)]
    [InlineData("سیو سود کنید روی سولانا", MessageType.TRADE_UPDATE)]
    [InlineData("ببندید سولانا را", MessageType.TRADE_UPDATE)]
    [InlineData("UPDATE SL 1.1610", MessageType.TRADE_UPDATE)]
    // Cancel commands
    [InlineData("cancel all orders", MessageType.CANCEL_COMMAND)]
    [InlineData("همه اوردرها کنسله", MessageType.CANCEL_COMMAND)]
    [InlineData("EURUSD cancel", MessageType.CANCEL_COMMAND)]
    // Analysis & Noise
    [InlineData("بازار رنج است منتظر باشید", MessageType.ANALYSIS)]
    [InlineData("تحلیل چارت بیت کوین", MessageType.ANALYSIS)]
    [InlineData("گزارش سود روزانه کانال", MessageType.STATUS_UPDATE)]
    [InlineData("سلام خوش آمدید به کانال ما", MessageType.GENERAL_MESSAGE)]
    [InlineData("فعاله", MessageType.UNKNOWN)]
    public async Task Classifier_ShouldClassifyMessageTypeCorrectly(string input, MessageType expectedType)
    {
        // Arrange
        var msg = new TelegramMessage(12345L, 56789L, null, input, DateTime.UtcNow);

        // Act
        var analysis = await _classifier.ClassifyAsync(msg);

        // Assert
        analysis.MessageType.Should().Be(expectedType);
        if (expectedType == MessageType.UNKNOWN)
        {
            analysis.Confidence.Should().Be(0.0m);
        }
        else
        {
            analysis.Confidence.Should().BeGreaterThan(0.0m);
        }
    }

    [Fact]
    public async Task Parser_ShouldParseEnglishSignalCorrectly()
    {
        // Arrange
        var content = "EUR/USD SELL\nEntry: 1.16070\nSL: 1.16250\nTP1: 1.15890\nTP2: 1.15750";
        var msg = new TelegramMessage(111L, 222L, null, content, DateTime.UtcNow);

        var parsedSignal = new ParsedSignal
        {
            Symbol = "EURUSD",
            Side = OrderSide.Sell,
            EntryPrice = 1.16070m,
            StopLoss = 1.16250m,
            TakeProfits = new List<decimal> { 1.15890m, 1.15750m }
        };

        _signalParserMock
            .Setup(p => p.ParseAsync(It.IsAny<ParserContext>()))
            .ReturnsAsync(ParserResult.SuccessResult(parsedSignal, "1.0"));

        // Act
        var result = await _parser.ParseAsync(msg);

        // Assert
        result.Should().NotBeNull();
        result.Type.Should().Be(MessageType.SIGNAL);
        result.Symbol.Should().Be("EURUSD");
        result.Side.Should().Be(OrderSide.Sell);
        result.Entry.Should().Be(1.16070m);
        result.StopLoss.Should().Be(1.16250m);
        result.TakeProfits.Should().Equal(1.15890m, 1.15750m);
        result.Confidence.Should().Be(1.0m); // 0.5 + 0.15 (Symbol) + 0.15 (Side) + 0.05 (Entry) + 0.05 (SL) + 0.10 (TPs)
        result.Source.Should().Be(ParserSource.RULE_BASED);

        _analysisRepoMock.Verify(r => r.CreateAsync(It.IsAny<MessageAnalysis>(), It.IsAny<CancellationToken>()), Times.Once);
        _messageRepoMock.Verify(r => r.MarkProcessedAsync(msg.Id, It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisherMock.Verify(p => p.PublishAsync(It.IsAny<SignalDetectedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Parser_ShouldParsePersianSignalCorrectly()
    {
        // Arrange
        var content = "🔥 یورو/دلار فروش\nورود: ۱.۱۶۰۷۰\nحد ضرر: ۱.۱۶۲۵۰\nحد سود ۱: ۱.۱۵۸۹۰\nحد سود ۲: ۱.۱۵۷۵۰";
        var msg = new TelegramMessage(111L, 222L, null, content, DateTime.UtcNow);

        // Mock ISignalParser to mimic parsed Persian signal after preprocessing
        var parsedSignal = new ParsedSignal
        {
            Symbol = "EURUSD",
            Side = OrderSide.Sell,
            EntryPrice = 1.16070m,
            StopLoss = 1.16250m,
            TakeProfits = new List<decimal> { 1.15890m, 1.15750m }
        };

        _signalParserMock
            .Setup(p => p.ParseAsync(It.Is<ParserContext>(c => c.RawMessage.Contains("1.16070"))))
            .ReturnsAsync(ParserResult.SuccessResult(parsedSignal, "1.0"));

        // Act
        var result = await _parser.ParseAsync(msg);

        // Assert
        result.Should().NotBeNull();
        result.Type.Should().Be(MessageType.SIGNAL);
        result.Symbol.Should().Be("EURUSD");
        result.Side.Should().Be(OrderSide.Sell);
        result.Entry.Should().Be(1.16070m);
        result.StopLoss.Should().Be(1.16250m);
        result.TakeProfits.Should().Equal(1.15890m, 1.15750m);
        result.Confidence.Should().Be(1.0m);
        result.Source.Should().Be(ParserSource.RULE_BASED);

        _eventPublisherMock.Verify(p => p.PublishAsync(It.IsAny<SignalDetectedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Parser_ShouldParseEntryRangeCorrectly()
    {
        // Arrange
        var content = "BTCUSDT BUY\nEntry: 60000 - 60500\nSL: 59000\nTP: 62000";
        var msg = new TelegramMessage(111L, 222L, null, content, DateTime.UtcNow);

        var parsedSignal = new ParsedSignal
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = null, // Entry range evaluated
            StopLoss = 59000m,
            TakeProfits = new List<decimal> { 62000m }
        };

        _signalParserMock
            .Setup(p => p.ParseAsync(It.IsAny<ParserContext>()))
            .ReturnsAsync(ParserResult.SuccessResult(parsedSignal, "1.0"));

        // Act
        var result = await _parser.ParseAsync(msg);

        // Assert
        result.Should().NotBeNull();
        result.EntryRangeMin.Should().Be(60000m);
        result.EntryRangeMax.Should().Be(60500m);
        result.Confidence.Should().Be(1.00m); // 0.5 + 0.15 (Symbol) + 0.15 (Side) + 0.05 (EntryRange) + 0.05 (SL) + 0.10 (TPs)
    }

    [Fact]
    public async Task Parser_ShouldParseTradeUpdateCorrectly()
    {
        // Arrange
        var content = "ریسک فری کنید";
        var msg = new TelegramMessage(111L, 222L, null, content, DateTime.UtcNow);

        // Act
        var result = await _parser.ParseAsync(msg);

        // Assert
        result.Should().NotBeNull();
        result.Type.Should().Be(MessageType.TRADE_UPDATE);
        result.Action.Should().Be(TradeAction.MOVE_STOP_TO_ENTRY);
        result.Confidence.Should().Be(0.90m);

        _eventPublisherMock.Verify(p => p.PublishAsync(It.IsAny<TradeUpdateDetectedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Parser_ShouldParseCancelCommandCorrectly()
    {
        // Arrange
        var content = "EURUSD cancel";
        var msg = new TelegramMessage(111L, 222L, null, content, DateTime.UtcNow);

        // Act
        var result = await _parser.ParseAsync(msg);

        // Assert
        result.Should().NotBeNull();
        result.Type.Should().Be(MessageType.CANCEL_COMMAND);
        result.Action.Should().Be(TradeAction.CANCEL);
        result.Symbol.Should().Be("EURUSD");
        result.Confidence.Should().Be(0.95m);

        _eventPublisherMock.Verify(p => p.PublishAsync(It.IsAny<CancelCommandDetectedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Parser_ShouldEnforceIdempotency_AndReturnExistingResultWithoutSideEffects()
    {
        // Arrange
        var content = "BTCUSDT BUY\nEntry: 60000\nSL: 59000\nTP: 62000";
        var msg = new TelegramMessage(111L, 222L, null, content, DateTime.UtcNow);

        var existingJson = "{\"type\":\"SIGNAL\",\"symbol\":\"BTCUSDT\",\"side\":\"Buy\",\"entry\":60000.0,\"stop_loss\":59000.0,\"take_profit\":[62000.0],\"confidence\":0.95,\"source\":\"RULE_BASED\"}";
        var existingAnalysis = new MessageAnalysis(msg.Id, MessageType.SIGNAL, 0.95m, existingJson, false, DateTime.UtcNow);

        _analysisRepoMock
            .Setup(r => r.GetByMessageIdAsync(msg.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnalysis);

        // Act
        var result = await _parser.ParseAsync(msg);

        // Assert
        result.Should().NotBeNull();
        result.Type.Should().Be(MessageType.SIGNAL);
        result.Symbol.Should().Be("BTCUSDT");
        result.Side.Should().Be(OrderSide.Buy);
        result.Entry.Should().Be(60000m);
        result.StopLoss.Should().Be(59000m);
        result.TakeProfits.Should().Equal(62000m);
        result.Confidence.Should().Be(0.95m);

        // No side-effects should run
        _analysisRepoMock.Verify(r => r.CreateAsync(It.IsAny<MessageAnalysis>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventPublisherMock.Verify(p => p.PublishAsync(It.IsAny<IIntelligenceEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Parser_ShouldBeRecoverable_WhenExceptionIsThrown()
    {
        // Arrange
        var content = "BTCUSDT BUY Entry 60000";
        var msg = new TelegramMessage(111L, 222L, null, content, DateTime.UtcNow);

        _signalParserMock
            .Setup(p => p.ParseAsync(It.IsAny<ParserContext>()))
            .ThrowsAsync(new Exception("Unexpected DB/Parser Error"));

        // Act
        var result = await _parser.ParseAsync(msg);

        // Assert
        result.Should().NotBeNull();
        result.Type.Should().Be(MessageType.UNKNOWN);
        result.Confidence.Should().Be(0.0m);
        result.ErrorMessage.Should().Contain("Unexpected parser failure");

        // The original message remains un-processed, thus not crash
        msg.Processed.Should().BeFalse();
    }
}
