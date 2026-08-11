using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using FluentAssertions;
using Xunit;
using TradingBot.Application.Repositories;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Events;
using TradingBot.Domain.SignalIntelligence.Interfaces;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Models;
using TradingBot.Parser.Parsers;

namespace TradingBot.UnitTests.Parser;

public class StructuredSignalExtractorTests
{
    private readonly Mock<ISignalExtractionRepository> _repositoryMock;
    private readonly Mock<IIntelligenceEventPublisher> _eventPublisherMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<ILogger<StructuredSignalExtractor>> _loggerMock;
    private readonly IOptions<ExtractionRulesOptions> _options;

    private readonly StructuredSignalExtractor _extractor;

    public StructuredSignalExtractorTests()
    {
        _repositoryMock = new Mock<ISignalExtractionRepository>();
        _eventPublisherMock = new Mock<IIntelligenceEventPublisher>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _loggerMock = new Mock<ILogger<StructuredSignalExtractor>>();
        _options = Microsoft.Extensions.Options.Options.Create(new ExtractionRulesOptions());

        _extractor = new StructuredSignalExtractor(
            _repositoryMock.Object,
            _eventPublisherMock.Object,
            _unitOfWorkMock.Object,
            _options,
            _loggerMock.Object
        );
    }

    private TelegramMessage CreateMessage(string content)
    {
        return new TelegramMessage(12345678, 987654, 111222, content, DateTime.UtcNow);
    }

    [Fact]
    public void NormalizeText_ShouldHandleFullWidthAndPersianDigits()
    {
        // Arrange
        string raw = "  ＳＥＬＬ  EUR/USD  \nEntry: ۵۴۳۲۱\n";

        // Act
        string normalized = StructuredSignalExtractor.NormalizeText(raw);

        // Assert
        normalized.Should().Be("SELL EUR/USD\nEntry: 54321");
    }

    [Theory]
    [InlineData("EURUSD", "EURUSD")]
    [InlineData("EUR/USD", "EURUSD")]
    [InlineData("BTCUSDT", "BTCUSDT")]
    [InlineData("BTC-USDT", "BTCUSDT")]
    [InlineData("GOLD", "XAUUSD")]
    public async Task ExtractAsync_ShouldExtractAndNormalizeSymbols(string rawSymbol, string expectedNormalized)
    {
        // Arrange
        var msg = CreateMessage($"{rawSymbol} LONG\nEntry: 1.1500\nSL: 1.1400");

        // Act
        var result = await _extractor.ExtractAsync(msg);

        // Assert
        result.Symbol.Should().Be(expectedNormalized);
        result.Success.Should().BeTrue();
    }

    [Theory]
    [InlineData("BUY", TradeSide.BUY)]
    [InlineData("LONG", TradeSide.BUY)]
    [InlineData("شراء", TradeSide.BUY)]
    [InlineData("لانگ", TradeSide.BUY)]
    [InlineData("SELL", TradeSide.SELL)]
    [InlineData("SHORT", TradeSide.SELL)]
    [InlineData("فروش", TradeSide.SELL)]
    [InlineData("شورت", TradeSide.SELL)]
    public async Task ExtractAsync_ShouldExtractSidesCorrectly(string rawSide, TradeSide expectedSide)
    {
        // Arrange
        var entryPrice = expectedSide == TradeSide.BUY ? "1.1500\nSL: 1.1400" : "1.1500\nSL: 1.1600";
        var msg = CreateMessage($"EURUSD {rawSide}\nEntry: {entryPrice}");

        // Act
        var result = await _extractor.ExtractAsync(msg);

        // Assert
        result.Side.Should().Be(expectedSide);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_ShouldExtractSinglePriceAndRangeEntry()
    {
        // Arrange
        var singleMsg = CreateMessage("BTCUSDT LONG\nEntry: 60000\nSL: 59000");
        var rangeMsg = CreateMessage("BTCUSDT LONG\nENTRY ZONE: 60000-60500\nSL: 59000");

        // Act
        var singleResult = await _extractor.ExtractAsync(singleMsg);
        var rangeResult = await _extractor.ExtractAsync(rangeMsg);

        // Assert
        singleResult.EntryPrice.Should().Be(60000m);
        singleResult.Metadata.Should().NotContainKey("EntryRangeMin");

        rangeResult.EntryPrice.Should().Be(60000m);
        rangeResult.Metadata["EntryRangeMin"].Should().Be("60000");
        rangeResult.Metadata["EntryRangeMax"].Should().Be("60500");
    }

    [Fact]
    public async Task ExtractAsync_ShouldRejectInvalidEntryFormat()
    {
        // Arrange
        var msg = CreateMessage("BTCUSDT LONG\nEntry: abc\nSL: 59000");

        // Act
        var result = await _extractor.ExtractAsync(msg);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be(ExtractionValidationStatus.Partial);
        result.Errors.Should().Contain(e => e.Contains("Extraction Failed: Invalid entry number 'abc'"));
    }

    [Fact]
    public async Task ExtractAsync_ShouldExtractStopLossAndValidateBoundaries()
    {
        // Arrange
        var validBuyMsg = CreateMessage("BTCUSDT LONG\nEntry: 60000\nSL: 59000");
        var invalidBuyMsg = CreateMessage("BTCUSDT LONG\nEntry: 60000\nSL: 61000");

        // Act
        var validResult = await _extractor.ExtractAsync(validBuyMsg);
        var invalidResult = await _extractor.ExtractAsync(invalidBuyMsg);

        // Assert
        validResult.StopLoss.Should().Be(59000m);
        validResult.Success.Should().BeTrue();

        invalidResult.StopLoss.Should().Be(61000m);
        invalidResult.Success.Should().BeFalse();
        invalidResult.Errors.Should().Contain(e => e.Contains("Stop Loss must be less than Entry Price"));
    }

    [Fact]
    public async Task ExtractAsync_ShouldExtractMultipleTakeProfitsAndRejectDuplicates()
    {
        // Arrange
        var msg = CreateMessage("BTCUSDT LONG\nEntry: 60000\nTP1 62000\nTP2 64000\nTP3 62000\nSL: 59000");

        // Act
        var result = await _extractor.ExtractAsync(msg);

        // Assert
        result.TakeProfits.Should().HaveCount(2);
        result.TakeProfits[0].Target.Should().Be(1);
        result.TakeProfits[0].Price.Should().Be(62000m);
        result.TakeProfits[1].Target.Should().Be(2);
        result.TakeProfits[1].Price.Should().Be(64000m);
        result.Errors.Should().Contain(e => e.Contains("Duplicate TP price detected and skipped"));
    }

    [Fact]
    public async Task ExtractAsync_ShouldExtractLeverage()
    {
        // Arrange
        var msg1 = CreateMessage("BTCUSDT LONG\nEntry: 60000\nSL: 59000\nLeverage: 50");
        var msg2 = CreateMessage("BTCUSDT LONG\nEntry: 60000\nSL: 59000\n25x");

        // Act
        var result1 = await _extractor.ExtractAsync(msg1);
        var result2 = await _extractor.ExtractAsync(msg2);

        // Assert
        result1.Leverage.Should().Be(50m);
        result2.Leverage.Should().Be(25m);
    }

    [Fact]
    public async Task ExtractAsync_ShouldPersistResultsAndPublishEvents()
    {
        // Arrange
        var msg = CreateMessage("BTCUSDT LONG\nEntry: 60000\nTP1 62000\nTP2 64000\nSL: 59000");

        // Act
        var result = await _extractor.ExtractAsync(msg);

        // Assert
        result.Success.Should().BeTrue();
        result.Confidence.Should().Be(1.0m);

        // Verify DB persistence was called
        _repositoryMock.Verify(r => r.CreateAsync(It.Is<SignalExtraction>(e =>
            e.Symbol == "BTCUSDT" &&
            e.Side == "BUY" &&
            e.EntryPrice == 60000m &&
            e.StopLoss == 59000m &&
            e.Status == "Valid"
        ), It.IsAny<CancellationToken>()), Times.Once);

        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Verify Event publishing was called
        _eventPublisherMock.Verify(p => p.PublishAsync(It.IsAny<SignalExtractionStarted>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisherMock.Verify(p => p.PublishAsync(It.IsAny<SignalExtracted>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisherMock.Verify(p => p.PublishAsync(It.IsAny<SignalExtractionFailed>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_ShouldCalculateConfidenceCorrectly()
    {
        // Arrange
        var completeMsg = CreateMessage("BTCUSDT LONG\nEntry 60000\nTP1 62000\nSL 59000");
        var missingTpMsg = CreateMessage("BTCUSDT LONG\nEntry 60000\nSL 59000");
        var onlySymbolMsg = CreateMessage("BTCUSDT");

        // Act
        var completeResult = await _extractor.ExtractAsync(completeMsg);
        var missingTpResult = await _extractor.ExtractAsync(missingTpMsg);
        var onlySymbolResult = await _extractor.ExtractAsync(onlySymbolMsg);

        // Assert
        completeResult.Confidence.Should().Be(1.0m);
        missingTpResult.Confidence.Should().Be(0.8m);
        onlySymbolResult.Confidence.Should().Be(0.3m);
    }

    [Fact]
    public async Task ExtractAsync_ShouldHandleFailureCasesGracefully()
    {
        // Arrange
        var emptyMsg = CreateMessage(".");
        var randomMsg = CreateMessage("Just talking about the weather and market movements today.");

        // Act
        var emptyResult = await _extractor.ExtractAsync(emptyMsg);
        var randomResult = await _extractor.ExtractAsync(randomMsg);

        // Assert
        emptyResult.Success.Should().BeFalse();
        emptyResult.Status.Should().Be(ExtractionValidationStatus.Invalid);

        randomResult.Success.Should().BeFalse();
        randomResult.Status.Should().NotBe(ExtractionValidationStatus.Valid);

        // Verify failure events were published
        _eventPublisherMock.Verify(p => p.PublishAsync(It.IsAny<SignalExtractionFailed>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExtractAsync_ShouldThrowOnNullMessage()
    {
        // Act
        Func<Task> act = async () => await _extractor.ExtractAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExtractAsync_ShouldProduceFinalDeliverableOutput()
    {
        // Arrange
        var msg = CreateMessage("BTCUSDT LONG\n\nEntry 60000\n\nTP1 62000\n\nTP2 64000\n\nSL 59000");

        // Act
        var result = await _extractor.ExtractAsync(msg);

        // Assert
        result.Success.Should().BeTrue();
        result.Symbol.Should().Be("BTCUSDT");
        result.Side.Should().Be(TradeSide.BUY);
        result.EntryPrice.Should().Be(60000m);
        result.StopLoss.Should().Be(59000m);
        result.TakeProfits.Select(t => t.Price).Should().Equal(62000m, 64000m);
        result.Confidence.Should().Be(1.0m);
    }
}
